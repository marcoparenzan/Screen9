using System.Collections;
using System.Globalization;
using System.Reflection;
using QBSharp.Core.Hosting;

namespace QBSharp.StandardLibrary.Serialization;

public sealed record SerializationLimits
{
    public int MaximumDepth { get; init; } = 64;
    public int MaximumNodes { get; init; } = 10_000;
    public int MaximumTextLength { get; init; } = 1_048_576;
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumDepth, 1); ArgumentOutOfRangeException.ThrowIfGreaterThan(MaximumDepth, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaximumNodes, 1); ArgumentOutOfRangeException.ThrowIfLessThan(MaximumTextLength, 1);
    }
}

public static class ObjectGraph
{
    public static object? Project(object? value, SerializationLimits? limits = null, CancellationToken token = default)
    {
        limits ??= new(); limits.Validate(); int count = 0; var active = new HashSet<object>(ReferenceEqualityComparer.Instance);
        object? Walk(object? current, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (++count > limits.MaximumNodes || depth > limits.MaximumDepth) throw new InvalidOperationException("Object graph limit exceeded.");
            if (current is System.Text.Json.JsonElement element) return Walk(new JsonCodec(limits).Deserialize(element.GetRawText(), token), depth);
            if (current is null || current is string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or decimal) return current;
            if (current is double d) return double.IsFinite(d) ? d : throw new InvalidOperationException("Non-finite number.");
            if (current is float f) return float.IsFinite(f) ? f : throw new InvalidOperationException("Non-finite number.");
            if (current is DateTime date) return date.ToString("O", CultureInfo.InvariantCulture);
            if (current is DateTimeOffset offset) return offset.ToString("O", CultureInfo.InvariantCulture);
            if (current is Guid or TimeSpan or char or Enum) return Convert.ToString(current, CultureInfo.InvariantCulture);
            if (current is Type or Delegate or IScriptOpaqueValue || current is IScriptMemberProvider && current is not IScriptPropertyBag)
                throw new InvalidOperationException("Code, namespace and library values cannot be serialized.");
            if (!active.Add(current)) throw new InvalidOperationException("Cyclic object graph.");
            try
            {
                var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                void Add(string key, object? child)
                { if (!result.TryAdd(key, Walk(child, depth + 1))) throw new InvalidOperationException($"Duplicate property '{key}'."); }
                if (current is IScriptPropertyBag bag) { foreach (var (key, child) in bag.Properties) Add(key, child); return result; }
                if (current is IDictionary map)
                { foreach (DictionaryEntry entry in map) { if (entry.Key is not string key) throw new InvalidOperationException("Only String map keys supported."); Add(key, entry.Value); } return result; }
                if (current is IEnumerable sequence)
                { var items = new List<object?>(); foreach (var child in sequence) items.Add(Walk(child, depth + 1)); return items; }
                foreach (var property in current.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetMethod?.IsPublic == true && p.GetIndexParameters().Length == 0)) Add(property.Name, property.GetValue(current));
                return result;
            }
            finally { active.Remove(current); }
        }
        return Walk(value, 0);
    }
    public static object? Dynamic(object? projected) => projected switch
    {
        Dictionary<string, object?> map => new DynamicRecord(map.Select(p => new KeyValuePair<string, object?>(p.Key, Dynamic(p.Value)))),
        List<object?> sequence => sequence.Select(Dynamic).ToList(), _ => projected
    };
}
