using Microsoft.Extensions.DependencyInjection;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.StandardLibrary;
using QBSharp.StandardLibrary.Serialization;
using QBSharp.StandardLibrary.UI;
using Spectre.Console;

namespace QBSharp.Tests;

public sealed class StandardUiTests
{
    private static IAnsiConsole ConsoleFor(TextWriter writer) => AnsiConsole.Create(new AnsiConsoleSettings { Ansi = AnsiSupport.No, Out = new AnsiConsoleOutput(writer) });
    [Fact]
    public async Task SpectrePanelsTablesAndProgressTreatUserTextAsLiteral()
    {
        using var writer = new StringWriter(); var ui = new SpectreTextUi(ConsoleFor(writer));
        await ui.WindowAsync("[red]Title[/]", "[green]Text[/]", default);
        Assert.Equal("OK", await ui.MessageBoxAsync("Info", "Hello", default));
        await ui.TableAsync(new[] { new Dictionary<string, object?> { ["[red]Column[/]"] = "[blue]Value[/]" } }, default);
        await ui.ProgressAsync(50, "[yellow]Deploy[/]", default);
        string text = writer.ToString(); Assert.Contains("[red]Title[/]", text); Assert.Contains("[green]Text[/]", text); Assert.Contains("[blue]Value[/]", text); Assert.Contains("50%", text);
    }
    [Fact]
    public async Task NonInteractivePromptsRejectWithoutReadingInput()
    {
        using var writer = new StringWriter(); var ui = new SpectreTextUi(ConsoleFor(writer));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await ui.InputBoxAsync("Name", "default", default));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await ui.SelectAsync("Pick", ["a"], default));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await ui.MultiSelectAsync("Pick", ["a"], default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await ui.ProgressAsync(101, "bad", default));
        await Assert.ThrowsAsync<ArgumentException>(async () => await ui.TableAsync("bad", default)); Assert.Empty(writer.ToString());
    }
    private sealed class FakeUi : ITextUi
    {
        public List<string> Calls { get; } = [];
        public ValueTask WindowAsync(string title, string text, CancellationToken token) { Calls.Add("window:" + title); return ValueTask.CompletedTask; }
        public ValueTask<string> MessageBoxAsync(string title, string text, CancellationToken token) { Calls.Add("message:" + text); return ValueTask.FromResult("OK"); }
        public ValueTask<string> InputBoxAsync(string prompt, string defaultValue, CancellationToken token) { Calls.Add("input:" + prompt); return ValueTask.FromResult("answer"); }
        public ValueTask<string> SelectAsync(string title, IReadOnlyList<string> items, CancellationToken token) { Calls.Add("select:" + title); return ValueTask.FromResult(items[0]); }
        public ValueTask<IReadOnlyList<string>> MultiSelectAsync(string title, IReadOnlyList<string> items, CancellationToken token) { Calls.Add("multi:" + title); return ValueTask.FromResult<IReadOnlyList<string>>(items.ToList()); }
        public ValueTask TableAsync(object? rows, CancellationToken token) { Calls.Add("table"); return ValueTask.CompletedTask; }
        public ValueTask ProgressAsync(double percent, string description, CancellationToken token) { Calls.Add("progress:" + percent); return ValueTask.CompletedTask; }
    }
    [Fact]
    public async Task EmbeddingUiDispatchUsesHostAdapterAndAllPublicUiFunctions()
    {
        var ui = new FakeUi(); var library = new StandardLibraryHost(new() { LoadConfiguration = false }, ui);
        await using var session = new ScriptEngine().CreateSession(await library.CreateBindingsAsync());
        await session.ExecuteAsync(new("""
            Ui.Window("Ops", "Text")
            Dim message = Ui.MessageBox("Hello")
            Dim answer = Ui.InputBox("Name", "default")
            Dim choices = Json.Deserialize("[""a"",""b""]")
            Dim menu = Ui.Menu("menu", choices)
            Dim selection = Ui.Select("select", choices)
            Dim many = Ui.MultiSelect("multi", choices)
            Ui.Table(Json.Deserialize("[{""value"":42}]"))
            Ui.Progress(50, "deploy")
            """));
        Assert.Equal("answer", session.Variables["answer"]); Assert.Equal("OK", session.Variables["message"]); Assert.Equal("a", session.Variables["menu"]);
        Assert.Equal(2, await session.EvaluateAsync(new("many.Count"))); Assert.Equal(8, ui.Calls.Count);
    }
    [Theory]
    [InlineData("Ui.Window(1, 2)")]
    [InlineData("Ui.Progress(-1)")]
    [InlineData("Ui.Select(\"pick\", Json.Deserialize(\"[]\"))")]
    [InlineData("File.ReadText()")]
    [InlineData("Path.Combine(1)")]
    [InlineData("Json.Serialize = Nothing")]
    public async Task StandardLibraryInvalidCallsHaveSourceDiagnostics(string expression)
    {
        var library = new StandardLibraryHost(new() { LoadConfiguration = false }, new FakeUi());
        await using var session = new ScriptEngine().CreateSession(await library.CreateBindingsAsync());
        var error = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(new(expression, "standard.bas")));
        Assert.Equal("standard.bas", error.Diagnostic.Span.File);
    }
    [Fact]
    public async Task NamespaceDiscoveryDynamicRecordGetSetAndDiRegistration()
    {
        var services = new ServiceCollection(); services.AddSingleton(new StandardLibraryOptions { LoadConfiguration = false }); services.AddQBSharpStandardLibrary().AddQBSharpStandardLibrary(); Assert.Equal(3, services.Count);
        using var provider = services.BuildServiceProvider(); var library = provider.GetRequiredService<StandardLibraryHost>();
        var bindings = await library.CreateBindingsAsync(); await using var session = new ScriptEngine().CreateSession(bindings);
        Assert.Equal(2, await session.EvaluateAsync(new("Json.Names.Count")));
        await session.ExecuteAsync(new("Dim data = Json.Deserialize(\"{\"\"x\"\":1}\")\ndata.SetValue(\"x\", 42)"));
        Assert.Equal(42, await session.EvaluateAsync(new("data.GetValue(\"X\")"))); Assert.Equal(1, await session.EvaluateAsync(new("data.Count")));
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.EvaluateAsync(new("Ui.InputBox(\"Name\")")));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await library.CreateBindingsAsync(cts.Token));
    }
}
