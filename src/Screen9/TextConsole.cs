using System.Globalization;
using System.Text;

namespace Screen9;

/// <summary>Literal text output and simple keyboard dialogs, without Spectre or markup interpretation.</summary>
public sealed class TextConsole(TextWriter output, TextReader? input = null, bool interactive = false)
{
    public bool Interactive { get; } = interactive;
    public TextWriter Output { get; } = output ?? throw new ArgumentNullException(nameof(output));
    private readonly TextReader reader = input ?? TextReader.Null;
    public static string SafeText(string value) => string.Concat(value.Select(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t') ? '\uFFFD' : c));
    public void Write(string text) => Output.Write(SafeText(text));
    public void WriteLine(string text = "") => Output.WriteLine(SafeText(text));
    public void Window(string title, string text)
    {
        var lines = SafeText(text).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        title = SafeText(title).Replace('\n', ' ').Replace('\r', ' ');
        int width = Math.Min(120, Math.Max(title.Length + 2, lines.Max(line => line.Length)));
        WriteLine("+" + new string('-', width + 2) + "+");
        WriteLine("| " + title[..Math.Min(title.Length, width)].PadRight(width) + " |");
        foreach (var line in lines)
        {
            if (line.Length == 0) { WriteLine("| " + new string(' ', width) + " |"); continue; }
            for (int i = 0; i < line.Length; i += Math.Max(1, width)) WriteLine("| " + line.Substring(i, Math.Min(Math.Max(1, width), line.Length - i)).PadRight(width) + " |");
        }
        WriteLine("+" + new string('-', width + 2) + "+");
    }
    public async ValueTask<string> InputBoxAsync(string prompt, string defaultValue = "", CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); RequireInteractive(); Write(prompt + " "); Output.Flush();
        string? answer = await reader.ReadLineAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return answer is null ? throw new EndOfStreamException("Input closed.") : answer.Length == 0 ? defaultValue : answer;
    }
    public async ValueTask<string> MessageBoxAsync(string title, string text, CancellationToken token = default)
    { token.ThrowIfCancellationRequested(); Window(title, text); if (Interactive) await InputBoxAsync("Enter to continue", token: token); return "OK"; }
    public async ValueTask<IReadOnlyList<string>> SelectAsync(string title, IReadOnlyList<string> items, bool multiple = false, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); RequireInteractive();
        if (items.Count is < 1 or > 1000 || items.Distinct(StringComparer.Ordinal).Count() != items.Count) throw new ArgumentException("Expected 1..1000 unique choices.");
        WriteLine(title); for (int i = 0; i < items.Count; i++) WriteLine($"{i + 1}. {items[i]}");
        while (true)
        {
            string answer = await InputBoxAsync(multiple ? "Numbers separated by comma (empty: none):" : "Choice number:", token: token);
            if (multiple && answer.Length == 0) return Array.Empty<string>();
            var parts = answer.Split(',', StringSplitOptions.TrimEntries); var selected = new List<string>(); bool valid = true;
            foreach (var part in parts)
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 1 || n > items.Count || selected.Contains(items[n - 1], StringComparer.Ordinal)) { valid = false; break; }
                selected.Add(items[n - 1]);
            }
            if (valid && (multiple || selected.Count == 1)) return selected;
            WriteLine("Invalid choice.");
        }
    }
    public void Table(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (headers.Count is < 1 or > 100 || rows.Count > 1000 || rows.Any(row => row.Count != headers.Count)) throw new ArgumentException("Table limits: 100 columns, 1000 rows, consistent shape.");
        int[] widths = Enumerable.Range(0, headers.Count).Select(i => Math.Min(80, Math.Max(SafeText(headers[i]).Length, rows.Select(row => SafeText(row[i]).Length).DefaultIfEmpty(0).Max()))).ToArray();
        string Row(IReadOnlyList<string> values) => "| " + string.Join(" | ", values.Select((value, i) => { string text = SafeText(value).Replace('\n', ' ').Replace('\r', ' '); return text[..Math.Min(text.Length, widths[i])].PadRight(widths[i]); })) + " |";
        WriteLine(Row(headers)); WriteLine("+-" + string.Join("-+-", widths.Select(w => new string('-', w))) + "-+");
        foreach (var row in rows) WriteLine(Row(row));
    }
    public void Progress(double percent, string description = "Progress")
    { if (!double.IsFinite(percent) || percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent)); WriteLine(description + ": " + percent.ToString(CultureInfo.InvariantCulture) + "%"); }
    private void RequireInteractive() { if (!Interactive) throw new InvalidOperationException("This dialog requires interactive input; redirected input is not consumed."); }
}

/// <summary>Portable plain snapshot renderer for streams: no cursor movement, color escape codes or native handles.</summary>
public sealed class TextCanvasRenderer(TextWriter output, CanvasSize viewport) : ICanvasRenderer
{
    public CanvasSize GetViewportSize() => viewport;
    public void Render(Canvas canvas, int x = 0, int y = 0)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (x < 0 || y < 0 || (long)x + canvas.Width > viewport.Width || (long)y + canvas.Height > viewport.Height) throw new ArgumentException("Canvas outside text viewport.");
        for (int row = 0; row < y; row++) output.WriteLine();
        for (int row = 0; row < canvas.Height; row++)
        {
            output.Write(new string(' ', x));
            for (int col = 0; col < canvas.Width; col++) { char c = canvas[col, row].Character; output.Write(c == '\0' ? ' ' : c); }
            output.WriteLine();
        }
    }
}