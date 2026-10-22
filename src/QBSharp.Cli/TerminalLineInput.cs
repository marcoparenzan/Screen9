using Spectre.Console;

namespace QBSharp.Cli;

internal sealed class TerminalLineInput(IAnsiConsole console)
{
    private readonly List<string> history = [];
    public async Task<string?> ReadAsync(bool pending, IEnumerable<string> variables, CancellationToken token)
    {
        console.Write(new Text(pending ? "...> " : "qbs> ", new Style(Color.Cyan)));
        var buffer = new ReplLineBuffer(); buffer.Start(history.Count);
        while (true)
        {
            var key = await console.Input.ReadKeyAsync(true, token).ConfigureAwait(false);
            if (key is null) { await Task.Delay(20, token).ConfigureAwait(false); continue; }
            if (key.Value.Key == ConsoleKey.Enter)
            {
                console.WriteLine();
                if (!string.IsNullOrWhiteSpace(buffer.Text)) { history.Add(buffer.Text); if (history.Count > 200) history.RemoveAt(0); }
                return buffer.Text;
            }
            if (key.Value.Key == ConsoleKey.D && key.Value.Modifiers.HasFlag(ConsoleModifiers.Control) && buffer.Text.Length == 0)
            { console.WriteLine(); return null; }
            var before = buffer.Text;
            switch (key.Value.Key)
            {
                case ConsoleKey.Backspace: buffer.Backspace(); break;
                case ConsoleKey.UpArrow: buffer.History(-1, history); break;
                case ConsoleKey.DownArrow: buffer.History(1, history); break;
                case ConsoleKey.Tab: buffer.Complete(variables); break;
                default: buffer.Type(key.Value.KeyChar); break;
            }
            if (buffer.Text != before)
            {
                // Simple single-line renderer; wide-glyph/wrapping-aware editing belongs to phase 6.
                console.Write(new Text(string.Concat(Enumerable.Repeat("\b \b", before.Length))));
                console.Write(new Text(buffer.Text));
            }
        }
    }
}
