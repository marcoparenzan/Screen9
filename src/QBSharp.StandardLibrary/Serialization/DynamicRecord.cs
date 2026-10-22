using System.Collections.ObjectModel;
using QBSharp.Core.Hosting;

namespace QBSharp.StandardLibrary.Serialization;

/// <summary>Case-insensitive data mapping. Missing keys are errors; values remain CLR objects.</summary>
public sealed class DynamicRecord : IScriptMemberProvider, IScriptPropertyBag
{
    private readonly Dictionary<string, object?> values = new(StringComparer.OrdinalIgnoreCase);
    public DynamicRecord(IEnumerable<KeyValuePair<string, object?>> properties)
    { foreach (var (key, value) in properties) { if (!values.TryAdd(key, value)) throw new InvalidOperationException($"Duplicate data key '{key}'."); } }
    public IReadOnlyDictionary<string, object?> Properties => new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase));
    public object? GetValue(string name) => values.TryGetValue(name, out var value) ? value : throw new KeyNotFoundException($"Missing data key '{name}'.");
    public void SetValue(string name, object? value) => values[name] = value;
    public object? GetMember(string name)
    {
        if (values.TryGetValue(name, out var value)) return value;
        return name.ToUpperInvariant() switch
        {
            "COUNT" => values.Count, "KEYS" => values.Keys.ToList(),
            "GETVALUE" => (HostFunction)((_, args) => { Require(args, 1); return ValueTask.FromResult(GetValue((string)args[0]!)); }),
            "SETVALUE" => (HostFunction)((_, args) => { Require(args, 2); SetValue((string)args[0]!, args[1]); return ValueTask.FromResult<object?>(null); }),
            _ => throw new KeyNotFoundException($"Missing data key '{name}'.")
        };
    }
    public void SetMember(string name, object? value) => SetValue(name, value);
    private static void Require(IReadOnlyList<object?> args, int count)
    { if (args.Count != count || args[0] is not string) throw new ArgumentException("Expected key String and optional value."); }
    public override string ToString() => $"Data({values.Count})";
}
