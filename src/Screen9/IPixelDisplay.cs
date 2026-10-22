namespace Screen9;

/// <summary>Where a running program's PC screen goes and where its keys come from: <see cref="PixelWindow"/>
/// on the Windows desktop, a browser canvas on the web. Present/Refresh/ReadKey are called from the program's
/// own thread; once <see cref="Closed"/> (the user closed it), they throw <see cref="OperationCanceledException"/>
/// so the program stops, as closing the DOS screen would.</summary>
public interface IPixelDisplay : IDisposable
{
    bool Closed { get; }
    void Present(PCCanvas canvas);
    void Present(Canvas cells);
    /// <summary>Flush a frame that was presented but not shown yet (throttled), without a new Present.</summary>
    void Refresh();
    /// <summary>INKEY$: "" when no key is waiting.</summary>
    string ReadKey();
}
