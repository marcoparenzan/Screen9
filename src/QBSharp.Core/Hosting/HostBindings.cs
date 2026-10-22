using System.Collections.ObjectModel;

namespace QBSharp.Core.Hosting;

public delegate ValueTask<object?> HostFunction(HostCallContext context, IReadOnlyList<object?> arguments);
public sealed record HostCallContext(CancellationToken CancellationToken)
{
    public Func<string, object?, ValueTask<object?>>? Materialize { get; init; }
}

/// <summary>Case-insensitive shared namespace for host values and functions. Not thread-safe.</summary>
public sealed class HostBindings
{
    private readonly Dictionary<string, object?> values = new(StringComparer.OrdinalIgnoreCase);

    public void SetValue(string name, object? value)
    {
        ValidateName(name);
        values[name] = value;
    }

    public void RegisterFunction(string name, HostFunction function)
    {
        ArgumentNullException.ThrowIfNull(function);
        SetValue(name, function);
    }

    public bool TryGetValue(string name, out object? value)
    {
        ValidateName(name);
        return values.TryGetValue(name, out value);
    }

    /// <summary>Read-only shallow snapshot; host objects themselves retain identity.</summary>
    public IReadOnlyDictionary<string, object?> Snapshot() =>
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase));

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!(char.IsLetter(name[0]) || name[0] == '_') ||
            name.Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
            throw new ArgumentException("Expected a QBS identifier (letters, digits, underscore).", nameof(name));
    }
}
