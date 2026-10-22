using System.Diagnostics;
using System.Text;
using QBSharp.Cli;

namespace QBSharp.Tests;

public sealed class Phase5ProcessTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "qbs-phase5-" + Guid.NewGuid());
    public Phase5ProcessTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private async Task<(int Code, string Output, string Error)> Run(string[] args, string stdin = "")
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        start.Environment.Remove("QBS_MAX_STEPS"); start.Environment["NO_COLOR"] = "1";
        start.ArgumentList.Add(typeof(CliApplication).Assembly.Location);
        foreach (var argument in args) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.StandardInput.WriteAsync(stdin); process.StandardInput.Close();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, (await output).Replace("\r\n", "\n"), await error);
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "samples", name);
    [Theory]
    [InlineData("run", "phase5.bas", "deploy:42")]
    [InlineData("test", "phase5-test.bas", "PASS")]
    public async Task BasFixturesExecuteWithStandardLibrary(string command, string file, string expected)
    {
        var result = await Run([command, Fixture(file), "--", "scratch"]);
        Assert.Equal(0, result.Code); Assert.Empty(result.Error); Assert.Contains(expected, result.Output);
        Assert.False(Directory.Exists(Path.Combine(root, "scratch")));
    }
    [Theory]
    [InlineData("qbs.yaml", "run")]
    [InlineData("qbs.yml", "repl")]
    public async Task ConfigurationComesFromCwdNotScriptDirectory(string name, string command)
    {
        await File.WriteAllTextAsync(Path.Combine(root, name), "ops:\n  name: deploy\n  count: 42");
        string code = "Print(Config.Data.ops.count)\nPrint(Config.Get(\"ops.name\"))\nPrint(Config.Get(\"missing\", \"fallback\"))";
        var result = command == "run" ? await Run(["run", "-"], code) : await Run(["repl"], code + "\n:quit\n");
        Assert.Equal(0, result.Code); Assert.Equal("42\ndeploy\nfallback\n", result.Output); Assert.Empty(result.Error);
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        await File.WriteAllTextAsync(Path.Combine(root, "scripts", "main.bas"), "Print(Config.Data.ops.name)");
        Assert.Equal("deploy\n", (await Run(["run", "scripts/main.bas"])).Output);
    }
    [Theory]
    [InlineData("x: !Custom value", false)]
    [InlineData("[1, 2]", false)]
    [InlineData("{}", true)]
    public async Task InvalidConfigurationFailsBeforeScriptExecution(string yaml, bool ambiguous)
    {
        await File.WriteAllTextAsync(Path.Combine(root, "qbs.yaml"), yaml);
        if (ambiguous) await File.WriteAllTextAsync(Path.Combine(root, "qbs.yml"), "{}");
        var result = await Run(["run", "-"], "Print(\"should-not-run\")");
        Assert.Equal(CliExitCodes.Io, result.Code); Assert.Empty(result.Output); Assert.NotEmpty(result.Error);
        Assert.Equal(0, (await Run(["--version"])).Code); // Informational commands do not load config.
    }
    [Fact]
    public async Task RedirectedReplSupportsDataKeywordsAndNonBlockingMessageBox()
    {
        var result = await Run(["repl"], "Dim data = Json.Deserialize(\"{\"\"Select\"\":42}\")\ndata.Select\nYaml.Deserialize(Yaml.Serialize(data)).Select\nUi.MessageBox(\"literal [red] text\")\nUi.Progress(100, \"done\")\n:quit\n");
        Assert.Equal(0, result.Code); Assert.Empty(result.Error);
        Assert.StartsWith("42\n42\n", result.Output); Assert.Contains("literal [red] text", result.Output);
        Assert.Contains("OK\n", result.Output); Assert.Contains("done: 100%", result.Output);
    }
    [Theory]
    [InlineData("Ui.InputBox(\"name\")")]
    [InlineData("Ui.Select(\"pick\", Json.Deserialize(\"[\"\"a\"\",\"\"b\"\"]\"))")]
    public async Task InteractiveDialogsRejectRedirectedInputWithCallSite(string expression)
    {
        var result = await Run(["run", "-"], expression);
        Assert.Equal(CliExitCodes.Runtime, result.Code); Assert.Empty(result.Output);
        Assert.Contains("interactive terminal", result.Error); Assert.Contains("<stdin>(1,", result.Error);
    }
}
