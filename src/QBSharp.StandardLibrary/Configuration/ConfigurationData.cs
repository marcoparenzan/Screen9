using QBSharp.Core.Hosting;
using QBSharp.StandardLibrary.Files;
using QBSharp.StandardLibrary.Serialization;

namespace QBSharp.StandardLibrary.Configuration;

public sealed class ConfigurationData(DynamicRecord data, string? source) : IScriptMemberProvider
{
    public DynamicRecord Data { get; } = data;
    public string? Source { get; } = source;
    public object? Get(string path, object? defaultValue = null)
    {
        object? current = Data;
        foreach (var part in path.Split('.'))
        {
            if (current is not DynamicRecord record || !record.Properties.TryGetValue(part, out current)) return defaultValue;
        }
        return current;
    }
    public object? GetMember(string name) => name.ToUpperInvariant() switch
    {
        "DATA" => Data, "SOURCE" => Source,
        "GET" => (HostFunction)((context, args) =>
        { context.CancellationToken.ThrowIfCancellationRequested(); if (args.Count is not (1 or 2) || args[0] is not string key) throw new ArgumentException("Config.Get requires a String path and optional default."); return ValueTask.FromResult(Get(key, args.Count == 2 ? args[1] : null)); }),
        _ => throw new KeyNotFoundException($"Unknown Config member '{name}'.")
    };
    public void SetMember(string name, object? value) => throw new InvalidOperationException("Config members are read-only; Config.Data is the in-memory mutable data snapshot.");
    public static async ValueTask<ConfigurationData> LoadAsync(FileServices files, YamlCodec yaml, CancellationToken token = default)
    {
        string first = Path.Combine(files.BaseDirectory, "qbs.yaml"), second = Path.Combine(files.BaseDirectory, "qbs.yml");
        bool a = files.FileExists(first), b = files.FileExists(second);
        if (a && b) throw new InvalidDataException("Use only one of qbs.yaml or qbs.yml.");
        string? path = a ? first : b ? second : null;
        if (path is null) return new(new DynamicRecord([]), null);
        try
        {
            object? value = yaml.Deserialize(await files.ReadTextAsync(path, token).ConfigureAwait(false), token);
            if (value is null) value = new DynamicRecord([]);
            if (value is not DynamicRecord data) throw new InvalidDataException("Configuration must be a YAML mapping.");
            return new(data, path);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is not IOException && e is not UnauthorizedAccessException)
        { throw new InvalidDataException($"Invalid YAML configuration '{path}': {e.Message}", e); }
    }
}
