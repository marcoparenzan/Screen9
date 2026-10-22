using Microsoft.Extensions.DependencyInjection;

namespace Screen9;

public static class ServiceCollectionExtensions
{
    /// <summary>Registra il renderer Windows. Il canvas rimane di proprietà dell'applicazione.</summary>
    public static IServiceCollection AddWindowsConsoleCanvas(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("È richiesta Windows.");
        services.AddSingleton<ICanvasRenderer, WindowsConsoleRenderer>();
        return services;
    }
}
