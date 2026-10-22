using System.Reflection;
using QBSharp.Core.Hosting;

namespace QBSharp.Core.Modules;

public sealed record ModuleRequest(string Name, string? ImportingFile);
public sealed record ResolvedModule(string CanonicalPath, ScriptSource Source);

/// <summary>Future .bas resolver: importer directory, user libraries, then stdlib.</summary>
public interface IModuleResolver
{
    ValueTask<ResolvedModule> ResolveAsync(ModuleRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Optional, opt-in phase 9 provider. Core never downloads packages by itself.</summary>
public interface IAssemblyProvider
{
    ValueTask<Assembly> LoadAsync(string reference, CancellationToken cancellationToken = default);
}
