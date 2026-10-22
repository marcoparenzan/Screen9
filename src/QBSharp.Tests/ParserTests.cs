using QBSharp.Core.Hosting;
using QBSharp.Core.Syntax;

namespace QBSharp.Tests;

public sealed class ParserTests
{
    [Fact]
    public void LexerCoordinatesEscapesCommentsAndUnicode()
    {
        var result = new Lexer().Scan(new("' comment\r\nDim città = \"a\"\"b\"\n\t42", "lexer.bas"));
        Assert.Empty(result.Diagnostics);
        var name = Assert.Single(result.Tokens, t => t.Text == "città");
        Assert.Equal(2, name.Span.Start.Line); Assert.Equal(5, name.Span.Start.Column); Assert.Equal(5, name.Span.Length);
        Assert.Equal("a\"b", Assert.Single(result.Tokens, t => t.Kind == TokenKind.String).Value);
        var number = Assert.Single(result.Tokens, t => t.Kind == TokenKind.Number);
        Assert.Equal(3, number.Span.Start.Line); Assert.Equal(2, number.Span.Start.Column);
        Assert.Equal(2, result.Tokens.Count(t => t.Kind == TokenKind.NewLine));
    }
    [Theory]
    [InlineData("\"unterminated", "QBS1002")]
    [InlineData("@", "QBS1003")]
    [InlineData("9223372036854775808", "QBS1001")]
    public void LexicalErrors(string code, string diagnostic)
    {
        var result = new ScriptParser().ParseExpression(new(code)); Assert.False(result.Success); Assert.Null(result.Root); Assert.Equal(diagnostic, result.Diagnostics[0].Code);
    }
    [Theory]
    [InlineData("Dim = 1")]
    [InlineData("If True Then\nDim x = 1")]
    [InlineData("While True\nEnd If")]
    [InlineData("Return 1")]
    [InlineData("Dim x = 1 : x = 2")]
    [InlineData("Print 1")]
    [InlineData("Function F(x, X)\nEnd Function")]
    [InlineData("Else")]
    [InlineData("Dim x =")]
    [InlineData("Dim If = 1")]
    public void SyntaxErrorsAreDiagnosticsNotCrashes(string code)
    {
        var result = new ScriptParser().ParseScript(new(code, "syntax.bas")); Assert.False(result.Success); Assert.Null(result.Root); Assert.Equal("syntax.bas", result.Diagnostics[0].Span.File);
    }
    [Theory]
    [InlineData("1 2")]
    [InlineData("1\n2")]
    [InlineData("(1")]
    [InlineData("f(1,)")]
    [InlineData("obj.")]
    [InlineData("")]
    public void ExpressionParserRejectsTrailingOrIncompleteInput(string code) => Assert.False(new ScriptParser().ParseExpression(new(code)).Success);
    [Fact]
    public void AstShapeSpanAndAssignmentEqualityDisambiguation()
    {
        var parser = new ScriptParser();
        var expression = Assert.IsType<BinaryExpression>(parser.ParseExpression(new("2 + 3 * 4", "shape.bas")).Root);
        Assert.Equal(BinaryOperator.Add, expression.Operator); Assert.Equal(BinaryOperator.Multiply, Assert.IsType<BinaryExpression>(expression.Right).Operator);
        Assert.Equal(9, expression.Span.Length);
        var script = parser.ParseScript(new("obj.Value = 1\ncheck(x = 1)")); Assert.True(script.Success);
        Assert.IsType<MemberExpression>(Assert.IsType<Assignment>(script.Root!.Statements[0]).Target);
        var call = Assert.IsType<CallExpression>(Assert.IsType<ExpressionStatement>(script.Root.Statements[1]).Expression);
        Assert.Equal(BinaryOperator.Equal, Assert.IsType<BinaryExpression>(call.Arguments[0]).Operator);
        Assert.True(parser.ParseScript(new("\n' empty\n")).Success);
        Assert.True(parser.ParseExpression(new("1\n")).Success);
    }
    [Fact]
    public void CancellationAndNestingLimit()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel(); var parser = new ScriptParser();
        Assert.ThrowsAny<OperationCanceledException>(() => parser.ParseScript(new("Dim x = 1"), cts.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => parser.ParseExpression(new("1"), cts.Token));
        var deep = parser.ParseExpression(new(new string('(', 200) + "1" + new string(')', 200)));
        Assert.False(deep.Success); Assert.Contains("nesting", deep.Diagnostics[0].Message);
        Assert.True(parser.ParseExpression(new("1")).Success);
    }
}
