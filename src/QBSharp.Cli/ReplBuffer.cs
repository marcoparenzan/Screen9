using System.Text;
using QBSharp.Core.Hosting;
using QBSharp.Core.Syntax;

namespace QBSharp.Cli;

/// <summary>Block continuation detection only; the real parser decides validity.</summary>
public sealed class ReplBuffer
{
    private readonly StringBuilder text = new();
    public bool Pending => text.Length > 0;
    public string Text => text.ToString();
    public void Clear() => text.Clear();
    public bool Append(string line)
    {
        if (text.Length + line.Length + 1 > 65_536) { Clear(); throw new CliUsageException("Buffer REPL oltre 65536 caratteri."); }
        text.AppendLine(line);
        if (string.IsNullOrWhiteSpace(line)) return true;
        var scanned = new Lexer().Scan(new(Text, "<repl>"));
        if (scanned.Diagnostics.Count > 0) return true;
        int blocks = 0; bool atStart = true;
        foreach (var token in scanned.Tokens)
        {
            if (token.Kind == TokenKind.NewLine) { atStart = true; continue; }
            if (token.Kind == TokenKind.End) break;
            if (atStart)
            {
                if (token.Text.Equals("Public", StringComparison.OrdinalIgnoreCase)) continue;
                if ((token.Text.Equals("End", StringComparison.OrdinalIgnoreCase) || token.Text.Equals("Next",StringComparison.OrdinalIgnoreCase) || token.Text.Equals("Loop",StringComparison.OrdinalIgnoreCase))) blocks--;
                else if (new[] { "If", "While", "Function", "Class", "Namespace", "Sub", "For", "Do", "Select" }.Contains(token.Text, StringComparer.OrdinalIgnoreCase)) blocks++;
                atStart = false;
            }
            if (blocks < 0) return true;
        }
        return blocks == 0;
    }
}

/// <summary>End-of-line editing with history and token completion; not a full-screen editor.</summary>
public sealed class ReplLineBuffer
{
    public string Text { get; private set; } = "";
    private int historyIndex;
    public void Start(int count) { Text = ""; historyIndex = count; }
    public void Type(char character) { if (!char.IsControl(character) && Text.Length < 65_536) Text += character; }
    public void Backspace() { if (Text.Length > 0) Text = Text[..^1]; }
    public void History(int direction, IReadOnlyList<string> history)
    {
        historyIndex = Math.Clamp(historyIndex + direction, 0, history.Count);
        Text = historyIndex < history.Count ? history[historyIndex] : "";
    }
    public void Complete(IEnumerable<string> variables)
    {
        int start = Text.Length;
        while (start > 0 && (char.IsLetterOrDigit(Text[start - 1]) || Text[start - 1] is '_' or ':')) start--;
        var candidates = Completion.Repl(Text[start..], variables);
        if (candidates.Length > 0) Text = Text[..start] + candidates[0];
    }
}
