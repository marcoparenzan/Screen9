using System.Collections.Immutable;

namespace QBSharp.Core.Syntax;

public abstract record SyntaxNode(SourceSpan Span);
public abstract record Statement(SourceSpan Span) : SyntaxNode(Span);
public abstract record Expression(SourceSpan Span) : SyntaxNode(Span);
public sealed record Script(ImmutableArray<Statement> Statements, SourceSpan Span) : SyntaxNode(Span);
public sealed record Block(ImmutableArray<Statement> Statements, SourceSpan Span) : Statement(Span);
public sealed record VariableDeclaration(string Name, string? TypeName, Expression? Initializer, SourceSpan Span) : Statement(Span);
public sealed record Assignment(Expression Target, Expression Value, SourceSpan Span) : Statement(Span);
public sealed record ExpressionStatement(Expression Expression, SourceSpan Span) : Statement(Span);
public sealed record IfStatement(Expression Condition, Block Then, Block? Else, SourceSpan Span) : Statement(Span);
public sealed record WhileStatement(Expression Condition, Block Body, SourceSpan Span) : Statement(Span);
public sealed record ReturnStatement(Expression? Value, SourceSpan Span) : Statement(Span);
public sealed record Parameter(string Name, string? TypeName, SourceSpan Span) : SyntaxNode(Span);
public sealed record FunctionDeclaration(string Name, ImmutableArray<Parameter> Parameters, string? ReturnType, Block Body, SourceSpan Span) : Statement(Span)
{
    public bool IsSub { get; init; }
    public bool IsConstructor { get; init; }
}
public sealed record ClassDeclaration(string Name, ImmutableArray<Statement> Members, SourceSpan Span) : Statement(Span);
public sealed record PropertyDeclaration(string Name, string? TypeName, Expression? Initializer, SourceSpan Span) : Statement(Span);
public sealed record NamespaceDeclaration(string Name, Block Body, SourceSpan Span) : Statement(Span);
public sealed record ImportStatement(Expression Name, string? Alias, SourceSpan Span) : Statement(Span);
public sealed record TypeSyntax(string Name, ImmutableArray<TypeSyntax> Arguments, SourceSpan Span) : SyntaxNode(Span)
{
    public override string ToString() => Arguments.IsEmpty ? Name : $"{Name}(Of {string.Join(", ", Arguments)})";
}
public sealed record NewExpression(TypeSyntax Type, ImmutableArray<Expression> Arguments, SourceSpan Span) : Expression(Span);
public sealed record LiteralExpression(object? Value, SourceSpan Span) : Expression(Span);
public sealed record NameExpression(string Name, SourceSpan Span) : Expression(Span);
public sealed record UnaryExpression(UnaryOperator Operator, Expression Operand, SourceSpan Span) : Expression(Span);
public sealed record BinaryExpression(Expression Left, BinaryOperator Operator, Expression Right, SourceSpan Span) : Expression(Span);
public sealed record CallExpression(Expression Target, ImmutableArray<Expression> Arguments, SourceSpan Span) : Expression(Span);
public sealed record MemberExpression(Expression Target, string Member, SourceSpan Span) : Expression(Span);

public enum UnaryOperator { Plus, Negate, Not }
public enum BinaryOperator
{
    Add, Subtract, Multiply, Divide, IntegerDivide, Modulo, Power, Concatenate,
    Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual,
    And, Or, AndAlso, OrElse
}
