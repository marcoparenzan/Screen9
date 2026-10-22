using System.Diagnostics;
using System.Text;
using QBSharp.Cli;

namespace QBSharp.Tests;

/// <summary>Real process tests of the built CLI, no dotnet run/restore and no global tool dependency.</summary>
public sealed class CliProcessTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "qbs-process-" + Guid.NewGuid());
    public CliProcessTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);
    private async Task<(int Code, string Output, string Error)> Run(string[] args, string stdin = "")
    {
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        info.Environment.Remove("QBS_MAX_STEPS"); info.Environment["NO_COLOR"] = "1";
        info.ArgumentList.Add(typeof(CliApplication).Assembly.Location);
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.StandardInput.WriteAsync(stdin); process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)); await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, (await output).Replace("\r\n", "\n"), await error);
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
    [Fact]
    public async Task HelpVersionAndUnknownCommandUseCorrectStreams()
    {
        var version = await Run(["--version"]); Assert.Equal(0, version.Code); Assert.Equal(CliApplication.Version + "\n", version.Output); Assert.Empty(version.Error);
        Assert.Contains("qbs run", (await Run(["run", "--help"])).Output);
        var bad = await Run(["[red]bad[/]"]); Assert.Equal(2, bad.Code); Assert.Empty(bad.Output); Assert.Contains("[red]bad[/]", bad.Error);
    }
    [Fact]
    public async Task Utf8FileArgumentsAndMarkupReachStdoutLiterally()
    {
        string file = Path.Combine(directory, "space name.bas"); await File.WriteAllTextAsync(file, "Print(\"città\")\nPrint(Args.GetValue(0))", new UTF8Encoding(false));
        var result = await Run(["run", file, "--", "[red]a b[/]"]); Assert.Equal(0, result.Code); Assert.Equal("città\n[red]a b[/]\n", result.Output); Assert.Empty(result.Error);
    }
    [Fact]
    public async Task RedirectedStdinRunAndReplAreNonInteractive()
    {
        var run = await Run(["run", "-"], "Print(6 * 7)"); Assert.Equal(0, run.Code); Assert.Equal("42\n", run.Output);
        var repl = await Run(["repl"], "Dim x = 2\nFunction F(n)\nReturn n * 3\nEnd Function\nF(x)\n:quit\n");
        Assert.Equal(0, repl.Code); Assert.Equal("6\n", repl.Output); Assert.Empty(repl.Error);
    }
    [Fact]
    public async Task NewCreatesRunnableScriptAndNeverOverwrites()
    {
        Assert.Equal(0, (await Run(["new"])).Code); string original = await File.ReadAllTextAsync(Path.Combine(directory, "main.bas"));
        var overwrite = await Run(["new"]); Assert.Equal(5, overwrite.Code); Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(directory, "main.bas")));
        Assert.Equal("Hello, QBSharp\n", (await Run(["run", "main.bas"])).Output);
        Assert.Equal(2, (await Run(["new", "bad.txt"])).Code);
    }
    [Fact]
    public async Task ProcessExitCodesForSyntaxRuntimeMissingFileEncodingAndStepLimit()
    {
        await File.WriteAllTextAsync(Path.Combine(directory, "syntax.bas"), "Dim = 1");
        var syntax = await Run(["run", "syntax.bas"]); Assert.Equal(3, syntax.Code); Assert.Contains("syntax.bas(1,5)", syntax.Error); Assert.Empty(syntax.Output);
        Assert.Equal(4, (await Run(["run", "-"], "Print(1 / 0)")).Code);
        Assert.Equal(5, (await Run(["run", "missing.bas"])).Code);
        await File.WriteAllBytesAsync(Path.Combine(directory, "invalid.bas"), [0xFF, 0xFF]); Assert.Equal(5, (await Run(["run", "invalid.bas"])).Code);
        var loop = await Run(["run", "-", "--max-steps", "20"], "While True\nEnd While"); Assert.Equal(4, loop.Code); Assert.Contains("QBS2002", loop.Error);
    }
    [Fact]
    public async Task TestFixturesAndFailureAreActualProcesses()
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "samples", "assertions.bas");
        var pass = await Run(["test", fixture, "--", "sample"]); Assert.Equal(0, pass.Code); Assert.Contains("PASS", pass.Output); Assert.Empty(pass.Error);
        var fail = await Run(["test", "-"], "AssertEqual(2, 3, \"wrong\")"); Assert.Equal(1, fail.Code); Assert.Contains("wrong", fail.Error);
        Assert.Equal(1, (await Run(["test", "-"], "")).Code);
        Assert.Equal(3, (await Run(["test", "-"], "Dim = 1")).Code);
    }
    [Fact]
    public async Task CompletionIsPlainOneCandidatePerLine()
    {
        Assert.Equal("run\n", (await Run(["complete", "ru"])).Output);
        await File.WriteAllTextAsync(Path.Combine(directory, "space name.bas"), "");
        Assert.Equal("space name.bas\n", (await Run(["complete", "spa"])).Output);
        Assert.Equal("--max-steps\n", (await Run(["complete", "--m"])).Output);
    }
}
