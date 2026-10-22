using System.Collections.Immutable;
namespace QBSharp.Core.Syntax;
public sealed record ForEachStatement(string Counter, string? CounterType, Expression Collection, Block Body, SourceSpan Span) : Statement(Span);
public sealed record CaseTest(BinaryOperator Operator, Expression Value, Expression? Upper, SourceSpan Span) : SyntaxNode(Span);
public sealed record CaseClause(ImmutableArray<CaseTest> Tests, Block Body, bool IsElse, SourceSpan Span) : SyntaxNode(Span);
public sealed record SelectStatement(Expression Selector, ImmutableArray<CaseClause> Cases, SourceSpan Span) : Statement(Span);
