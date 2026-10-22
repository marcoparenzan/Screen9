using QBSharp.Core.Hosting;
using QBSharp.Core.Syntax;
using QBSharp.StandardLibrary;

namespace QBSharp.Tests;

public sealed class Phase5MemberTests
{
    [Fact]
    public async Task KeywordsAreAllowedAfterDotButNotAsDeclarations()
    {
        var parser = new ScriptParser(); Assert.True(parser.ParseExpression(new("Ui.Select")).Success); Assert.False(parser.ParseScript(new("Dim Select = 1")).Success);
        await using var session = new ScriptEngine().CreateSession(await new StandardLibraryHost(new() { LoadConfiguration = false }).CreateBindingsAsync());
        await session.ExecuteAsync(new("Dim data = Json.Deserialize(\"{\"\"End\"\":1,\"\"Class\"\":2}\")\ndata.End = 42"));
        Assert.Equal(42, await session.EvaluateAsync(new("data.End"))); Assert.Equal(2, await session.EvaluateAsync(new("data.Class")));
    }
}
