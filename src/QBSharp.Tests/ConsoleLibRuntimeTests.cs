using System.Diagnostics;
using System.Text;
using Screen9;
using QBSharp.Cli;
using QBSharp.Cli.Editor;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.StandardLibrary;
using QBSharp.StandardLibrary.UI;

namespace QBSharp.Tests;

public sealed class ConsoleLibRuntimeTests
{
    private sealed class Renderer : ICanvasRenderer
    {
        public int Calls; public Cell[]? Cells; public int X, Y;
        public CanvasSize GetViewportSize() => new(80, 25);
        public void Render(Canvas canvas, int x = 0, int y = 0) { Calls++; Cells = canvas.Snapshot(); X = x; Y = y; }
    }
    [Fact]
    public async Task ScreenApiUsesRealConsoleLibModelAndInjectedRenderer()
    {
        var renderer = new Renderer(); var host = new StandardLibraryHost(new() { LoadConfiguration = false }, renderer: renderer);
        await using var session = new ScriptEngine().CreateSession(await host.CreateBindingsAsync());
        await session.ExecuteAsync(new("""
            Dim c = Screen.Create(20, 12)
            Screen.Clear(c, " ", 15, 1)
            Screen.Text(c, 1, 1, "Hello", 14, 1)
            Screen.Line(c, -2147483647, 3, 2147483647, 3, "+")
            Screen.Rectangle(c, 3, 5, 4, 3, "#", 7, 0, True)
            Screen.Ellipse(c, 10, 8, 3, 2, "*", 11, 0)
            Screen.Circle(c, 16, 8, 2, "@", 3, 0, True)
            Dim sprite = Screen.Get(c, 1, 1, 5, 1)
            Screen.Put(c, sprite, 1, 10)
            Dim view = Screen.Viewport()
            Screen.Render(c, 2, 1)
            """));
        Assert.Equal(typeof(Canvas), session.Variables["c"]!.GetType()); Assert.Equal(1, renderer.Calls); Assert.Equal((2, 1), (renderer.X, renderer.Y));
        Assert.Equal("H", await session.EvaluateAsync(new("Screen.Point(c, 1, 10).Character")));
        Assert.Equal(14, await session.EvaluateAsync(new("Screen.Point(c, 1, 10).Foreground")));
        Assert.Equal("#", await session.EvaluateAsync(new("Screen.Point(c, 4, 6).Character")));
        Assert.Equal("@", await session.EvaluateAsync(new("Screen.Point(c, 16, 8).Character")));
        Assert.Equal(80, await session.EvaluateAsync(new("view.Width")));
        Assert.Equal(false, await session.EvaluateAsync(new("Screen.Pixel(c, -1, 0, \"x\")")));
        Assert.Equal(true, await session.EvaluateAsync(new("Screen.Pixel(c, 0, 0, \"x\", 2, 0)")));
        Assert.Equal(12, await session.EvaluateAsync(new("Screen.Snapshot(c).Length")));
        Assert.Equal("x", Assert.IsType<string[]>(await session.EvaluateAsync(new("Screen.Snapshot(c)")))[0][..1]);
    }
    [Theory]
    [InlineData("Screen.Create(301, 1)")]
    [InlineData("Screen.Create(0, 1)")]
    [InlineData("Screen.Create(1, 101)")]
    [InlineData("Screen.Pixel(c, 1, 1, \"xx\")")]
    [InlineData("Screen.Clear(c, \"x\", 16)")]
    [InlineData("Screen.Ellipse(c, 0, 0, -1, 2, \"x\")")]
    [InlineData("Screen.Get(c, 5, 5, 100, 100)")]
    [InlineData("Screen.Put(c, 1, 0, 0)")]
    [InlineData("Screen.Text(c, 0, 0, \"a\tb\")")]
    [InlineData("Screen.Render(c)")]
    public async Task InvalidCanvasCallsAreSourceDiagnosticsAndDoNotNeedNativeConsole(string expression)
    {
        await using var session = new ScriptEngine().CreateSession(await new StandardLibraryHost(new() { LoadConfiguration = false }).CreateBindingsAsync());
        await session.ExecuteAsync(new("Dim c = Screen.Create(10, 10)"));
        var error = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.EvaluateAsync(new(expression, "canvas.bas")));
        Assert.Equal("canvas.bas", error.Diagnostic.Span.File); Assert.Equal("QBS2001", error.Diagnostic.Code);
    }
    [Fact]
    public async Task ConsoleLibUiRendersLiteralTextAndUsesNumericKeyboardDialogs()
    {
        using var writer = new StringWriter(); var console = new TextConsole(writer, new StringReader("\n2\n1,2\n\n"), true); var ui = new ConsoleLibTextUi(console);
        Assert.Equal("default", await ui.InputBoxAsync("Name", "default", default));
        Assert.Equal("b", await ui.SelectAsync("Pick", ["a", "b"], default));
        Assert.Equal(new[] { "a", "b" }, await ui.MultiSelectAsync("Many", ["a", "b"], default));
        Assert.Equal("OK", await ui.MessageBoxAsync("[red]", "literal [red]", default));
        await ui.WindowAsync("Panel", "contents", default); await ui.ProgressAsync(100, "Done", default);
        await ui.TableAsync(new QBSharp.StandardLibrary.Serialization.JsonCodec().Deserialize("[{\"Name\":\"[red]\",\"Count\":42}]"), default);
        Assert.Contains("[red]", writer.ToString()); Assert.DoesNotContain('\u001b', writer.ToString()); Assert.Contains("Done: 100%", writer.ToString());
        Assert.Contains("Count", writer.ToString()); Assert.Contains("42", writer.ToString());
    }
    [Fact]
    public async Task CancellationAndRedirectionDoNotReadPromptInput()
    {
        using var input = new StringReader("unused"); using var output = new StringWriter(); var ui = new ConsoleLibTextUi(new TextConsole(output, input));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await ui.InputBoxAsync("Name", "", default)); Assert.Equal("unused", input.ReadLine());
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await ui.MessageBoxAsync("Title", "Message", cts.Token)); Assert.Empty(output.ToString());
        await using var session = new ScriptEngine().CreateSession(await new StandardLibraryHost(new() { LoadConfiguration = false }).CreateBindingsAsync());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await session.EvaluateAsync(new("Screen.Create(2,2)"), cts.Token));
    }
    [Fact]
    public void RuntimeAssemblyNoLongerReferencesSpectre()
    {
        Assert.DoesNotContain(typeof(StandardLibraryHost).Assembly.GetReferencedAssemblies(), assembly => assembly.Name!.StartsWith("Spectre", StringComparison.Ordinal));
        Assert.Equal("QBSharp.Cli", typeof(SpectreTextUi).Assembly.GetName().Name);
        Assert.Contains(typeof(StandardLibraryHost).Assembly.GetReferencedAssemblies(), assembly => assembly.Name == "Screen9");
    }
    [Fact]
    public async Task CanvasSampleRunsAsLiteralSnapshotInActualRedirectedCli()
    {
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var arg in new[] { typeof(CliApplication).Assembly.Location, "run", Path.Combine(AppContext.BaseDirectory, "samples", "canvas.bas") }) info.ArgumentList.Add(arg);
        info.Environment["QBS_AI_PROVIDER"] = "";
        using var process = Process.Start(info)!; var output = process.StandardOutput.ReadToEndAsync(); var errors = process.StandardError.ReadToEndAsync(); process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); } finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
        Assert.Equal(0, process.ExitCode); Assert.Empty(await errors); string text = await output;
        Assert.Contains("Screen9", text); Assert.DoesNotContain('\u001b', text); Assert.Equal(12, text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }
    [Fact]
    public async Task EditorCapturesConsoleLibCanvasWithoutTouchingItsNativeScreen()
    {
        await using var runner = new EditorRunner(); runner.Start("Dim c = Screen.Create(5,2)\nScreen.Text(c,0,0,\"HELLO\")\nScreen.Render(c)", null);
        Assert.Equal("Completed", (await runner.WaitAsync()).Status); Assert.True(runner.TryReadOutput(out var first)); Assert.Equal("HELLO", first);
    }
}
