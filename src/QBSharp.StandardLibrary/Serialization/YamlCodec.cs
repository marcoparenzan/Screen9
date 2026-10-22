using System.Collections;
using System.Globalization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace QBSharp.StandardLibrary.Serialization;

/// <summary>Data-only YAML: a single document, no tags, anchors or aliases.</summary>
public sealed class YamlCodec
{
    public SerializationLimits Limits { get; }
    public YamlCodec(SerializationLimits? limits = null) { Limits = limits ?? new(); Limits.Validate(); }
    public string Serialize(object? value, CancellationToken token = default)
    {
        var plain = ObjectGraph.Project(value, Limits, token);
        YamlNode Node(object? data)
        {
            token.ThrowIfCancellationRequested();
            if (data is IDictionary map)
            {
                var mapping = new YamlMappingNode();
                foreach (DictionaryEntry entry in map) mapping.Add(new YamlScalarNode((string)entry.Key) { Style = ScalarStyle.DoubleQuoted }, Node(entry.Value));
                return mapping;
            }
            if (data is IEnumerable sequence && data is not string) return new YamlSequenceNode(sequence.Cast<object?>().Select(Node));
            if (data is string s) return new YamlScalarNode(s) { Style = ScalarStyle.DoubleQuoted };
            return new YamlScalarNode(data is null ? "null" : data is bool b ? b ? "true" : "false" : Convert.ToString(data, CultureInfo.InvariantCulture)) { Style = ScalarStyle.Plain };
        }
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        new YamlStream(new YamlDocument(Node(plain))).Save(writer, false); string text = writer.ToString(); CheckText(text); return text;
    }
    public object? Deserialize(string text, CancellationToken token = default) => ObjectGraph.Dynamic(ReadPlain(text, token));
    public object? DeserializeClr(string text, Type type, CancellationToken token = default) => new JsonCodec(Limits).DeserializeClr(new JsonCodec(Limits).Serialize(ReadPlain(text, token), token), type, token);
    internal object? ReadPlain(string text, CancellationToken token)
    {
        CheckText(text); Preflight(text, token);
        var yaml = new YamlStream(); yaml.Load(new StringReader(text));
        if (yaml.Documents.Count == 0) return null;
        object? Read(YamlNode node)
        {
            token.ThrowIfCancellationRequested();
            if (node is YamlMappingNode mapping)
            {
                var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, child) in mapping.Children)
                {
                    if (key is not YamlScalarNode scalar || scalar.Value is null) throw new InvalidOperationException("YAML mapping keys must be strings.");
                    if (!result.TryAdd(scalar.Value, Read(child))) throw new InvalidOperationException($"Duplicate YAML key '{scalar.Value}'.");
                }
                return result;
            }
            if (node is YamlSequenceNode sequence) return sequence.Children.Select(Read).ToList();
            if (node is not YamlScalarNode value) throw new InvalidOperationException("Unsupported YAML node.");
            string textValue = value.Value ?? "";
            if (value.Style is not (ScalarStyle.Plain or ScalarStyle.Any)) return textValue;
            if (textValue is "" or "~" || textValue.Equals("null", StringComparison.OrdinalIgnoreCase)) return null;
            if (bool.TryParse(textValue, out var boolean)) return boolean;
            if (int.TryParse(textValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)) return integer;
            if (long.TryParse(textValue, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var longValue)) return longValue;
            if (decimal.TryParse(textValue, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var decimalValue)) return decimalValue;
            if (double.TryParse(textValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating)) return double.IsFinite(floating) ? floating : throw new InvalidOperationException("Non-finite YAML number.");
            if (textValue.Equals(".nan", StringComparison.OrdinalIgnoreCase) || textValue.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Non-finite YAML number.");
            return textValue; // YAML 1.2 subset: dates and yes/no stay text.
        }
        return Read(yaml.Documents[0].RootNode);
    }
    private void Preflight(string text, CancellationToken token)
    {
        int depth = 0, nodes = 0, documents = 0; var parser = new Parser(new StringReader(text));
        while (parser.MoveNext())
        {
            token.ThrowIfCancellationRequested(); var current = parser.Current;
            if (current is DocumentStart && ++documents > 1) throw new InvalidOperationException("One YAML document only.");
            if (current is AnchorAlias) throw new InvalidOperationException("YAML aliases are disabled.");
            if (current is NodeEvent node)
            {
                if (!node.Anchor.IsEmpty || !node.Tag.IsEmpty) throw new InvalidOperationException("YAML anchors and explicit tags are disabled.");
                if (++nodes > Limits.MaximumNodes) throw new InvalidOperationException("YAML node limit exceeded.");
            }
            if (current is MappingStart or SequenceStart && ++depth > Limits.MaximumDepth) throw new InvalidOperationException("YAML depth exceeded.");
            if (current is MappingEnd or SequenceEnd) depth--;
        }
    }
    private void CheckText(string text)
    { ArgumentNullException.ThrowIfNull(text); if (text.Length > Limits.MaximumTextLength) throw new InvalidOperationException("YAML text limit exceeded."); }
}
