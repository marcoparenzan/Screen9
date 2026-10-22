using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.Core.Modules;
using QBSharp.Core.Syntax;

namespace QBSharp.Core.Runtime;

internal sealed partial class ScriptSession
{
    private sealed record ScriptClass(ClassDeclaration Declaration, Scope Closure, string FullName) : IScriptOpaqueValue;
    private sealed record ScriptContainer(Scope Scope, bool IsModule = false) : IScriptOpaqueValue { public override string ToString() => Scope.Qualification; }
    private sealed record ScriptInstance(ScriptClass Class, Scope Scope) : IScriptPropertyBag
    {
        public IReadOnlyDictionary<string, object?> Properties => new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(Class.Declaration.Members.OfType<PropertyDeclaration>().ToDictionary(p => p.Name, p => Scope.Cells[p.Name].Value, StringComparer.OrdinalIgnoreCase));
        public override string ToString() => Class.FullName;
    }
    private sealed class ModuleEntry { public ScriptContainer? Module; public ScriptRuntimeException? Failure; }
    private readonly Dictionary<string, ModuleEntry> modules = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly List<string> importStack = [];
    private readonly Dictionary<string, Type> clrTypes;
    private static readonly Dictionary<string, Type> BuiltinTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Integer"] = typeof(int), ["Long"] = typeof(long), ["Double"] = typeof(double), ["Decimal"] = typeof(decimal), ["Boolean"] = typeof(bool),
        ["String"] = typeof(string), ["Object"] = typeof(object), ["Date"] = typeof(DateTime), ["List"] = typeof(List<>), ["Dictionary"] = typeof(Dictionary<,>),
        ["System.Int32"] = typeof(int), ["System.Int64"] = typeof(long), ["System.Double"] = typeof(double), ["System.Decimal"] = typeof(decimal),
        ["System.Boolean"] = typeof(bool), ["System.String"] = typeof(string), ["System.Object"] = typeof(object), ["System.DateTime"] = typeof(DateTime),
        ["System.Collections.Generic.List"] = typeof(List<>), ["System.Collections.Generic.Dictionary"] = typeof(Dictionary<,>)
    };
    private static object? DefaultValue(object? type) => type is Type clr ? Values.Default(clr) : null;
    private static object? ConvertValue(object? value, object? type)
    {
        if (type is ScriptClass scriptClass)
        {
            if (value is null || value is ScriptInstance instance && ReferenceEquals(instance.Class, scriptClass)) return value;
            throw new InvalidOperationException($"Expected script class '{scriptClass.FullName}'.");
        }
        return Values.ConvertTo(value, type as Type);
    }
    private static void Set(Cell cell, object? value)
    { if (cell.ReadOnly) throw new InvalidOperationException("Binding is read-only."); cell.Value = ConvertValue(value, cell.Type); }
    private static Cell Export(Scope scope, string name) => scope.Cells.TryGetValue(name, out var cell) ? cell : throw new InvalidOperationException($"Missing exported member '{name}'.");
    private object? GetMember(object? target, string name) => target switch
    {
        ScriptInstance instance => Export(instance.Scope, name).Value,
        ScriptContainer container => Export(container.Scope, name).Value,
        IScriptMemberProvider provider => provider.GetMember(name),
        _ => members.Get(target, name)
    };
    private void SetMember(object? target, string name, object? value)
    {
        if (target is ScriptInstance instance) Set(Export(instance.Scope, name), value);
        else if (target is ScriptContainer container) Set(Export(container.Scope, name), value);
        else if (target is IScriptMemberProvider provider) provider.SetMember(name, value);
        else members.Set(target, name, value);
    }
    private object? ResolveDeclared(string? name, Scope scope)
    {
        if (name is null) return null;
        var result = new ScriptParser().ParseType(new(name, "<type>"), cancellation);
        if (!result.Success) throw new InvalidOperationException($"Invalid type syntax '{name}'.");
        return ResolveType(result.Root!, scope);
    }
    private object ResolveType(TypeSyntax syntax, Scope scope)
    {
        cancellation.ThrowIfCancellationRequested();
        object? type = null;
        string[] names = syntax.Name.Split('.');
        if (scope.TryFind(names[0], out var cell) && cell!.Value is ScriptClass or ScriptContainer or Type)
        {
            type = cell!.Value;
            foreach (var part in names.Skip(1)) type = GetMember(type, part);
        }
        else if (clrTypes.TryGetValue(syntax.Name, out var registered) || BuiltinTypes.TryGetValue(syntax.Name, out registered)) type = registered;
        if (type is ScriptClass scriptClass)
        { if (!syntax.Arguments.IsEmpty) throw new InvalidOperationException("Script generic classes are not supported."); return scriptClass; }
        if (type is not Type clr) throw new InvalidOperationException($"Unknown type '{syntax.Name}'.");
        if (syntax.Arguments.IsEmpty)
        { if (clr.ContainsGenericParameters) throw new InvalidOperationException("Generic type requires Of arguments."); return clr; }
        if (!clr.IsGenericTypeDefinition || clr.GetGenericArguments().Length != syntax.Arguments.Length) throw new InvalidOperationException("Incorrect generic arity.");
        var arguments = syntax.Arguments.Select(a => ResolveType(a, scope)).ToArray();
        if (arguments.Any(a => a is not Type)) throw new InvalidOperationException("CLR generic arguments must be CLR types, not interpreted classes.");
        return clr.MakeGenericType(arguments.Cast<Type>().ToArray());
    }
    private static string Qualify(Scope scope, string name) => string.IsNullOrEmpty(scope.Qualification) ? name : scope.Qualification + "." + name;
    private void DeclareClass(ClassDeclaration declaration, Scope scope)
    {
        if (scope.Cells.ContainsKey(declaration.Name)) throw new InvalidOperationException($"Duplicate declaration '{declaration.Name}'.");
        scope.Cells.Add(declaration.Name, new(new ScriptClass(declaration, scope, Qualify(scope, declaration.Name)), null, true));
    }
    private static ScriptContainer Namespace(string name, Scope scope)
    {
        ScriptContainer? container = null;
        foreach (var part in name.Split('.'))
        {
            if (scope.Cells.TryGetValue(part, out var cell)) container = cell.Value is ScriptContainer { IsModule: false } ns ? ns : throw new InvalidOperationException($"Namespace collision '{part}'.");
            else
            { container = new(new Scope(scope, Qualify(scope, part))); scope.Cells.Add(part, new(container, null, true)); }
            scope = container.Scope;
        }
        return container!;
    }
    private async ValueTask<ScriptInstance> Construct(ScriptClass type, object?[] args, SourceSpan span)
    {
        if (++callDepth > options.MaximumCallDepth) { callDepth--; throw new ScriptRuntimeException(new("QBS2003", "Construction depth limit exceeded.", span)); }
        try
        {
            var scope = new Scope(type.Closure); var instance = new ScriptInstance(type, scope);
            scope.Cells.Add("Me", new(instance, null, true));
            foreach (var method in type.Declaration.Members.OfType<FunctionDeclaration>().Where(f => !f.IsConstructor))
                scope.Cells.Add(method.Name, new(new ScriptFunction(method, scope), null, true));
            // Allocate defaults before evaluating initializers: other properties exist with their CLR defaults.
            foreach (var property in type.Declaration.Members.OfType<PropertyDeclaration>())
            {
                try { var constraint = ResolveDeclared(property.TypeName, type.Closure); scope.Cells.Add(property.Name, new(DefaultValue(constraint), constraint)); }
                catch (Exception e) when (e is not (ScriptRuntimeException or OperationCanceledException)) { throw Wrap(e, property.Span); }
            }
            foreach (var property in type.Declaration.Members.OfType<PropertyDeclaration>())
            {
                Step(property.Span);
                try { if (property.Initializer is not null) Set(scope.Cells[property.Name], await Evaluate(property.Initializer, scope).ConfigureAwait(false)); }
                catch (Exception e) when (e is not (ScriptRuntimeException or OperationCanceledException)) { throw Wrap(e, property.Span); }
            }
            var constructor = type.Declaration.Members.OfType<FunctionDeclaration>().SingleOrDefault(f => f.IsConstructor);
            if (constructor is not null) await Invoke(new(constructor, scope), args, span).ConfigureAwait(false);
            else if (args.Length > 0) throw new InvalidOperationException("Default script constructor accepts no arguments.");
            return instance;
        }
        finally { callDepth--; }
    }
    private async ValueTask Import(ImportStatement statement, Scope scope)
    {
        var nameValue = await Evaluate(statement.Name, scope).ConfigureAwait(false);
        if (nameValue is not string name || string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Import requires a nonempty String module path.");
        var resolved = await resolver.ResolveAsync(new(name, statement.Span.File), cancellation).ConfigureAwait(false);
        string path = resolved.CanonicalPath;
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string alias = statement.Alias ?? Path.GetFileNameWithoutExtension(name.TrimEnd('/', '\\'));
        if (alias.Equals("__init__", StringComparison.OrdinalIgnoreCase)) alias = Path.GetFileName(Path.GetDirectoryName(name)) ?? "";
        var validate = new HostBindings(); validate.SetValue(alias, null);
        // A default alias must be addressable by the parser (not a keyword).
        if (new ScriptParser().ParseExpression(new(alias)).Root is not NameExpression) throw new InvalidOperationException("Import path needs an explicit, valid As alias.");
        ScriptContainer module;
        if (modules.TryGetValue(path, out var entry))
        {
            if (entry.Failure is not null) throw entry.Failure;
            if (entry.Module is null) throw new ScriptRuntimeException(new("QBS3002", $"Import cycle: {string.Join(" -> ", importStack.Append(path))}", statement.Span));
            module = entry.Module;
        }
        else
        {
            if (importStack.Count >= options.MaximumImportDepth) throw new ScriptRuntimeException(new("QBS3003", "Import depth limit exceeded.", statement.Span));
            entry = new(); modules.Add(path, entry); importStack.Add(path);
            try
            {
                var parsed = parser.ParseScript(resolved.Source, cancellation);
                if (!parsed.Success) throw new ScriptRuntimeException(parsed.Diagnostics[0]);
                var moduleScope = new Scope(hostScope, Path.GetFileNameWithoutExtension(path));
                foreach (var child in parsed.Root!.Statements) await Execute(child, moduleScope).ConfigureAwait(false);
                module = new(moduleScope, true); entry.Module = module;
            }
            catch (OperationCanceledException) { modules.Remove(path); throw; }
            catch (ScriptRuntimeException e) { entry.Failure = e; throw; }
            catch (Exception e) { var failure = Wrap(e, statement.Span); entry.Failure = failure; throw failure; }
            finally { importStack.RemoveAt(importStack.Count - 1); }
        }
        if (scope.Cells.TryGetValue(alias, out var existing))
        { if (!ReferenceEquals(existing.Value, module)) throw new InvalidOperationException($"Import alias collision '{alias}'."); }
        else scope.Cells.Add(alias, new(module, null, true));
    }
}
