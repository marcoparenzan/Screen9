using System.Runtime.InteropServices.JavaScript;

namespace Screen9.Web.Client.Components;

/// <summary>JS side of the in-browser screen: bind a canvas, blit an RGBA buffer, wait for the next display frame.</summary>
internal static partial class PixelBlit
{
    private const string Module = "pixelBlit";
    private static Task? loading;

    /// <summary>Loads pixelBlit.js once; idempotent and safe to await from several screens.</summary>
    public static Task EnsureLoadedAsync() => loading ??= JSHost.ImportAsync(Module, "/pixelBlit.js");

    [JSImport("attach", Module)] public static partial void Attach(string id, int width, int height);
    [JSImport("draw", Module)] public static partial void Draw(string id, byte[] rgba);
    [JSImport("detach", Module)] public static partial void Detach(string id);
    [JSImport("save", Module)] public static partial void Save(string id);
    [JSImport("nextFrame", Module)] public static partial Task NextFrame();
}
