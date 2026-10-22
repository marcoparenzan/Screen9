using System.Collections;
using System.Globalization;
using Screen9;
using QBSharp.StandardLibrary.Serialization;

namespace QBSharp.StandardLibrary.UI;

/// <summary>Execution UI adapter: Screen9 streams/dialogs, no Spectre dependency.</summary>
public sealed class ConsoleLibTextUi(TextConsole console) : ITextUi
{
    public ValueTask WindowAsync(string title, string text, CancellationToken token)
    { token.ThrowIfCancellationRequested(); console.Window(title, text); return ValueTask.CompletedTask; }
    public ValueTask<string> MessageBoxAsync(string title, string text, CancellationToken token) => console.MessageBoxAsync(title, text, token);
    public ValueTask<string> InputBoxAsync(string prompt, string defaultValue, CancellationToken token) { RequireInteractive(token); return console.InputBoxAsync(prompt, defaultValue, token); }
    public async ValueTask<string> SelectAsync(string title, IReadOnlyList<string> items, CancellationToken token) { RequireInteractive(token); return (await console.SelectAsync(title, items, token: token))[0]; }
    public ValueTask<IReadOnlyList<string>> MultiSelectAsync(string title, IReadOnlyList<string> items, CancellationToken token) { RequireInteractive(token); return console.SelectAsync(title, items, true, token); }
    private void RequireInteractive(CancellationToken token) { token.ThrowIfCancellationRequested(); if (!console.Interactive) throw new InvalidOperationException("This dialog requires an interactive terminal; no redirected input is consumed."); }
    public ValueTask TableAsync(object? rows, CancellationToken token)
    {
        var graph = ObjectGraph.Project(rows, token: token);
        if (graph is not IEnumerable sequence || graph is string || graph is IDictionary) throw new ArgumentException("Table requires a list of mappings or scalar values.");
        var values = sequence.Cast<object?>().Take(1001).ToList();
        if (values.Count > 1000) throw new ArgumentException("Table row limit exceeded.");
        if (values.Count == 0) { console.WriteLine("No rows."); return ValueTask.CompletedTask; }
        bool maps = values.All(v => v is IDictionary);
        if (values.Any(v => v is IDictionary) && !maps) throw new ArgumentException("Mixed table row shapes are unsupported.");
        string[] columns = maps ? values.Cast<IDictionary>().SelectMany(map => map.Keys.Cast<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : ["Value"];
        if (columns.Length is < 1 or > 100) throw new ArgumentException("Table column limit exceeded.");
        var tableRows = new List<IReadOnlyList<string>>();
        foreach (var row in values)
        {
            token.ThrowIfCancellationRequested();
            tableRows.Add(columns.Select(column => Convert.ToString(maps ? ((IDictionary)row!)[column] : row, CultureInfo.InvariantCulture) ?? "").ToArray());
        }
        console.Table(columns, tableRows); return ValueTask.CompletedTask;
    }
    public ValueTask ProgressAsync(double percent, string description, CancellationToken token)
    { token.ThrowIfCancellationRequested(); console.Progress(percent, description); return ValueTask.CompletedTask; }
}
