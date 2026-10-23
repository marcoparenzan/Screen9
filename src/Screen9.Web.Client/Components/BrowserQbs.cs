using System.Globalization;
using System.Text;
using Microsoft.JSInterop;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.StandardLibrary;
using QBSharp.StandardLibrary.UI;

namespace Screen9.Web.Client.Components;

/// <summary>Runs a program in the modern QBS dialect (<c>ScriptEngine</c> with the standard library: <c>Print</c>, <c>Json</c>,
/// <c>Ui</c>, <c>Screen</c>…) inside the browser, as <c>EditorRunner</c> does on the server. Its output is text, so it goes to the
/// log; the <c>Ui</c> dialogs are the browser's own (<c>prompt</c>/<c>alert</c>). No files (the browser has none) and no Ralf.</summary>
public static class BrowserQbs
{
    /// <summary>Returns "Completed", "Canceled" or "Error". Errors are also written to <paramref name="output"/>.</summary>
    public static async Task<string> RunAsync(string source, string fileName, IJSRuntime js, Action<string> output, CancellationToken token)
    {
        try
        {
            using var sink = new LineWriter(output);
            var console = new TextConsole(sink);
            var bindings = await new StandardLibraryHost(new StandardLibraryOptions { LoadConfiguration = false }, new BrowserUi(js, sink, output),
                new TextCanvasRenderer(sink, new(300, 100))).CreateBindingsAsync(token);
            bindings.SetValue("Args", Array.Empty<string>());
            bindings.RegisterFunction("Print", (_, args) =>
            {
                console.WriteLine(string.Join(" ", args.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture) ?? "")));
                return ValueTask.FromResult<object?>(null);
            });
            var engine = new ScriptEngine(options: new ScriptEngineOptions { MaximumSteps = 1_000_000 });
            await using var session = engine.CreateSession(bindings);
            await session.ExecuteAsync(new ScriptSource(source, fileName), token);
            return "Completed";
        }
        catch (OperationCanceledException) { return "Canceled"; }
        catch (ScriptRuntimeException error) { output(error.Diagnostic.ToString() ?? error.Message); return "Error"; }
        catch (Exception error) { output(error.Message); return "Error"; }
    }

    /// <summary>Text written by the program, one log line per line.</summary>
    private sealed class LineWriter(Action<string> emit) : TextWriter
    {
        private readonly StringBuilder pending = new();
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value)
        {
            if (value == '\n') { emit(pending.ToString()); pending.Clear(); }
            else if (value != '\r' && pending.Length < 262144) pending.Append(value);
        }
        public override void Flush() { if (pending.Length > 0) { emit(pending.ToString()); pending.Clear(); } }
        protected override void Dispose(bool disposing) { if (disposing) Flush(); base.Dispose(disposing); }
    }

    /// <summary>The <c>Ui</c> of the standard library over the page: messages and progress go to the log, questions are the browser's dialogs.</summary>
    private sealed class BrowserUi(IJSRuntime js, TextWriter sink, Action<string> output) : ITextUi
    {
        public ValueTask WindowAsync(string title, string text, CancellationToken token)
        { token.ThrowIfCancellationRequested(); output(title + ": " + text); return ValueTask.CompletedTask; }

        public async ValueTask<string> MessageBoxAsync(string title, string text, CancellationToken token)
        { token.ThrowIfCancellationRequested(); await js.InvokeVoidAsync("alert", token, title + ": " + text); return "OK"; }

        public async ValueTask<string> InputBoxAsync(string prompt, string defaultValue, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return await js.InvokeAsync<string?>("prompt", token, prompt, defaultValue) ?? throw new OperationCanceledException("Input cancelled.");
        }

        public async ValueTask<string> SelectAsync(string title, IReadOnlyList<string> items, CancellationToken token)
        {
            string answer = await InputBoxAsync(Numbered(title, items), "1", token);
            return items[Pick(answer, items.Count)];
        }

        public async ValueTask<IReadOnlyList<string>> MultiSelectAsync(string title, IReadOnlyList<string> items, CancellationToken token)
        {
            string answer = await InputBoxAsync(Numbered(title + " (numbers separated by commas)", items), "", token);
            return answer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(n => items[Pick(n, items.Count)]).ToList();
        }

        public async ValueTask TableAsync(object? rows, CancellationToken token)
        {
            using var writer = new StringWriter(CultureInfo.InvariantCulture);
            await new ConsoleLibTextUi(new TextConsole(writer)).TableAsync(rows, token);
            foreach (var line in writer.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n')) output(line);
        }

        public ValueTask ProgressAsync(double percent, string description, CancellationToken token)
        { token.ThrowIfCancellationRequested(); new TextConsole(sink).Progress(percent, description); return ValueTask.CompletedTask; }

        private static string Numbered(string title, IReadOnlyList<string> items)
            => title + "\n" + string.Join("\n", items.Select((item, i) => $"{i + 1}. {item}"));

        private static int Pick(string answer, int count)
            => int.TryParse(answer, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= count ? n - 1 : throw new ArgumentException("Not one of the choices: " + answer);
    }
}
