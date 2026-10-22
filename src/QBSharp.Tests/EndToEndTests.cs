using QBSharp.Core.Hosting;

namespace QBSharp.Tests;

public sealed class EndToEndTests
{
    [Theory]
    [InlineData("hello.bas", "Hello, QBSharp")]
    [InlineData("ops-core.bas", "total=30")]
    public async Task BasFixturesExecuteThroughEmbedding(string file, string expected)
    {
        var output = new List<string>(); var bindings = new HostBindings();
        bindings.RegisterFunction("Print", (_, args) => { output.Add(string.Join("", args)); return ValueTask.FromResult<object?>(null); });
        await using var session = new ScriptEngine().CreateSession(bindings);
        var path = Path.Combine(AppContext.BaseDirectory, "samples", file);
        await session.ExecuteAsync(new(await File.ReadAllTextAsync(path), path));
        Assert.Equal(expected, Assert.Single(output));
    }
}
