using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;

namespace QBSharp.Tests;

public sealed class SafetyTests
{
    [Fact]
    public async Task FlatLeftDeepExpressionsAreRejectedBeforeEvaluation()
    {
        await using var session = new ScriptEngine().CreateSession();
        var code = string.Join(" + ", Enumerable.Repeat("1", 300));
        var error = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.EvaluateAsync(new(code)));
        Assert.Equal("QBS1101", error.Diagnostic.Code);
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(new("Dim x = " + code)));
        Assert.Empty(session.Variables);
        Assert.Equal(42, await session.EvaluateAsync(new("42")));
    }
    [Fact]
    public async Task ClrOverflowAndIntegralNarrowingAreChecked()
    {
        await using var session = new ScriptEngine().CreateSession();
        session.SetVariable("money", decimal.MaxValue);
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.EvaluateAsync(new("money + 1")));
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(new("Dim x As Integer = 2147483648")));
        session.SetVariable("minimum", int.MinValue);
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.EvaluateAsync(new("-minimum")));
    }
    [Fact]
    public async Task UnicodeAndBlockScopeDifferenceAreExplicit()
    {
        await using var session = new ScriptEngine().CreateSession();
        await session.ExecuteAsync(new("If True Then\nDim città As Integer = 4\nEnd If\nCITTÀ = città + 1"));
        Assert.Equal(5, session.Variables["città"]);
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(new("While città > 0\nDim once = 1\ncittà = città - 1\nEnd While")));
    }
}
