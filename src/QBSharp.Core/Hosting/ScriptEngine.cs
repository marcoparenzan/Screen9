using QBSharp.Core.Modules;
using QBSharp.Core.Runtime;
using QBSharp.Core.Syntax;

namespace QBSharp.Core.Hosting;

public sealed record ScriptEngineOptions
{
    public long MaximumSteps { get; init; } = 1_000_000;
    public int MaximumCallDepth { get; init; } = 128;
    public int MaximumImportDepth { get; init; } = 64;
    public string BaseDirectory { get; init; } = Directory.GetCurrentDirectory();
    public string? UserLibraryDirectory { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".qbs", "lib");
    public string? StandardLibraryDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "stdlib");
    /// <summary>Host opt-in type aliases, including open generic definitions. No package loading.</summary>
    public IReadOnlyDictionary<string, Type> ClrTypes { get; init; } = new Dictionary<string, Type>();
}

public sealed class ScriptEngine : IScriptEngine
{
    private readonly IScriptParser parser;
    private readonly ScriptEngineOptions options;
    private readonly IModuleResolver resolver;
    private readonly MemberAccess members = new();
    public ScriptEngine(IScriptParser? parser = null, ScriptEngineOptions? options = null, IModuleResolver? moduleResolver = null)
    {
        this.parser = new GuardedParser(parser ?? new ScriptParser());
        this.options = options ?? new();
        ArgumentOutOfRangeException.ThrowIfLessThan(this.options.MaximumSteps, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(this.options.MaximumCallDepth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(this.options.MaximumCallDepth, 128);
        ArgumentOutOfRangeException.ThrowIfLessThan(this.options.MaximumImportDepth, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(this.options.MaximumImportDepth, 64);
        resolver = moduleResolver ?? new FileModuleResolver(this.options.BaseDirectory, this.options.UserLibraryDirectory, this.options.StandardLibraryDirectory);
    }
    public IScriptSession CreateSession(HostBindings? bindings = null) => new ScriptSession(parser, options, members, bindings, resolver);
}
