using QBSharp.Core.Hosting;
using QBSharp.Core.Syntax;

namespace QBSharp.Core.Runtime;

internal sealed class GuardedParser(IScriptParser inner) : IScriptParser
{
    public ParseResult<Script> ParseScript(ScriptSource source, CancellationToken cancellationToken = default) => Validate(inner.ParseScript(source, cancellationToken), cancellationToken);
    public ParseResult<Expression> ParseExpression(ScriptSource source, CancellationToken cancellationToken = default) => Validate(inner.ParseExpression(source, cancellationToken), cancellationToken);
    private static ParseResult<T> Validate<T>(ParseResult<T> result, CancellationToken token) where T : SyntaxNode
    {
        if (!result.Success) return result;
        var stack = new Stack<(SyntaxNode Node, int Depth)>(); stack.Push((result.Root!, 0));
        while (stack.TryPop(out var entry))
        {
            token.ThrowIfCancellationRequested();
            if (entry.Depth > 128) return new(null, [new("QBS1101", "Maximum AST depth (128) exceeded.", entry.Node.Span)]);
            void Push(SyntaxNode? node) { if (node is not null) stack.Push((node, entry.Depth + 1)); }
            switch (entry.Node)
            {
                case Script script: foreach (var s in script.Statements) Push(s); break;
                case Block block: foreach (var s in block.Statements) Push(s); break;
                case VariableDeclaration d: Push(d.Initializer); break;
                case PropertyDeclaration p: Push(p.Initializer); break;
                case ClassDeclaration c: foreach (var m in c.Members) Push(m); break;
                case NamespaceDeclaration n: Push(n.Body); break;
                case ImportStatement i: Push(i.Name); break;
                case NewExpression n: Push(n.Type); foreach (var a in n.Arguments) Push(a); break;
                case TypeSyntax t: foreach (var a in t.Arguments) Push(a); break;
                case Assignment a: Push(a.Target); Push(a.Value); break;
                case ExpressionStatement s: Push(s.Expression); break;
                case IfStatement c: Push(c.Condition); Push(c.Then); Push(c.Else); break;
                case WhileStatement w: Push(w.Condition); Push(w.Body); break;
                case ForStatement f: Push(f.Start); Push(f.Limit); Push(f.Step); Push(f.Body); break;
                case ForEachStatement f: Push(f.Collection); Push(f.Body); break;
                case SelectStatement s: Push(s.Selector); foreach(var c in s.Cases) Push(c); break;
                case CaseClause c: foreach(var t in c.Tests) Push(t); Push(c.Body); break;
                case CaseTest c: Push(c.Value); Push(c.Upper); break;
                case DoStatement d: Push(d.Precondition); Push(d.Body); Push(d.Postcondition); break;
                case FunctionDeclaration f: Push(f.Body); break;
                case ReturnStatement r: Push(r.Value); break;
                case UnaryExpression u: Push(u.Operand); break;
                case BinaryExpression b: Push(b.Left); Push(b.Right); break;
                case MemberExpression m: Push(m.Target); break;
                case CallExpression c: Push(c.Target); foreach (var a in c.Arguments) Push(a); break;
            }
        }
        return result;
    }
}
