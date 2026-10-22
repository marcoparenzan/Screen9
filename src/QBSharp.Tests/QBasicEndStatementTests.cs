using QBSharp.Core.Diagnostics;
using QBSharp.QBasic;

namespace QBSharp.Tests;

public sealed class QBasicEndStatementTests
{
    [Theory]
    [InlineData("IF 1 THEN\nEND\nELSE\nx=1\nEND IF\nafter=1")]
    [InlineData("Probe\nafter=1\nSUB Probe\nEND\nEND SUB")]
    [InlineData("Probe()\nafter=1\nFUNCTION Probe\nEND\nEND FUNCTION")]
    [InlineData("SELECT CASE 1\nCASE 1\nEND\nEND SELECT\nafter=1")]
    public async Task StandaloneEndTerminatesProgramNotSyntacticBlock(string source)
    {
        await using var session=new QBasicEngine().CreateSession();await session.ExecuteAsync(new(source));
        Assert.DoesNotContain("after",session.Variables.Keys);
    }
    [Fact]
    public async Task EndInUnexecutedBranchDoesNotTerminate()
    {
        await using var session=new QBasicEngine().CreateSession();await session.ExecuteAsync(new("IF 0 THEN\nEND\nELSE\nx=42\nEND IF\nafter=x"));
        Assert.Equal(42f,session.Variables["after"]);
    }
    [Fact]
    public async Task MismatchedEndDoesNotExecutePartially()
    {
        await using var session=new QBasicEngine().CreateSession();
        await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("x=1\nIF 1 THEN\nEND SUB")));
        Assert.DoesNotContain("x",session.Variables.Keys);
    }
}
