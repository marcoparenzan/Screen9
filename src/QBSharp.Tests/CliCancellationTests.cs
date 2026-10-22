namespace QBSharp.Tests;

public sealed class CliCancellationTests
{
    [Theory]
    [InlineData("run")]
    [InlineData("test")]
    [InlineData("repl")]
    public async Task CancellationDuringCpuLoopReturns130(string command)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        string[] args = command == "repl" ? [command, "--max-steps", long.MaxValue.ToString()] : [command, "-", "--max-steps", long.MaxValue.ToString()];
        var result = await CliTests.Run(args, "While True\nEnd While\n", token: cts.Token);
        Assert.Equal(130, result.Code); Assert.Contains("interrotta", result.Error);
    }
}
