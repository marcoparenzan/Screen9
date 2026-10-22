using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QBSharp.Core.Syntax;

namespace QBSharp.Core.Hosting;

public static class ServiceCollectionExtensions
{
    /// <summary>Backward-compatible registration of bindings only.</summary>
    public static IServiceCollection AddQBSharpContracts(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<HostBindings>();
        return services;
    }
    /// <summary>Registers the stateless parser and reusable engine; sessions are explicitly created by hosts.</summary>
    public static IServiceCollection AddQBSharp(this IServiceCollection services)
    {
        services.AddQBSharpContracts();
        services.TryAddSingleton<IScriptParser, ScriptParser>();
        services.TryAddSingleton<IScriptEngine>(provider => new ScriptEngine(provider.GetRequiredService<IScriptParser>()));
        return services;
    }
}
