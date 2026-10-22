using System.Collections.Immutable;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;

namespace QBSharp.Core.Syntax;

public sealed partial class ScriptParser : IScriptParser
{
    public ParseResult<Script> ParseScript(ScriptSource source, CancellationToken cancellationToken = default) => Parse(source, cancellationToken, p => p.Script());
    public ParseResult<Expression> ParseExpression(ScriptSource source, CancellationToken cancellationToken = default) =>
        Parse(source, cancellationToken, p => { var e = p.Expression(); p.NewLines(); p.ExpectEnd(); return e; });
    public ParseResult<TypeSyntax> ParseType(ScriptSource source, CancellationToken cancellationToken = default) =>
        Parse(source, cancellationToken, p => { var t = p.Type(); p.ExpectEnd(); return t; });
    private static ParseResult<T> Parse<T>(ScriptSource source, CancellationToken token, Func<Reader, T> parse) where T : SyntaxNode
    {
        var lex = new Lexer().Scan(source, token);
        if (lex.Diagnostics.Count > 0) return new(null, [.. lex.Diagnostics]);
        try { return new(parse(new(lex.Tokens, token)), []); }
        catch (ParseFailure failure) { return new(null, [failure.Diagnostic]); }
    }
    private sealed class ParseFailure(ScriptDiagnostic diagnostic) : Exception { public ScriptDiagnostic Diagnostic { get; } = diagnostic; }
    private sealed partial class Reader(IReadOnlyList<Token> tokens, CancellationToken token)
    {
        private int index, depth, functionDepth;
        private bool inSub;
        private Token Current => tokens[index];
        private Token Previous => tokens[Math.Max(0, index - 1)];
        private bool Is(string value) => Current.Text.Equals(value, StringComparison.OrdinalIgnoreCase);
        private Token Take() { token.ThrowIfCancellationRequested(); var t = Current; if (t.Kind != TokenKind.End) index++; return t; }
        private bool Match(string value) { if (!Is(value)) return false; Take(); return true; }
        private void Fail(string message) => throw new ParseFailure(new("QBS1100", message, Current.Span));
        private Token Expect(string value) { if (!Is(value)) Fail($"Expected '{value}', found '{Current.Text}'."); return Take(); }
        private SourceSpan Range(SourceSpan start) => new(start.File, start.Start, Math.Max(0, Previous.Span.Start.Offset + Previous.Span.Length - start.Start.Offset));
        private void Enter() { token.ThrowIfCancellationRequested(); if (++depth > 128) Fail("Maximum parser nesting (128) exceeded."); }
        public void NewLines() { while (Current.Kind == TokenKind.NewLine) Take(); }
        public void ExpectEnd() { if (Current.Kind != TokenKind.End) Fail("Unexpected trailing input."); }
        private void LineEnd() { if (Current.Kind != TokenKind.End && Current.Kind != TokenKind.NewLine) Fail("Expected end of line."); NewLines(); }
        private Token Name() { if (Current.Kind != TokenKind.Identifier || Reserved.Contains(Current.Text)) Fail("Expected identifier."); return Take(); }
        private Token MemberName() { if (Current.Kind != TokenKind.Identifier) Fail("Expected member identifier."); return Take(); }
        private string QualifiedName() { var name = Name().Text; while (Match(".")) name += "." + Name().Text; return name; }
        public TypeSyntax Type()
        {
            Enter();
            try
            {
                var start = Current.Span; string name = QualifiedName(); var args = ImmutableArray.CreateBuilder<TypeSyntax>();
                if (Is("(") && index + 1 < tokens.Count && tokens[index + 1].Text.Equals("Of", StringComparison.OrdinalIgnoreCase))
                { Take(); Expect("Of"); do { args.Add(Type()); } while (Match(",")); Expect(")"); }
                return new(name, args.ToImmutable(), Range(start));
            }
            finally { depth--; }
        }
        private string? TypeName() => Match("As") ? Type().ToString() : null;
        public Script Script()
        {
            var start = Current.Span; NewLines(); var statements = ImmutableArray.CreateBuilder<Statement>();
            while (Current.Kind != TokenKind.End) statements.Add(Statement());
            return new(statements.ToImmutable(), Range(start));
        }
        private Block Body(params string[] stops)
        {
            var start = Current.Span; var statements = ImmutableArray.CreateBuilder<Statement>();
            while (Current.Kind != TokenKind.End && !Is("End") && !Is("Else") && !stops.Any(Is)) statements.Add(Statement());
            return new(statements.ToImmutable(), Range(start));
        }
        private ImmutableArray<Parameter> Parameters()
        {
            Expect("("); var parameters = ImmutableArray.CreateBuilder<Parameter>();
            if (!Is(")")) do { var p = Name(); parameters.Add(new(p.Text, TypeName(), Range(p.Span))); } while (Match(","));
            Expect(")");
            if (parameters.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != parameters.Count) Fail("Duplicate parameter.");
            return parameters.ToImmutable();
        }
        private FunctionDeclaration Function(SourceSpan start, bool isSub, bool allowConstructor = false)
        {
            string name; bool constructor = isSub && Is("New");
            if (constructor) { if (!allowConstructor) Fail("Sub New is only valid in a class."); name = Take().Text; }
            else name = Name().Text;
            var parameters = Parameters(); var type = isSub ? null : TypeName(); LineEnd();
            var previousLoops=loops;loops=[];functionDepth++; bool previousSub = inSub; inSub = isSub; Block body;
            try { body = Body(); } finally { functionDepth--; inSub = previousSub;loops=previousLoops; }
            Expect("End"); Expect(isSub ? "Sub" : "Function");
            var result = new FunctionDeclaration(name, parameters, type, body, Range(start)) { IsSub = isSub, IsConstructor = constructor }; LineEnd(); return result;
        }
        private ClassDeclaration Class(SourceSpan start)
        {
            if (functionDepth > 0) Fail("Class cannot be declared inside a function.");
            string name = Name().Text; LineEnd(); var members = ImmutableArray.CreateBuilder<Statement>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (Current.Kind != TokenKind.End && !Is("End"))
            {
                var memberStart = Current.Span; Match("Public"); Statement member;
                if (Match("Property"))
                {
                    string propertyName = Name().Text; string? type = TypeName(); var init = Match("=") ? Expression() : null;
                    member = new PropertyDeclaration(propertyName, type, init, Range(memberStart)); LineEnd();
                }
                else if (Match("Function")) member = Function(memberStart, false);
                else if (Match("Sub")) member = Function(memberStart, true, true);
                else { Fail("Class supports auto Property, Function and Sub only; all public."); return null!; }
                string memberName = member is PropertyDeclaration p ? p.Name : ((FunctionDeclaration)member).Name;
                if (!names.Add(memberName)) Fail($"Duplicate class member '{memberName}'.");
                members.Add(member);
            }
            Expect("End"); Expect("Class"); var result = new ClassDeclaration(name, members.ToImmutable(), Range(start)); LineEnd(); return result;
        }
        private Statement Statement()
        {
            Enter();
            try
            {
                var start = Current.Span;
                if (Match("Public"))
                {
                    if (Match("Class")) return Class(start);
                    if (Match("Function")) return Function(start, false);
                    if (Match("Sub")) return Function(start, true);
                    Fail("Public is valid before Class, Function or Sub here.");
                }
                if (Match("Class")) return Class(start);
                if (Match("Namespace"))
                {
                    if (functionDepth > 0) Fail("Namespace cannot be declared inside a function.");
                    var name = QualifiedName(); LineEnd(); var body = Body();
                    if (body.Statements.Any(s => s is not (ClassDeclaration or NamespaceDeclaration))) Fail("Namespace supports Class and Namespace declarations only.");
                    Expect("End"); Expect("Namespace"); var result = new NamespaceDeclaration(name, body, Range(start)); LineEnd(); return result;
                }
                if (Match("Import"))
                {
                    var name = Expression(); string? alias = Match("As") ? Name().Text : null;
                    var result = new ImportStatement(name, alias, Range(start)); LineEnd(); return result;
                }
                if (Match("Dim"))
                { var name = Name().Text; var type = TypeName(); var init = Match("=") ? Expression() : null; var result = new VariableDeclaration(name, type, init, Range(start)); LineEnd(); return result; }
                if (Match("If"))
                {
                    var result = IfChain(start); Expect("End"); Expect("If"); LineEnd(); return result;
                }
                if(Match("Select"))return Select(start);
                if(Match("For"))return Match("Each") ? ForEach(start) : For(start);
                if(Match("Do"))return Do(start);
                if(Match("Exit"))return ExitLoop(start);
                if (Match("While"))
                { var condition = Expression(); LineEnd(); var body = LoopBody(LoopKind.While); Expect("End"); Expect("While"); var result = new WhileStatement(condition, body, Range(start)); LineEnd(); return result; }
                if (Match("Function")) return Function(start, false);
                if (Match("Sub")) return Function(start, true);
                if (Match("Return"))
                {
                    if (functionDepth == 0) Fail("Return is only valid inside a function or Sub.");
                    var value = Current.Kind is TokenKind.NewLine or TokenKind.End ? null : Expression();
                    if (inSub && value is not null) Fail("Sub/constructor cannot return a value.");
                    var result = new ReturnStatement(value, Range(start)); LineEnd(); return result;
                }
                int saved = index;
                if (Current.Kind == TokenKind.Identifier && (!Reserved.Contains(Current.Text) || Is("Me")))
                {
                    var n = Take(); Expression target = new NameExpression(n.Text, n.Span);
                    while (Match(".")) target = new MemberExpression(target, MemberName().Text, Range(start));
                    if (Match("=")) { var value = Expression(); var result = new Assignment(target, value, Range(start)); LineEnd(); return result; }
                }
                index = saved; var expression = Expression(); var statement = new ExpressionStatement(expression, Range(start)); LineEnd(); return statement;
            }
            finally { depth--; }
        }
        private ImmutableArray<Expression> Arguments()
        {
            Expect("("); var args = ImmutableArray.CreateBuilder<Expression>();
            if (!Is(")")) do { args.Add(Expression()); } while (Match(","));
            Expect(")"); return args.ToImmutable();
        }
        public Expression Expression(int minimum = 0)
        {
            Enter();
            try
            {
                var start = Current.Span; Expression left;
                if (Match("+")) left = new UnaryExpression(UnaryOperator.Plus, Expression(11), Range(start));
                else if (Match("-")) left = new UnaryExpression(UnaryOperator.Negate, Expression(11), Range(start));
                else if (Match("Not")) left = new UnaryExpression(UnaryOperator.Not, Expression(4), Range(start));
                else if (Match("New")) { var type = Type(); left = new NewExpression(type, Arguments(), Range(start)); }
                else if (Match("Me")) left = new NameExpression("Me", start);
                else if (Match("(")) { left = Expression(); Expect(")"); }
                else if (Current.Kind is TokenKind.Number or TokenKind.String) { var t = Take(); left = new LiteralExpression(t.Value, t.Span); }
                else if (Is("True") || Is("False") || Is("Nothing"))
                { var t = Take(); left = new LiteralExpression(t.Text.Equals("Nothing", StringComparison.OrdinalIgnoreCase) ? null : t.Text.Equals("True", StringComparison.OrdinalIgnoreCase), t.Span); }
                else { var t = Name(); left = new NameExpression(t.Text, t.Span); }
                while (true)
                {
                    if (Match(".")) { left = new MemberExpression(left, MemberName().Text, Range(start)); continue; }
                    if (Is("(")) { left = new CallExpression(left, Arguments(), Range(start)); continue; }
                    if (!Operators.TryGetValue(Current.Text, out var op) || op.Precedence < minimum) break;
                    Take(); var right = Expression(op.Precedence + (op.Operator == BinaryOperator.Power ? 0 : 1)); left = new BinaryExpression(left, op.Operator, right, Range(start));
                }
                return left;
            }
            finally { depth--; }
        }
    }
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    { "Dim", "As", "If", "Then", "Else", "End", "While", "Function", "Return", "True", "False", "Nothing", "Not", "And", "Or", "AndAlso", "OrElse", "Mod", "Import", "Class", "Namespace", "New", "Sub", "For", "Next", "Do", "Loop", "Select", "Case", "Public", "Private", "Property", "Me", "Of", "To", "Step", "Until", "Exit", "ElseIf", "Each", "In", "Is" };
    private static readonly Dictionary<string, (BinaryOperator Operator, int Precedence)> Operators = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Or"] = (BinaryOperator.Or, 1), ["OrElse"] = (BinaryOperator.OrElse, 1), ["And"] = (BinaryOperator.And, 2), ["AndAlso"] = (BinaryOperator.AndAlso, 2),
        ["="] = (BinaryOperator.Equal, 4), ["<>"] = (BinaryOperator.NotEqual, 4), ["<"] = (BinaryOperator.Less, 4), ["<="] = (BinaryOperator.LessOrEqual, 4), [">"] = (BinaryOperator.Greater, 4), [">="] = (BinaryOperator.GreaterOrEqual, 4),
        ["&"] = (BinaryOperator.Concatenate, 5), ["+"] = (BinaryOperator.Add, 6), ["-"] = (BinaryOperator.Subtract, 6), ["Mod"] = (BinaryOperator.Modulo, 7), ["\\"] = (BinaryOperator.IntegerDivide, 8),
        ["*"] = (BinaryOperator.Multiply, 9), ["/"] = (BinaryOperator.Divide, 9), ["^"] = (BinaryOperator.Power, 12)
    };
}
