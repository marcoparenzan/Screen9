using System.Diagnostics;

namespace Screen9.Tests;

/// <summary>Specifica eseguibile di un gap noto: saltata di default, eseguita con CONSOLELIB_GAPS=1.
/// Quando il gap è chiuso, sostituire [GapFact] con [Fact].</summary>
public sealed class GapFactAttribute : FactAttribute
{
    public GapFactAttribute(string gap)
    {
        if (Environment.GetEnvironmentVariable("CONSOLELIB_GAPS") != "1") Skip = $"Gap noto: {gap}. Eseguire con CONSOLELIB_GAPS=1.";
    }
}

[CollectionDefinition(nameof(PerformanceCollection), DisableParallelization = true)]
public sealed class PerformanceCollection;

/// <summary>Budget di tempo validi per la libreria compilata in Release; ×4 se il JIT optimizer è disattivato (Debug).
/// I budget di allocazione non dipendono dalla configurazione e restano esatti.</summary>
public static class TimeBudget
{
    public static readonly bool Optimized = typeof(Canvas).Assembly.GetCustomAttributes(typeof(DebuggableAttribute), false)
        .Cast<DebuggableAttribute>().All(attribute => !attribute.IsJITOptimizerDisabled);
    public static double Milliseconds(double release) => Optimized ? release : release * 4;
}

/// <summary>Budget di allocazione (deterministici) e di tempo (best-of-N, soglie larghe per CI).
/// Riferimento: un frame a 60 Hz è 16,7 ms.</summary>
[Collection(nameof(PerformanceCollection))] [Trait("Category", "Performance")]
public sealed class PerformanceTests
{
    // Riscaldamento oltre la ricompilazione a livelli del JIT, poi il minimo di tre misure:
    // un'allocazione reale si ripete a ogni chiamata, quella del JIT una volta sola.
    private static long Allocated(Action action)
    {
        for (int i = 0; i < 40; i++) action();
        long best = long.MaxValue;
        for (int i = 0; i < 3; i++) { long before = GC.GetAllocatedBytesForCurrentThread(); action(); best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before); }
        return best;
    }

    private static double BestMilliseconds(Action action, int runs = 5)
    {
        action(); double best = double.MaxValue;
        for (int i = 0; i < runs; i++) { var watch = Stopwatch.StartNew(); action(); best = Math.Min(best, watch.Elapsed.TotalMilliseconds); }
        return best;
    }

    // Regressioni: oggi verdi.

    [Fact]
    public void CanvasPrimitivesDoNotAllocate()
    {
        var canvas = new Canvas(320, 200); var ink = new Cell('#', ConsoleColor.Yellow);
        Assert.Equal(0, Allocated(() => { for (int y = 0; y < 200; y++) for (int x = 0; x < 320; x++) canvas.SetPixel(x, y, ink); }));
        Assert.Equal(0, Allocated(() => canvas.FillRectangle(-10, -10, 400, 300, ink)));
        Assert.Equal(0, Allocated(() => canvas.DrawLine(int.MinValue, 0, int.MaxValue, 199, ink)));
        Assert.Equal(0, Allocated(() => canvas.DrawEllipse(160, 100, 100, 80, ink, true)));
    }

    [Fact]
    public void CanvasFullFrameFitsWellInsideAFrameBudget()
        => Assert.InRange(BestMilliseconds(() => new Canvas(320, 200).FillRectangle(0, 0, 320, 200, new Cell('#'))), 0, TimeBudget.Milliseconds(5));

    [Fact]
    public void ToCellCanvasProjectionFitsInsideAFrameBudget()
    {
        var pc = new PCCanvas(13); Assert.InRange(BestMilliseconds(() => pc.ToCellCanvas(300, 100)), 0, TimeBudget.Milliseconds(8));
    }

    // Gap chiusi: storage indicizzato con proiezione a celle pigra.

    [Fact]
    public void PSetDoesNotAllocate()
    {
        var pc = new PCCanvas(13);
        Assert.Equal(0, Allocated(() => { for (int y = 0; y < 200; y++) for (int x = 0; x < 320; x++) pc.PSet(x, y, (x + y) & 255); }));
    }

    [Fact]
    public void PutImageDoesNotAllocate()
    {
        var pc = new PCCanvas(13); var sprite = pc.GetImage(0, 0, 32, 32);
        Assert.Equal(0, Allocated(() => pc.PutImage(sprite, 100, 100, PCPixelOperation.Xor)));
    }

    [Fact]
    public void Mode13FullFramePSetFitsInsideAFrameBudget()
    {
        var pc = new PCCanvas(13);
        Assert.InRange(BestMilliseconds(() => { for (int y = 0; y < 200; y++) for (int x = 0; x < 320; x++) pc.PSet(x, y, (x ^ y) & 255); }), 0, TimeBudget.Milliseconds(4));
    }

    [Fact]
    public void Mode13PaletteCycleOf256EntriesFitsInsideAFrameBudget()
    {
        var pc = new PCCanvas(13); int phase = 0;
        Assert.InRange(BestMilliseconds(() => { phase++; for (int i = 0; i < 256; i++) pc.SetDac(i, (i + phase) & 63, 0, 0); }, runs: 3), 0, TimeBudget.Milliseconds(16));
    }

    [Fact]
    public void PageFlipIsConstantTime()
    {
        var pc = new PCCanvas(9);
        Assert.InRange(BestMilliseconds(() => { pc.ActivePage ^= 1; pc.VisualPage ^= 1; }), 0, TimeBudget.Milliseconds(0.5));
    }

    [Fact]
    public void FullScreenPaintAllocatesLessThanTheFramebuffer()
    {
        var pc = new PCCanvas(13);
        Assert.InRange(Allocated(() => { pc.ClearIndex(0); pc.Paint(10, 10, 4); }), 0, 64_000);
    }
}
