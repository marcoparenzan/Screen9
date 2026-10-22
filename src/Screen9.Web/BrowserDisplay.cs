using System.Collections.Concurrent;
using Screen9.Web.Components;

namespace Screen9.Web;

/// <summary>The browser's <see cref="IPixelDisplay"/>: one running program's screen in a <see cref="PixelDisplay"/>
/// canvas, its keys from the browser. Used by EditorRunner/EditorApplication in place of the native
/// PixelWindow — which on a web host would open a window on the SERVER, behind the browser. Closing the
/// dialog (<see cref="Close"/>) makes the next call throw OperationCanceledException, ending the program as
/// closing the pixel window does.</summary>
public sealed class BrowserDisplay(PixelDisplay screen, Action disposed) : IPixelDisplay
{
    private readonly ConcurrentQueue<string> keys = new();
    private volatile bool closed;

    public bool Closed => closed;

    public void Close() => closed = true;

    public void Enqueue(string key)
    {
        if (key.Length > 0 && keys.Count < 1024) keys.Enqueue(key);
    }

    public void Present(PCCanvas canvas) { Check(); screen.PresentGraphics(canvas); }
    public void Present(Canvas cells) { Check(); screen.PresentText(cells); }
    public void Refresh() { Check(); screen.Flush(); }

    public string ReadKey()
    {
        Refresh();
        return keys.TryDequeue(out var key) ? key : "";
    }

    private void Check()
    {
        if (closed) throw new OperationCanceledException("Program window closed.");
    }

    public void Dispose() => disposed();
}
