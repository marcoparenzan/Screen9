namespace QBSharp.Core.Syntax;

public enum LoopKind { For, Do, While }
public sealed record ForStatement(string Counter,string? CounterType,Expression Start,Expression Limit,Expression? Step,Block Body,SourceSpan Span):Statement(Span);
public sealed record DoStatement(Expression? Precondition,bool PreUntil,Block Body,Expression? Postcondition,bool PostUntil,SourceSpan Span):Statement(Span);
public sealed record ExitLoopStatement(LoopKind Kind,SourceSpan Span):Statement(Span);
