using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using QBSharp.Core.Hosting;
using QBSharp.Core.Syntax;

namespace QBSharp.Core.Runtime;

internal sealed partial class ScriptSession
{
    private static readonly JsonSerializerOptions MaterializationOptions = new()
    { PropertyNameCaseInsensitive = true, MaxDepth = 64, Converters = { new JsonStringEnumConverter() } };
    private async ValueTask<object?> Materialize(string typeName, object? data, Scope scope, SourceSpan span)
    {
        var type = ResolveDeclared(typeName, scope) ?? throw new InvalidOperationException("Expected type name.");
        return await MaterializeValue(type, data, span, 0).ConfigureAwait(false);
    }
    private async ValueTask<object?> MaterializeValue(object? type, object? data, SourceSpan span, int depth)
    {
        Step(span);
        if (depth > 64) throw new InvalidOperationException("Materialization depth exceeded.");
        if (type is ScriptClass scriptClass)
        {
            if (data is null) return null;
            var properties = data is IScriptPropertyBag bag ? bag.Properties : data as IReadOnlyDictionary<string, object?>;
            if (properties is null) throw new InvalidOperationException("Script class requires an object mapping.");
            // Constructors/initializers are deliberate: materialization is not a constructor bypass.
            var instance = await Construct(scriptClass, [], span).ConfigureAwait(false);
            foreach (var (name, value) in properties)
            {
                var declaration = scriptClass.Declaration.Members.OfType<PropertyDeclaration>().SingleOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (declaration is null) throw new InvalidOperationException($"Unknown property '{name}'.");
                var cell = instance.Scope.Cells[declaration.Name];
                Set(cell, await MaterializeValue(cell.Type, value, span, depth + 1).ConfigureAwait(false));
            }
            return instance;
        }
        if (type is null || type is Type objectType && objectType == typeof(object)) return data;
        if (data is null) return ConvertValue(null, type);
        var clr = (Type)type;
        if (clr.IsInstanceOfType(data)) return data;
        if (Values.Numeric(data) && (clr == typeof(int) || clr == typeof(long) || clr == typeof(double) || clr == typeof(decimal))) return Values.ConvertTo(data, clr);
        string json = JsonSerializer.Serialize(Normalize(data, depth));
        var result = JsonSerializer.Deserialize(json, clr, MaterializationOptions);
        cancellation.ThrowIfCancellationRequested(); return result;
    }
    private object? Normalize(object? value, int depth)
    {
        cancellation.ThrowIfCancellationRequested();
        if (depth > 64) throw new InvalidOperationException("Materialization depth exceeded.");
        if (value is IScriptPropertyBag bag) return bag.Properties.ToDictionary(p => p.Key, p => Normalize(p.Value, depth + 1));
        if (value is IDictionary dictionary)
        {
            var result = new Dictionary<string, object?>();
            foreach (DictionaryEntry entry in dictionary)
            { if (entry.Key is not string key) throw new InvalidOperationException("Mapping keys must be strings."); result.Add(key, Normalize(entry.Value, depth + 1)); }
            return result;
        }
        if (value is IEnumerable sequence && value is not string) return sequence.Cast<object?>().Select(v => Normalize(v, depth + 1)).ToList();
        return value;
    }
}
