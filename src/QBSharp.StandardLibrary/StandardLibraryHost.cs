using System.Collections;
using Screen9;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QBSharp.Core.Hosting;
using QBSharp.StandardLibrary.Configuration;
using QBSharp.StandardLibrary.Files;
using QBSharp.StandardLibrary.Serialization;
using QBSharp.StandardLibrary.UI;

namespace QBSharp.StandardLibrary;

public sealed record StandardLibraryOptions
{
    public FileSystemOptions Files { get; init; } = new();
    public SerializationLimits Serialization { get; init; } = new();
    public bool LoadConfiguration { get; init; } = true;
}
public sealed class StandardNamespace : IScriptMemberProvider
{
    private readonly Dictionary<string, HostFunction> functions = new(StringComparer.OrdinalIgnoreCase);
    internal void Add(string name, HostFunction function) => functions.Add(name, async (context, args) =>
    { context.CancellationToken.ThrowIfCancellationRequested(); var value = await function(context, args).ConfigureAwait(false); context.CancellationToken.ThrowIfCancellationRequested(); return value; });
    public object? GetMember(string name) => name.Equals("Names", StringComparison.OrdinalIgnoreCase) ? functions.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList()
        : functions.TryGetValue(name, out var function) ? function : throw new KeyNotFoundException($"Unknown library function '{name}'.");
    public void SetMember(string name, object? value) => throw new InvalidOperationException("Standard library members are read-only.");
}

