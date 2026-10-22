using System.Diagnostics;
using System.Text;
using QBSharp.Cli;

namespace QBSharp.Tests;

public sealed class CliBomTests
{
    [Theory]
    [InlineData("run")]
    [InlineData("repl")]
    public async Task RedirectedUtf8BomAndUnicodeAreAccepted(string command)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.Environment.Remove("QBS_MAX_STEPS");
        start.ArgumentList.Add(typeof(CliApplication).Assembly.Location); start.ArgumentList.Add(command);
        if (command == "run") start.ArgumentList.Add("-");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.StandardInput.WriteAsync("\uFEFFPrint(\"città\")\n" + (command == "repl" ? ":quit\n" : "")); process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await process.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, process.ExitCode); Assert.Equal("città", (await output).Trim()); Assert.Empty(await error);
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
}
