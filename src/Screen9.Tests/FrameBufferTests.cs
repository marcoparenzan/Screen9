using System.Diagnostics;

namespace Screen9.Tests;

/// <summary>Double buffering del renderer Windows senza Console reale: back buffer, diff con il frame presentato, invalidazione.</summary>
public sealed class FrameBufferTests
{
    private static readonly FrameTarget Origin = new(0, 0, 120, 30, 0, 0);
    private static readonly Cell Ink = new('#', ConsoleColor.Yellow, ConsoleColor.Blue);

    private static FrameRect? Present(ConsoleFrameBuffer frame, Canvas canvas, FrameTarget? target = null)
    {
        if (!frame.Prepare(canvas.Cells, canvas.Width, canvas.Height, target ?? Origin, out var dirty)) return null;
        frame.Commit(); return dirty;
    }

    [Fact]
    public void FirstFrameIsFullAndAnIdenticalFrameWritesNothing()
    {
        var frame = new ConsoleFrameBuffer(); var canvas = new Canvas(10, 5);
        Assert.Equal(new FrameRect(0, 0, 9, 4), Present(frame, canvas));
        Assert.Null(Present(frame, canvas));
        canvas.SetPixel(3, 2, Cell.Empty); Assert.Null(Present(frame, canvas)); // stessa cella: nessun cambiamento
    }

    [Fact]
    public void DirtyRectangleIsTheBoundingBoxOfChangedCells()
    {
        var frame = new ConsoleFrameBuffer(); var canvas = new Canvas(10, 5); Present(frame, canvas);
        canvas.SetPixel(4, 3, Ink); Assert.Equal(new FrameRect(4, 3, 4, 3), Present(frame, canvas));
        canvas.SetPixel(2, 1, Ink); canvas.SetPixel(7, 4, Ink); Assert.Equal(new FrameRect(2, 1, 7, 4), Present(frame, canvas));
        canvas.SetPixel(9, 0, new Cell('#', ConsoleColor.Red, ConsoleColor.Blue)); Assert.Equal(new FrameRect(9, 0, 9, 0), Present(frame, canvas)); // solo colore
        canvas.Clear(); Assert.Equal(new FrameRect(2, 0, 9, 4), Present(frame, canvas));
    }

    [Fact]
    public void BackBufferEncodesCharacterAndAttributes()
    {
        var frame = new ConsoleFrameBuffer(); var canvas = new Canvas(3, 1); canvas.SetPixel(1, 0, Ink);
        Assert.True(frame.Prepare(canvas.Cells, 3, 1, Origin, out _));
        Assert.Equal('#', frame.Back[1].UnicodeChar); Assert.Equal((ushort)((int)ConsoleColor.Yellow | (int)ConsoleColor.Blue << 4), frame.Back[1].Attributes);
        Assert.Equal(' ', frame.Back[0].UnicodeChar); Assert.Equal((ushort)ConsoleColor.Gray, frame.Back[0].Attributes);
        var blank = CharInfo.Encode(default); Assert.Equal(' ', blank.UnicodeChar); Assert.Equal(0, blank.Attributes);
    }

    [Fact]
    public void TargetSizeOrInvalidateForceAFullFrame()
    {
        var frame = new ConsoleFrameBuffer(); var canvas = new Canvas(10, 5); Present(frame, canvas);
        Assert.Equal(new FrameRect(0, 0, 9, 4), Present(frame, canvas, Origin with { WindowTop = 1 })); // scroll
        Assert.Equal(new FrameRect(0, 0, 9, 4), Present(frame, canvas, Origin with { WindowTop = 1, WindowWidth = 100 })); // resize
        Assert.Equal(new FrameRect(0, 0, 9, 4), Present(frame, canvas, Origin with { WindowTop = 1, WindowWidth = 100, X = 2 })); // spostamento
        Assert.Null(Present(frame, canvas, Origin with { WindowTop = 1, WindowWidth = 100, X = 2 }));
        frame.Invalidate(); Assert.Equal(new FrameRect(0, 0, 9, 4), Present(frame, canvas, Origin with { WindowTop = 1, WindowWidth = 100, X = 2 }));
        Assert.Equal(new FrameRect(0, 0, 3, 2), Present(frame, new Canvas(4, 3))); // nuova dimensione
    }

    [Fact]
    public void DiffIsAlwaysAgainstThePresentedFrameNotTheLastPrepared()
    {
        var frame = new ConsoleFrameBuffer(); var canvas = new Canvas(10, 5); Present(frame, canvas);
        canvas.SetPixel(1, 1, Ink); Assert.True(frame.Prepare(canvas.Cells, 10, 5, Origin, out _)); // scrittura fallita: niente Commit
        Assert.True(frame.Prepare(canvas.Cells, 10, 5, Origin, out var again)); Assert.Equal(new FrameRect(1, 1, 1, 1), again);
        frame.Commit(); Assert.Null(Present(frame, canvas));
    }

    [Fact]
    public void PCCanvasPageFlipAndPaletteChangesReachTheFrame()
    {
        // 320x200 proiettato su 160x100: la cella (3,3) campiona il pixel (6,6).
        var frame = new ConsoleFrameBuffer(); var pc = new PCCanvas(7); var screen = new Canvas(160, 100);
        FrameRect? Show() { pc.ToCellCanvas(screen); return Present(frame, screen); }
        Show(); var cell = new FrameRect(3, 3, 3, 3);
        pc.PSet(6, 6, 14); Assert.Equal(cell, Show());
        pc.SetPalette(14, 4); Assert.Equal(cell, Show()); // solo i pixel con quell'indice
        pc.ActivePage = 1; Assert.Null(Show()); // si disegna sulla pagina 1, si vede ancora la 0
        pc.VisualPage = 1; Assert.Equal(cell, Show()); // pagina 1 vuota
        Assert.Null(Show());
        pc.SetPalette(14, 63); Assert.Null(Show()); // nessun pixel con indice 14 nella pagina visibile
        pc.VisualPage = 0; Assert.Equal(cell, Show());
    }

    [Fact]
    public void ArgumentsMustMatchTheFrame()
        => Assert.Throws<ArgumentException>(() => new ConsoleFrameBuffer().Prepare(new Canvas(2, 2).Cells, 3, 2, Origin, out _));
}

[Collection(nameof(PerformanceCollection))] [Trait("Category", "Performance")]
public sealed class FrameBufferPerformanceTests
{
    [Fact]
    public void SteadyStateFramesDoNotAllocateAndFitInsideAFrameBudget()
    {
        var frame = new ConsoleFrameBuffer(); var canvas = new Canvas(300, 100); var target = new FrameTarget(0, 0, 300, 100, 0, 0); int tick = 0;
        void Step()
        {
            canvas.SetPixel(tick % 300, tick / 300 % 100, new Cell((char)('a' + tick % 26))); tick++;
            if (frame.Prepare(canvas.Cells, 300, 100, target, out _)) frame.Commit();
        }
        for (int i = 0; i < 1000; i++) Step(); // oltre la ricompilazione a livelli del JIT
        long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 100; i++) Step();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        double best = double.MaxValue;
        for (int i = 0; i < 5; i++) { var watch = Stopwatch.StartNew(); Step(); best = Math.Min(best, watch.Elapsed.TotalMilliseconds); }
        Assert.InRange(best, 0, TimeBudget.Milliseconds(2));
    }
}
