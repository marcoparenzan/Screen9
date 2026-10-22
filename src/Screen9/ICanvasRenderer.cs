namespace Screen9;

public readonly record struct CanvasSize(int Width, int Height);

public interface ICanvasRenderer
{
    CanvasSize GetViewportSize();

    /// <summary>Scrive il canvas nella finestra visibile; non modifica cursore, colori globali o dimensioni.
    /// Il canvas deve rientrare interamente nella finestra.</summary>
    void Render(Canvas canvas, int x = 0, int y = 0);

    /// <summary>Dimentica l'ultimo frame presentato: il prossimo Render riscrive tutto il canvas.
    /// Da chiamare dopo scritture sulla Console esterne al renderer. Default: nessuno stato da dimenticare.</summary>
    void Invalidate() { }
}
