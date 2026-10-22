using System.Collections.ObjectModel;
using System.Reflection;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.Core.Modules;
using QBSharp.Core.Syntax;

namespace QBSharp.Core.Runtime;

internal sealed partial class ScriptSession : IScriptSession
{
    private sealed class Cell(object? value, object? type, bool readOnly = false)
    { public object? Value = value; public object? Type { get; } = type; public bool ReadOnly { get; } = readOnly; }
    private sealed class Scope(Scope? parent = null, string? qualification = null)
    {
        public string Qualification { get; } = qualification ?? parent?.Qualification ?? "";
        public Dictionary<string, Cell> Cells { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool TryFind(string name, out Cell? cell)
        { if (Cells.TryGetValue(name, out cell)) return true; if (parent is not null) return parent.TryFind(name, out cell); cell = null; return false; }
        public Cell Find(string name) => TryFind(name, out var cell) ? cell! : throw new InvalidOperationException($"Unknown name '{name}'.");
    }
    private sealed record ScriptFunction(FunctionDeclaration Declaration, Scope Closure) : IScriptOpaqueValue;
    private sealed class Returned(object? value) : Exception { public object? Value { get; } = value; }
    private readonly IScriptParser parser;
    private readonly ScriptEngineOptions options;
    private readonly MemberAccess members;
    private readonly IModuleResolver resolver;
    private readonly Scope global = new(), hostScope = new();
    private readonly object gate = new();
    private bool busy, disposed;
    private long steps;
    private int callDepth;
    private CancellationToken cancellation;
    public ScriptSession(IScriptParser parser, ScriptEngineOptions options, MemberAccess members, HostBindings? bindings, IModuleResolver resolver)
    {
        this.parser = parser; this.options = options; this.members = members; this.resolver = resolver;
        foreach (var (name, value) in bindings?.Snapshot() ?? new Dictionary<string, object?>())
        { global.Cells.Add(name, new(value, null)); hostScope.Cells.Add(name, new(value, null)); }
        clrTypes = new(options.ClrTypes, StringComparer.OrdinalIgnoreCase);
    }
    private void Available() { ObjectDisposedException.ThrowIf(disposed, this); if (busy) throw new InvalidOperationException("Session already executing."); }
    public IReadOnlyDictionary<string, object?> Variables
    { get { lock (gate) { Available(); return new ReadOnlyDictionary<string, object?>(global.Cells.ToDictionary(p => p.Key, p => p.Value.Value, StringComparer.OrdinalIgnoreCase)); } } }
    public void SetVariable(string name, object? value)
    {
        var validation = new HostBindings(); validation.SetValue(name, value);
        lock (gate)
        {
            Available();
            if (global.Cells.TryGetValue(name, out var cell)) Set(cell, value);
            else global.Cells.Add(name, new(value, null));
            // Explicit host registrations also become available to modules imported later.
            hostScope.Cells[name] = new(value, null);
        }
    }
    public void RegisterFunction(string name, HostFunction function) { ArgumentNullException.ThrowIfNull(function); SetVariable(name, function); }
    private void Begin(CancellationToken token)
    { lock (gate) { Available(); token.ThrowIfCancellationRequested(); busy = true; cancellation = token; steps = 0; callDepth = 0;activeLoops=[]; } }
    private void Finish() { lock (gate) { busy = false; cancellation = default; } }
    public async ValueTask ExecuteAsync(ScriptSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); Begin(cancellationToken);
        try
        {
            var result = parser.ParseScript(source, cancellationToken); if (!result.Success) throw new ScriptRuntimeException(result.Diagnostics[0]);
            foreach (var statement in result.Root!.Statements) await Execute(statement, global).ConfigureAwait(false);
        }
        finally { Finish(); }
    }
    public async ValueTask<object?> EvaluateAsync(ScriptSource expression, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expression); Begin(cancellationToken);
        try
        { var result = parser.ParseExpression(expression, cancellationToken); if (!result.Success) throw new ScriptRuntimeException(result.Diagnostics[0]); return await Evaluate(result.Root!, global).ConfigureAwait(false); }
        finally { Finish(); }
    }
    public ValueTask DisposeAsync()
    { lock (gate) { if (busy) throw new InvalidOperationException("Cancel and await execution before disposal."); disposed = true; global.Cells.Clear(); hostScope.Cells.Clear(); modules.Clear(); } return ValueTask.CompletedTask; }
    private void Step(SourceSpan span)
    { cancellation.ThrowIfCancellationRequested(); if (++steps > options.MaximumSteps) throw new ScriptRuntimeException(new("QBS2002", "Execution step limit exceeded.", span)); }
    private static ScriptRuntimeException Wrap(Exception error, SourceSpan span)
    {
        if (error is TargetInvocationException { InnerException: not null } invocation) error = invocation.InnerException!;
        if (error is OperationCanceledException canceled) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(canceled).Throw();
        return new(new("QBS2001", error.Message, span), error);
    }
    private async ValueTask Execute(Statement statement, Scope scope)
    {
        Step(statement.Span);
        try
        {
            switch (statement)
            {
                case Block block: foreach (var child in block.Statements) await Execute(child, scope).ConfigureAwait(false); break;
                case VariableDeclaration declaration:
                    if (scope.Cells.ContainsKey(declaration.Name)) throw new InvalidOperationException($"Duplicate declaration '{declaration.Name}'.");
                    object? type = ResolveDeclared(declaration.TypeName, scope);
                    var value = declaration.Initializer is null ? DefaultValue(type) : await Evaluate(declaration.Initializer, scope).ConfigureAwait(false);
                    scope.Cells.Add(declaration.Name, new(ConvertValue(value, type), type)); break;
                case Assignment assignment:
                    if (assignment.Target is NameExpression name) Set(scope.Find(name.Name), await Evaluate(assignment.Value, scope).ConfigureAwait(false));
                    else if (assignment.Target is MemberExpression member)
                    {
                        var target = await Evaluate(member.Target, scope).ConfigureAwait(false); var assigned = await Evaluate(assignment.Value, scope).ConfigureAwait(false);
                        SetMember(target, member.Member, assigned);
                    }
                    else throw new InvalidOperationException("Invalid assignment target.");
                    break;
                case ExpressionStatement expression: await Evaluate(expression.Expression, scope).ConfigureAwait(false); break;
                case IfStatement conditional:
                    if (Values.Boolean(await Evaluate(conditional.Condition, scope).ConfigureAwait(false))) await Execute(conditional.Then, scope).ConfigureAwait(false);
                    else if (conditional.Else is not null) await Execute(conditional.Else, scope).ConfigureAwait(false); break;
                case WhileStatement loop:
                    await ExecuteWhile(loop,scope).ConfigureAwait(false);break;
                case ForStatement loop: await ExecuteFor(loop,scope).ConfigureAwait(false);break;
                case ForEachStatement loop: await ExecuteForEach(loop,scope).ConfigureAwait(false);break;
                case SelectStatement selection: await ExecuteSelect(selection,scope).ConfigureAwait(false);break;
                case DoStatement loop: await ExecuteDo(loop,scope).ConfigureAwait(false);break;
                case ExitLoopStatement exited:
                    if(!activeLoops.Contains(exited.Kind))throw new InvalidOperationException("Exit has no matching active loop.");
                    throw new LoopExited(exited.Kind);
                case FunctionDeclaration function:
                    if (scope.Cells.ContainsKey(function.Name)) throw new InvalidOperationException($"Duplicate declaration '{function.Name}'.");
                    ResolveDeclared(function.ReturnType, scope); foreach (var p in function.Parameters) ResolveDeclared(p.TypeName, scope);
                    scope.Cells.Add(function.Name, new(new ScriptFunction(function, scope), null)); break;
                case ClassDeclaration declaration: DeclareClass(declaration, scope); break;
                case NamespaceDeclaration declaration:
                    var ns = Namespace(declaration.Name, scope); await Execute(declaration.Body, ns.Scope).ConfigureAwait(false); break;
                case ImportStatement import: await Import(import, scope).ConfigureAwait(false); break;
                case ReturnStatement returned: throw new Returned(returned.Value is null ? null : await Evaluate(returned.Value, scope).ConfigureAwait(false));
                default: throw new InvalidOperationException("Unsupported statement.");
            }
        }
        catch (Exception e) when (e is not (Returned or LoopExited or ScriptRuntimeException or OperationCanceledException)) { throw Wrap(e, statement.Span); }
    }
    private async ValueTask<object?[]> EvaluateArguments(IEnumerable<Expression> expressions, Scope scope)
    { var args = new List<object?>(); foreach (var expression in expressions) args.Add(await Evaluate(expression, scope).ConfigureAwait(false)); return args.ToArray(); }
    private async ValueTask<object?> Evaluate(Expression expression, Scope scope)
    {
        Step(expression.Span);
        try
        {
            switch (expression)
            {
                case LiteralExpression literal: return literal.Value;
                case NameExpression name: return scope.Find(name.Name).Value;
                case UnaryExpression unary: return Values.Unary(unary.Operator, await Evaluate(unary.Operand, scope).ConfigureAwait(false));
                case BinaryExpression binary:
                    var left = await Evaluate(binary.Left, scope).ConfigureAwait(false);
                    if (binary.Operator == BinaryOperator.AndAlso && !Values.Boolean(left)) return false;
                    if (binary.Operator == BinaryOperator.OrElse && Values.Boolean(left)) return true;
                    return Values.Binary(binary.Operator, left, await Evaluate(binary.Right, scope).ConfigureAwait(false));
                case MemberExpression member: return GetMember(await Evaluate(member.Target, scope).ConfigureAwait(false), member.Member);
                case NewExpression creation:
                    var type = ResolveType(creation.Type, scope); var arguments = await EvaluateArguments(creation.Arguments, scope).ConfigureAwait(false);
                    if (type is ScriptClass scriptClass) return await Construct(scriptClass, arguments, creation.Span).ConfigureAwait(false);
                    cancellation.ThrowIfCancellationRequested(); var instance = members.Construct((Type)type, arguments); cancellation.ThrowIfCancellationRequested(); return instance;
                case CallExpression call:
                    object? target; string? method = null;
                    if (call.Target is MemberExpression m)
                    {
                        target = await Evaluate(m.Target, scope).ConfigureAwait(false);
                        if (target is ScriptInstance or ScriptContainer or IScriptMemberProvider) target = GetMember(target, m.Member);
                        else method = m.Member;
                    }
                    else target = await Evaluate(call.Target, scope).ConfigureAwait(false);
                    var args = await EvaluateArguments(call.Arguments, scope).ConfigureAwait(false); cancellation.ThrowIfCancellationRequested(); object? result;
                    if (method is not null) result = members.Call(target, method, args);
                    else if (target is HostFunction host) result = await host(new(cancellation) { Materialize = (typeName, data) => Materialize(typeName, data, scope, call.Span) }, args).ConfigureAwait(false);
                    else if (target is ScriptFunction function) result = await Invoke(function, args, call.Span).ConfigureAwait(false);
                    else throw new InvalidOperationException("Target is not callable (register delegates as HostFunction).");
                    cancellation.ThrowIfCancellationRequested(); return result;
                default: throw new InvalidOperationException("Unsupported expression.");
            }
        }
        catch (Exception e) when (e is not (ScriptRuntimeException or OperationCanceledException)) { throw Wrap(e, expression.Span); }
    }
    private async ValueTask<object?> Invoke(ScriptFunction function, object?[] args, SourceSpan span)
    {
        if (++callDepth > options.MaximumCallDepth) { callDepth--; throw new ScriptRuntimeException(new("QBS2003", "Function call depth limit exceeded.", span)); }
        try
        {
            var previousLoops=activeLoops;activeLoops=[];
            try
            {
            var declaration = function.Declaration;
            if (args.Length != declaration.Parameters.Length) throw new InvalidOperationException("Incorrect argument count.");
            var local = new Scope(function.Closure);
            for (int i = 0; i < args.Length; i++)
            { var p = declaration.Parameters[i]; var type = ResolveDeclared(p.TypeName, function.Closure); local.Cells.Add(p.Name, new(ConvertValue(args[i], type), type)); }
            object? value = null; try { await Execute(declaration.Body, local).ConfigureAwait(false); } catch (Returned returned) { value = returned.Value; }
            return declaration.IsSub ? null : ConvertValue(value, ResolveDeclared(declaration.ReturnType, function.Closure));
            }finally{activeLoops=previousLoops;}
        }
        finally { callDepth--; }
    }
}
