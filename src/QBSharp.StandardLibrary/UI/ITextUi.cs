namespace QBSharp.StandardLibrary.UI;

public interface ITextUi
{
    ValueTask WindowAsync(string title, string text, CancellationToken token);
    ValueTask<string> MessageBoxAsync(string title, string text, CancellationToken token);
    ValueTask<string> InputBoxAsync(string prompt, string defaultValue, CancellationToken token);
    ValueTask<string> SelectAsync(string title, IReadOnlyList<string> items, CancellationToken token);
    ValueTask<IReadOnlyList<string>> MultiSelectAsync(string title, IReadOnlyList<string> items, CancellationToken token);
    ValueTask TableAsync(object? rows, CancellationToken token);
    ValueTask ProgressAsync(double percent, string description, CancellationToken token);
}

/// <summary>Embedding defaults to no UI; the host opts into a terminal or supplies a deterministic adapter.</summary>
public sealed class UnconfiguredTextUi : ITextUi
{
    private static Exception Error() => new InvalidOperationException("No UI adapter configured.");
    public ValueTask WindowAsync(string title, string text, CancellationToken token) => throw Error();
    public ValueTask<string> MessageBoxAsync(string title, string text, CancellationToken token) => throw Error();
    public ValueTask<string> InputBoxAsync(string prompt, string defaultValue, CancellationToken token) => throw Error();
    public ValueTask<string> SelectAsync(string title, IReadOnlyList<string> items, CancellationToken token) => throw Error();
    public ValueTask<IReadOnlyList<string>> MultiSelectAsync(string title, IReadOnlyList<string> items, CancellationToken token) => throw Error();
    public ValueTask TableAsync(object? rows, CancellationToken token) => throw Error();
    public ValueTask ProgressAsync(double percent, string description, CancellationToken token) => throw Error();
}
