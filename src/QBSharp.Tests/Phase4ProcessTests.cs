using System.Diagnostics;
using System.Text;
using QBSharp.Cli;

namespace QBSharp.Tests;

public sealed class Phase4ProcessTests
{
    [Theory]
    [InlineData("run", "phase4.bas", "class=42")]
    [InlineData("test", "phase4-test.bas", "PASS")]
    public async Task RealCliProcessesExecuteImportedClassesAndClrCollections(string command, string file, string expected)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetTempPath()
        };
        start.Environment.Remove("QBS_MAX_STEPS"); start.ArgumentList.Add(typeof(CliApplication).Assembly.Location);
        start.ArgumentList.Add(command); start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "samples", file));
        using var process = Process.Start(start)!; process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode); Assert.Contains(expected, await output); Assert.Empty(await error);
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
}
