using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBSharp.StandardLibrary.Serialization;

public sealed class JsonCodec
{
    public SerializationLimits Limits { get; }
    private readonly JsonSerializerOptions options;
    public JsonCodec(SerializationLimits? limits = null)
    {
        Limits = limits ?? new(); Limits.Validate();
        options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, MaxDepth = Limits.MaximumDepth, Converters = { new JsonStringEnumConverter() } };
    }
    public string Serialize(object? value, CancellationToken token = default)
    {
        var projected = ObjectGraph.Project(value, Limits, token); string text = JsonSerializer.Serialize(projected, options);
        CheckText(text); token.ThrowIfCancellationRequested(); return text;
    }
    public object? Deserialize(string text, CancellationToken token = default) => ObjectGraph.Dynamic(ReadPlain(text, token));
    public object? DeserializeClr(string text, Type type, CancellationToken token = default)
    { var plain = ReadPlain(text, token); var result = JsonSerializer.Deserialize(JsonSerializer.Serialize(plain, options), type, options); token.ThrowIfCancellationRequested(); return result; }
    internal object? ReadPlain(string text, CancellationToken token)
    {
        CheckText(text); token.ThrowIfCancellationRequested();
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = Limits.MaximumDepth }); int nodes = 0;
        object? Read(JsonElement element, int depth)
        {
            token.ThrowIfCancellationRequested();
            if (++nodes > Limits.MaximumNodes || depth > Limits.MaximumDepth) throw new InvalidOperationException("JSON graph limit exceeded.");
            switch (element.ValueKind)
            {
                case JsonValueKind.Null: return null;
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.String: return element.GetString();
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out var i)) return i;
                    if (element.TryGetInt64(out var l)) return l;
                    if (element.TryGetDecimal(out var d)) return d;
                    double number = element.GetDouble(); return double.IsFinite(number) ? number : throw new InvalidOperationException("Non-finite JSON number.");
                case JsonValueKind.Array: return element.EnumerateArray().Select(e => Read(e, depth + 1)).ToList();
                case JsonValueKind.Object:
                    var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var property in element.EnumerateObject()) if (!values.TryAdd(property.Name, Read(property.Value, depth + 1))) throw new InvalidOperationException($"Duplicate JSON key '{property.Name}'.");
                    return values;
                default: throw new InvalidOperationException("Unsupported JSON value.");
            }
        }
        return Read(document.RootElement, 0);
    }
    private void CheckText(string text)
    { ArgumentNullException.ThrowIfNull(text); if (text.Length > Limits.MaximumTextLength) throw new InvalidOperationException("JSON text limit exceeded."); }
}
