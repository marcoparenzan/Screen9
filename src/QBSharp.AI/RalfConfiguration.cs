using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace QBSharp.AI;

/// <summary>Explicit external credential source; never included in script Config, prompts or logs.</summary>
public static class RalfConfiguration
{
    public const int MaximumFileBytes = 1_048_576;
    public static RalfSettings Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        // An explicit empty provider disables AI, including reading the external file.
        string? explicitProvider = configuration["QBS_AI_PROVIDER"];
        if (explicitProvider is not null && explicitProvider.Trim().Length == 0) return new();
        var file = configuration["QBS_AI_CONFIG_FILE"];
        IConfiguration external = string.IsNullOrWhiteSpace(file) ? new ConfigurationBuilder().Build() : ReadExternal(file);
        string provider = (explicitProvider ?? external["AIProvider"] ?? (string.IsNullOrWhiteSpace(file) ? "" : "foundry")).Trim().ToLowerInvariant();
        string group = provider switch { "openai" => "OpenAI", "anthropic" => "Anthropic", _ => "AzureAIFoundry" };
        string? model = external[group + ":ModelId"];
        if (provider == "foundry")
        {
            string context = configuration["QBS_AI_CONTEXT_KEY"] ?? "qbsharp";
            string? selection = external["AzureAIFoundry:ContextModels:" + context] ?? external["AzureAIFoundry:DefaultModel"];
            if (selection is not null) model = external["AzureAIFoundry:Models:" + selection + ":ModelId"] ?? selection;
        }
        int Number(string key, int fallback) => configuration[key] is not { } value ? fallback
            : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : throw new RalfException($"Invalid {key}.");
        return new()
        {
            Provider = provider, Model = configuration["QBS_AI_MODEL"] ?? model ?? "",
            ApiKey = configuration["QBS_AI_API_KEY"] ?? external[group + ":ApiKey"] ?? "",
            Endpoint = configuration["QBS_AI_ENDPOINT"] ?? (provider == "foundry" ? external[group + ":Endpoint"] : null),
            FoundryApi = (configuration["QBS_AI_FOUNDRY_API"] ?? external["AzureAIFoundry:Api"] ?? "chat").Trim().ToLowerInvariant(),
            Timeout = TimeSpan.FromSeconds(Number("QBS_AI_TIMEOUT_SECONDS", 60)), MaximumOutputTokens = Number("QBS_AI_MAX_OUTPUT_TOKENS", 2048)
        };
    }
    private static IConfiguration ReadExternal(string file)
    {
        try
        {
            if (!Path.IsPathFullyQualified(file)) throw new InvalidDataException();
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumFileBytes) throw new InvalidDataException();
            using var bytes = new MemoryStream(); var buffer = new byte[8192]; int count;
            while ((count = stream.Read(buffer)) != 0)
            { if (bytes.Length + count > MaximumFileBytes) throw new InvalidDataException(); bytes.Write(buffer, 0, count); }
            var data = bytes.ToArray(); int start = data.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
            string text = new UTF8Encoding(false, true).GetString(data, start, data.Length - start);
            using var document = JsonDocument.Parse(text, new() { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            void Flatten(JsonElement node, string prefix)
            {
                if (node.ValueKind == JsonValueKind.Object)
                {
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var property in node.EnumerateObject())
                    { if (!names.Add(property.Name)) throw new InvalidDataException(); Flatten(property.Value, prefix.Length == 0 ? property.Name : prefix + ":" + property.Name); }
                }
                else if (node.ValueKind == JsonValueKind.String) values.Add(prefix, node.GetString());
                else if (node.ValueKind != JsonValueKind.Null && (prefix.Equals("AIProvider", StringComparison.OrdinalIgnoreCase) || new[] { ":ApiKey", ":ModelId", ":Endpoint", ":Api", ":DefaultModel" }.Any(suffix => prefix.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))) throw new InvalidDataException();
                // Non-string optional metadata (prices, token limits, flags) is unrelated to credentials and ignored.
            }
            foreach (var property in document.RootElement.EnumerateObject())
            {
                // Ignore unrelated Ralf CLI configuration, particularly tools and project paths.
                if (property.Name.Equals("AIProvider", StringComparison.OrdinalIgnoreCase) || property.Name.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("Anthropic", StringComparison.OrdinalIgnoreCase) || property.Name.Equals("AzureAIFoundry", StringComparison.OrdinalIgnoreCase)) Flatten(property.Value, property.Name);
            }
            return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        }
        catch (Exception)
        { throw new RalfException("Cannot load QBS_AI_CONFIG_FILE. Use an absolute path to a valid RalfAI JSON configuration (max 1 MiB). File contents and path are not logged."); }
    }
}
