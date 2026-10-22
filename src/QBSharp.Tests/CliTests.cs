using Microsoft.Extensions.Configuration;
using QBSharp.Cli;
using Spectre.Console;

namespace QBSharp.Tests;

public sealed class CliTests
{
    private static IAnsiConsole ConsoleFor(TextWriter writer) => AnsiConsole.Create(new AnsiConsoleSettings
    { Ansi = AnsiSupport.No, ColorSystem = ColorSystemSupport.NoColors, Out = new AnsiConsoleOutput(writer) });
    public static async Task<(int Code, string Output, string Error)> Run(string[] args, string stdin = "", IConfiguration? configuration = null, CancellationToken token = default)
    {
        using var output = new StringWriter(); using var error = new StringWriter();
        var app = new CliApplication(ConsoleFor(output), ConsoleFor(error), new StringReader(stdin), configuration: configuration);
        int code = await app.RunAsync(args, token); return (code, output.ToString(), error.ToString());
    }
    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public async Task HelpReportsAvailableCommands(string arg)
    {
        var result = await Run([arg]); Assert.Equal(0, result.Code); Assert.Contains("fase 7", result.Output); Assert.Contains("run file.bas", result.Output); Assert.Empty(result.Error);
    }
    [Theory]
    [InlineData("run")]
    [InlineData("repl")]
    [InlineData("new")]
    [InlineData("test")]
    [InlineData("complete")]
    [InlineData("edit")]
    public async Task ContextualHelp(string command)
    {
        var result = await Run([command, "--help"]); Assert.Equal(0, result.Code); Assert.Contains("qbs " + command, result.Output);
        Assert.Equal(0, (await Run(["help", command])).Code);
    }
    [Fact]
    public async Task DefaultsAndVersion()
    {
        Assert.Equal(0, (await Run([])).Code);
        var result = await Run(["--version"]); Assert.Equal(CliApplication.Version, result.Output.Trim()); Assert.Empty(result.Error);
    }
    [Theory]
    [InlineData("unknown")]
    [InlineData("[red]unsafe[/]")]
    [InlineData("run")]
    [InlineData("test")]
    [InlineData("edit")]
    public async Task UsageFailureOnlyOnStderr(string command)
    {
        var result = await Run([command]); Assert.Equal(2, result.Code); Assert.Empty(result.Output); Assert.Contains(command == "edit" ? "Editor" : command, result.Error);
    }
    [Fact]
    public async Task RunStdinOutputArgumentsAndLiteralMarkup()
    {
        var result = await Run(["run", "-", "--", "[red]safe[/]"], "Print(Args.GetValue(0))\nPrint(1, 2)\nPrint()");
        Assert.Equal(0, result.Code); Assert.Equal("[red]safe[/]\n1 2\n\n", result.Output.Replace("\r\n", "\n")); Assert.Empty(result.Error);
        string longText = new('x', 300);
        var longResult = await Run(["run", "-", "--", longText], "Print(Args.GetValue(0))"); Assert.Equal(longText, longResult.Output.TrimEnd('\r', '\n'));
    }
    [Fact]
    public async Task SyntaxRuntimeIoAndLimitsAreDistinct()
    {
        Assert.Equal(3, (await Run(["run", "-"], "Dim = 1")).Code);
        var runtime = await Run(["run", "-"], "Dim x = 1\nx = missing"); Assert.Equal(4, runtime.Code); Assert.Contains("<stdin>(2,5)", runtime.Error);
        Assert.Equal(5, (await Run(["run", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bas")])).Code);
        var limit = await Run(["run", "-", "--max-steps", "10"], "While True\nEnd While"); Assert.Equal(4, limit.Code); Assert.Contains("QBS2002", limit.Error);
    }
    [Fact]
    public async Task EnvironmentConfigurationAndCliPrecedence()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["QBS_MAX_STEPS"] = "1" }).Build();
        Assert.Equal(4, (await Run(["run", "-"], "Print(1)", config)).Code);
        Assert.Equal(0, (await Run(["run", "-", "--max-steps", "100"], "Print(1)", config)).Code);
        var bad = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["QBS_MAX_STEPS"] = "bad" }).Build(); Assert.Equal(2, (await Run(["run", "-"], "", bad)).Code);
    }
    [Fact]
    public async Task TestAssertionsStrictEqualityFailuresAndNoAssertions()
    {
        var success = await Run(["test", "-"], "Assert(True)\nAssertEqual(42, 6 * 7)\nAssertEqual(Nothing, Nothing)");
        Assert.Equal(0, success.Code); Assert.Contains("PASS", success.Output); Assert.Contains("3", success.Output);
        var failed = await Run(["test", "-"], "Assert(False, \"[red]failure[/]\")\nPrint(\"never\")");
        Assert.Equal(1, failed.Code); Assert.Contains("[red]failure[/]", failed.Error); Assert.DoesNotContain("never", failed.Output);
        Assert.Equal(1, (await Run(["test", "-"], "AssertEqual(1, 1.0)")).Code);
        Assert.Equal(1, (await Run(["test", "-"], "Print(\"no tests\")")).Code);
        Assert.Equal(4, (await Run(["test", "-"], "Assert(1)")).Code);
        Assert.Equal(4, (await Run(["test", "-"], "AssertEqual(1)")).Code);
        Assert.Equal(4, (await Run(["run", "-"], "Assert(True)")).Code);
    }
    [Fact]
    public async Task ReplPersistsVariablesFunctionsAndNestedBlocksWithoutPrompts()
    {
        var result = await Run(["repl"], "Dim x = 1\nx = x + 1\nx\nFunction Twice(n As Integer)\nIf n > 0 Then\nReturn n * 2\nElse\nReturn 0\nEnd If\nEnd Function\nTwice(x)\n:quit\n");
        Assert.Equal(0, result.Code); Assert.Equal("2\n4\n", result.Output.Replace("\r\n", "\n")); Assert.Empty(result.Error); Assert.DoesNotContain("qbs>", result.Output);
    }
    [Fact]
    public async Task ReplCommandsResetCancelVarsCompletionAndErrors()
    {
        var result = await Run(["repl"], "Dim xyz = 7\n:vars\n:complete xy\nIf True Then\n:cancel\n:reset\n:help\nxyz\n1 + 2\n:quit\n");
        Assert.Equal(4, result.Code); Assert.Contains("xyz", result.Output); Assert.Contains("Int32", result.Output); Assert.Contains(":cancel", result.Output); Assert.Contains("3", result.Output);
        Assert.Equal(2, (await Run(["repl"], ":unknown\n")).Code);
        Assert.Equal(3, (await Run(["repl"], "If True Then\n")).Code);
        Assert.Equal(3, (await Run(["repl"], "If True Then\n\n1 + 2\n")).Code);
        Assert.Equal(0, (await Run(["repl"], "")).Code);
        Assert.Equal(0, (await Run(["repl"], "If True Then\n:quit\n")).Code);
    }
    [Fact]
    public async Task CancellationHasDedicatedExit()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel(); var result = await Run(["run", "-"], "", token: cts.Token); Assert.Equal(130, result.Code); Assert.Contains("interrotta", result.Error);
    }
    [Fact]
    public async Task CompletionSupportsOptionPrefixes()
    {
        var result = await Run(["complete", "--m"]); Assert.Equal(0, result.Code); Assert.Equal("--max-steps", result.Output.Trim());
        // Filesystem entries may also begin with 'ru' in the runner's cwd.
        Assert.Contains("run", (await Run(["complete", "ru"])).Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }
}
