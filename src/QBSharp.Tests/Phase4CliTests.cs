using QBSharp.Core.Hosting;

namespace QBSharp.Tests;

public sealed class Phase4CliTests
{
    [Fact]
    public async Task FixturesExecuteViaEmbeddingAndCli()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "samples", "phase4.bas"); var output = new List<object?>();
        var bindings = new HostBindings(); bindings.RegisterFunction("Print", (_, args) => { output.Add(args[0]); return ValueTask.FromResult<object?>(null); });
        await using var session = new ScriptEngine().CreateSession(bindings); await session.ExecuteAsync(new(await File.ReadAllTextAsync(path), path)); Assert.Equal("class=42", Assert.Single(output));
        var run = await CliTests.Run(["run", path]); Assert.Equal(0, run.Code); Assert.Equal("class=42", run.Output.Trim());
        var test = await CliTests.Run(["test", Path.Combine(AppContext.BaseDirectory, "samples", "phase4-test.bas")]); Assert.Equal(0, test.Code); Assert.Contains("PASS", test.Output);
    }
    [Fact]
    public async Task ReplBuffersPublicClassesNamespaceAndSub()
    {
        var result = await CliTests.Run(["repl"], "Namespace Ops\nPublic Class C\nProperty X As Integer\nPublic Sub New()\nMe.X = 42\nEnd Sub\nEnd Class\nEnd Namespace\nNew Ops.C().X\n:quit\n");
        Assert.Equal(0, result.Code); Assert.Equal("42", result.Output.Trim()); Assert.Empty(result.Error);
    }
}