/// <summary>Explicit embedding opt-in; the CLI registers these namespaces for each new session.</summary>
public sealed class StandardLibraryHost
{
    private readonly StandardLibraryOptions options;
    private readonly ITextUi ui;
    private readonly ICanvasRenderer? renderer;
    public FileServices Files { get; }
    public JsonCodec Json { get; }
    public YamlCodec Yaml { get; }
    public StandardLibraryHost(StandardLibraryOptions? options = null, ITextUi? ui = null, ICanvasRenderer? renderer = null)
    {
        this.renderer = renderer; this.options = options ?? new(); this.ui = ui ?? new UnconfiguredTextUi();
        Files = new(this.options.Files); Json = new(this.options.Serialization); Yaml = new(this.options.Serialization);
    }
    public async ValueTask<HostBindings> CreateBindingsAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); var bindings = new HostBindings();
        bindings.SetValue("Json", SerializationGroup(false)); bindings.SetValue("Yaml", SerializationGroup(true));
        bindings.SetValue("File", FileGroup()); bindings.SetValue("Directory", DirectoryGroup()); bindings.SetValue("Path", PathGroup()); bindings.SetValue("Ui", UiGroup());
        var config = options.LoadConfiguration ? await ConfigurationData.LoadAsync(Files, Yaml, token).ConfigureAwait(false) : new ConfigurationData(new DynamicRecord([]), null);
        bindings.SetValue("Screen", CanvasBindings.Create(renderer)); bindings.SetValue("PC", PCCanvasBindings.Create(renderer)); bindings.SetValue("Config", config); return bindings;
    }
    private static void Count(IReadOnlyList<object?> args, int min, int max)
    { if (args.Count < min || args.Count > max) throw new ArgumentException($"Expected {min}..{max} arguments."); }
    private static string Text(IReadOnlyList<object?> args, int i) => args[i] as string ?? throw new ArgumentException("Expected String argument.");
    private static bool Bool(IReadOnlyList<object?> args, int i) => args[i] is bool b ? b : throw new ArgumentException("Expected Boolean argument.");
    private static ValueTask<object?> Value(object? value) => ValueTask.FromResult(value);
    private StandardNamespace SerializationGroup(bool yaml)
    {
        var group = new StandardNamespace();
        group.Add("Serialize", (context, args) => { Count(args, 1, 1); return Value(yaml ? Yaml.Serialize(args[0], context.CancellationToken) : Json.Serialize(args[0], context.CancellationToken)); });
        group.Add("Deserialize", async (context, args) =>
        {
            Count(args, 1, 2); object? data = yaml ? Yaml.Deserialize(Text(args, 0), context.CancellationToken) : Json.Deserialize(Text(args, 0), context.CancellationToken);
            return args.Count == 1 ? data : await Materialize(context, Text(args, 1), data).ConfigureAwait(false);
        });
        return group;
    }
    private static ValueTask<object?> Materialize(HostCallContext context, string type, object? data) =>
        context.Materialize is { } mapper ? mapper(type, data) : throw new InvalidOperationException("Typed deserialization requires an interpreter materialization context; use DeserializeClr from a .NET host.");
    private StandardNamespace FileGroup()
    {
        var group = new StandardNamespace();
        group.Add("Exists", (_, args) => { Count(args, 1, 1); return Value(Files.FileExists(Text(args, 0))); });
        group.Add("ReadText", async (context, args) => { Count(args, 1, 1); return await Files.ReadTextAsync(Text(args, 0), context.CancellationToken).ConfigureAwait(false); });
        group.Add("WriteText", async (context, args) => { Count(args, 2, 2); await Files.WriteTextAsync(Text(args, 0), Text(args, 1), context.CancellationToken).ConfigureAwait(false); return null; });
        group.Add("Delete", (_, args) => { Count(args, 1, 1); Files.DeleteFile(Text(args, 0)); return Value(null); });
        group.Add("Copy", async (context, args) => { Count(args, 2, 2); await Files.CopyAsync(Text(args, 0), Text(args, 1), context.CancellationToken).ConfigureAwait(false); return null; });
        foreach (bool yaml in new[] { false, true })
        {
            string suffix = yaml ? "Yaml" : "Json";
            group.Add("Read" + suffix, async (context, args) =>
            {
                Count(args, 1, 2); string text = await Files.ReadTextAsync(Text(args, 0), context.CancellationToken).ConfigureAwait(false);
                var data = yaml ? Yaml.Deserialize(text, context.CancellationToken) : Json.Deserialize(text, context.CancellationToken);
                return args.Count == 1 ? data : await Materialize(context, Text(args, 1), data).ConfigureAwait(false);
            });
            group.Add("Write" + suffix, async (context, args) =>
            {
                Count(args, 2, 2); string text = yaml ? Yaml.Serialize(args[1], context.CancellationToken) : Json.Serialize(args[1], context.CancellationToken);
                await Files.WriteTextAsync(Text(args, 0), text, context.CancellationToken).ConfigureAwait(false); return null;
            });
        }
        return group;
    }
    private StandardNamespace DirectoryGroup()
    {
        var group = new StandardNamespace();
        group.Add("Exists", (_, args) => { Count(args, 1, 1); return Value(Files.DirectoryExists(Text(args, 0))); });
        group.Add("Create", (_, args) => { Count(args, 1, 1); Files.CreateDirectory(Text(args, 0)); return Value(null); });
        group.Add("Delete", (_, args) => { Count(args, 1, 2); Files.DeleteDirectory(Text(args, 0), args.Count == 2 && Bool(args, 1)); return Value(null); });
        group.Add("Files", (context, args) => { Count(args, 0, 1); return Value(Files.Files(args.Count == 1 ? Text(args, 0) : Files.BaseDirectory, context.CancellationToken)); });
        group.Add("Directories", (context, args) => { Count(args, 0, 1); return Value(Files.Directories(args.Count == 1 ? Text(args, 0) : Files.BaseDirectory, context.CancellationToken)); });
        group.Add("Glob", (context, args) => { Count(args, 1, 2); return Value(Files.Glob(Text(args, 0), args.Count == 2 ? Text(args, 1) : null, context.CancellationToken)); });
        return group;
    }
    private StandardNamespace PathGroup()
    {
        var group = new StandardNamespace();
        group.Add("Combine", (_, args) => Value(System.IO.Path.Combine(args.Select((_, i) => Text(args, i)).ToArray())));
        group.Add("FullPath", (_, args) => { Count(args, 1, 1); return Value(Files.Resolve(Text(args, 0))); });
        group.Add("GetFileName", (_, args) => { Count(args, 1, 1); return Value(System.IO.Path.GetFileName(Text(args, 0))); });
        group.Add("GetDirectoryName", (_, args) => { Count(args, 1, 1); return Value(System.IO.Path.GetDirectoryName(Text(args, 0))); });
        group.Add("GetExtension", (_, args) => { Count(args, 1, 1); return Value(System.IO.Path.GetExtension(Text(args, 0))); });
        return group;
    }
    private static IReadOnlyList<string> Items(object? value, CancellationToken token)
    {
        if (value is not IEnumerable sequence || value is string) throw new ArgumentException("Expected a sequence of String choices.");
        var items = new List<string>();
        foreach (var item in sequence)
        { token.ThrowIfCancellationRequested(); if (item is not string text || items.Count >= 1000) throw new ArgumentException("Expected at most 1000 String choices."); items.Add(text); }
        if (items.Count == 0 || items.Distinct(StringComparer.Ordinal).Count() != items.Count) throw new ArgumentException("Expected nonempty unique choices."); return items;
    }
    private StandardNamespace UiGroup()
    {
        var group = new StandardNamespace();
        group.Add("Window", async (context, args) => { Count(args, 2, 2); await ui.WindowAsync(Text(args, 0), Text(args, 1), context.CancellationToken); return null; });
        group.Add("MessageBox", async (context, args) => { Count(args, 1, 2); return await ui.MessageBoxAsync(args.Count == 2 ? Text(args, 1) : "QBSharp", Text(args, 0), context.CancellationToken); });
        group.Add("InputBox", async (context, args) => { Count(args, 1, 2); return await ui.InputBoxAsync(Text(args, 0), args.Count == 2 ? Text(args, 1) : "", context.CancellationToken); });
        foreach (var name in new[] { "Menu", "Select" }) group.Add(name, async (context, args) => { Count(args, 2, 2); return await ui.SelectAsync(Text(args, 0), Items(args[1], context.CancellationToken), context.CancellationToken); });
        group.Add("MultiSelect", async (context, args) => { Count(args, 2, 2); return await ui.MultiSelectAsync(Text(args, 0), Items(args[1], context.CancellationToken), context.CancellationToken); });
        group.Add("Table", async (context, args) => { Count(args, 1, 1); await ui.TableAsync(args[0], context.CancellationToken); return null; });
        group.Add("Progress", async (context, args) =>
        {
            Count(args, 1, 2); if (args[0] is not (int or long or double or decimal)) throw new ArgumentException("Expected numeric progress percent.");
            double percent = Convert.ToDouble(args[0], CultureInfo.InvariantCulture); if (!double.IsFinite(percent) || percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
            await ui.ProgressAsync(percent, args.Count == 2 ? Text(args, 1) : "Progress", context.CancellationToken); return null;
        });
        return group;
    }
}

public static class StandardLibraryServiceCollectionExtensions
{
    public static IServiceCollection AddQBSharpStandardLibrary(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services); services.TryAddSingleton<StandardLibraryOptions>(); services.TryAddSingleton<ITextUi, UnconfiguredTextUi>();
        services.TryAddTransient<StandardLibraryHost>(p => new(p.GetRequiredService<StandardLibraryOptions>(), p.GetRequiredService<ITextUi>(), p.GetService<ICanvasRenderer>())); return services;
    }
}
