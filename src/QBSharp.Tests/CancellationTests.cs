using QBSharp.Core.Hosting;
using QBSharp.Core.Syntax;

namespace QBSharp.Tests;

public sealed class CancellationTests
{
    [Fact]
    public void EmptySourcesHonorCancellationInLexerAndParser()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel(); var source = new ScriptSource("");
        Assert.ThrowsAny<OperationCanceledException>(() => new Lexer().Scan(source, cts.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => new ScriptParser().ParseScript(source, cts.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => new ScriptParser().ParseExpression(source, cts.Token));
    }
}
