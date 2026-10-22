using System.Collections;
using System.Globalization;
using QBSharp.StandardLibrary.Serialization;
using Spectre.Console;

namespace QBSharp.StandardLibrary.UI;

public sealed class SpectreTextUi(IAnsiConsole console, bool interactive = false) : ITextUi
{
    private void RequireInteractive()
    { if (!interactive) throw new InvalidOperationException("This dialog requires an interactive terminal; no redirected input is consumed."); }
    public ValueTask WindowAsync(string title, string text, CancellationToken token)
    { token.ThrowIfCancellationRequested(); console.Write(new Panel(new Text(text)).Header(Markup.Escape(title)).Border(BoxBorder.Double)); return ValueTask.CompletedTask; }
    public async ValueTask<string> MessageBoxAsync(string title, string text, CancellationToken token)
    {
        await WindowAsync(title, text, token);
        if (interactive) await new TextPrompt<string>("[cyan]Enter per continuare[/]") { AllowEmpty = true }.ShowAsync(console, token);
        return "OK";
    }
    public async ValueTask<string> InputBoxAsync(string prompt, string defaultValue, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); RequireInteractive();
        string answer = await new TextPrompt<string>(Markup.Escape(prompt)) { AllowEmpty = true }.ShowAsync(console, token);
        return string.IsNullOrEmpty(answer) ? defaultValue : answer;
    }
    public async ValueTask<string> SelectAsync(string title, IReadOnlyList<string> items, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); RequireInteractive(); ValidateItems(items);
        return await new SelectionPrompt<string>().Title(Markup.Escape(title)).UseConverter(Markup.Escape).AddChoices(items).ShowAsync(console, token);
    }
    public async ValueTask<IReadOnlyList<string>> MultiSelectAsync(string title, IReadOnlyList<string> items, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); RequireInteractive(); ValidateItems(items);
        return await new MultiSelectionPrompt<string>().Title(Markup.Escape(title)).UseConverter(Markup.Escape).NotRequired().AddChoices(items).ShowAsync(console, token);
    }
    private static void ValidateItems(IReadOnlyList<string> items)
    { if (items.Count is < 1 or > 1000 || items.Distinct(StringComparer.Ordinal).Count() != items.Count) throw new ArgumentException("Selection requires 1..1000 unique items."); }
    public ValueTask TableAsync(object? rows, CancellationToken token)
    {
        var graph = ObjectGraph.Project(rows, token: token);
        if (graph is not IEnumerable sequence || graph is string) throw new ArgumentException("Table requires a list of mappings or scalar values.");
        var values = sequence.Cast<object?>().Take(1001).ToList();
        if (values.Count > 1000) throw new ArgumentException("Table row limit exceeded.");
        if (values.Count == 0) { console.Write(new Text("No rows.\n")); return ValueTask.CompletedTask; }
        bool maps = values.All(v => v is IDictionary);
        if (values.Any(v => v is IDictionary) && !maps) throw new ArgumentException("Mixed table row shapes are unsupported.");
        string[] columns = maps ? values.Cast<IDictionary>().SelectMany(d => d.Keys.Cast<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() : ["Value"];
        if (columns.Length is < 1 or > 100) throw new ArgumentException("Table column limit exceeded.");
        var table = new Table(); foreach (var column in columns) table.AddColumn(new TableColumn(new Text(column)));
        foreach (var row in values)
        {
            token.ThrowIfCancellationRequested();
            table.AddRow(columns.Select(c => new Text(Convert.ToString(maps ? ((IDictionary)row!)[c] : row, CultureInfo.InvariantCulture) ?? "")).ToArray());
        }
        console.Write(table); return ValueTask.CompletedTask;
    }
    public ValueTask ProgressAsync(double percent, string description, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!double.IsFinite(percent) || percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        if (interactive)
            console.Progress().Start(context => { var task = context.AddTask(Markup.Escape(description)); task.Value = percent; });
        else console.Write(new Text($"{description}: {percent.ToString(CultureInfo.InvariantCulture)}%\n"));
        return ValueTask.CompletedTask;
    }
}
