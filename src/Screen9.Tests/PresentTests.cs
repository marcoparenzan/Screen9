using System.Diagnostics;

namespace Screen9.Tests;

/// <summary>Presentazione manuale di PCCanvas: double buffering anche nei modi VGA a pagina singola.</summary>
public sealed class PresentTests
{
    [Fact]
    public void LiveProjectionIsTheDefault()
    {
        var pc = new PCCanvas(13); Assert.False(pc.ManualPresent);
        pc.ClearIndex(4); Assert.Equal(ConsoleColor.DarkRed, pc.ToCellCanvas(1, 1)[0, 0].Background);
        Assert.Throws<InvalidOperationException>(pc.Present);
    }

    [Fact]
    public void Mode13DrawingIsInvisibleUntilPresent()
    {
        var pc = new PCCanvas(13); pc.ClearIndex(1); pc.ManualPresent = true;
        var shown = pc.ToCellCanvas(40, 20).Snapshot(); Assert.All(shown, c => Assert.Equal(ConsoleColor.DarkBlue, c.Background));
        pc.ClearIndex(4); pc.Box(0, 0, 100, 100, 14, true);
        Assert.Equal(shown, pc.ToCellCanvas(40, 20).Snapshot()); // back buffer invisibile
        Assert.Equal(14, pc.Point(0, 0)); // la superficie di disegno è aggiornata
        pc.Present(); var next = pc.ToCellCanvas(40, 20);
        Assert.Equal(ConsoleColor.Yellow, next[0, 0].Background); Assert.Equal(ConsoleColor.DarkRed, next[39, 19].Background);
    }

    [Fact]
    public void PaletteChangesArePresentedTogetherWithPixels()
    {
        var pc = new PCCanvas(13); pc.ClearIndex(200); pc.SetDac(200, 63, 21, 21); pc.ManualPresent = true;
        pc.SetDac(200, 21, 21, 63); Assert.Equal(ConsoleColor.Red, pc.ToCellCanvas(1, 1)[0, 0].Background);
        Assert.Equal(new PCRgb(255, 85, 85), pc.VisiblePalette[200]); Assert.Equal(new PCRgb(85, 85, 255), pc.GetRgb(200));
        pc.Present(); Assert.Equal(ConsoleColor.Blue, pc.ToCellCanvas(1, 1)[0, 0].Background); Assert.Equal(pc.GetRgb(200), pc.VisiblePalette[200]);
    }

    [Fact]
    public void PresentSamplesVisualPageInMultiPageModes()
    {
        var pc = new PCCanvas(9); pc.ManualPresent = true; pc.ActivePage = 1; pc.ClearIndex(2);
        pc.Present(); Assert.Equal(ConsoleColor.Black, pc.ToCellCanvas(1, 1)[0, 0].Background); // VisualPage è ancora 0
        pc.VisualPage = 1; Assert.Equal(ConsoleColor.Black, pc.ToCellCanvas(1, 1)[0, 0].Background); // il flip si vede al Present
        pc.Present(); Assert.Equal(ConsoleColor.DarkGreen, pc.ToCellCanvas(1, 1)[0, 0].Background);
        Assert.Equal(pc.PixelSnapshot(1), pc.VisiblePixels.ToArray());
    }

    [Fact]
    public void DisablingReturnsToLiveProjectionAndReenablingPresentsImmediately()
    {
        var pc = new PCCanvas(12); pc.ManualPresent = true; pc.ClearIndex(4);
        pc.ManualPresent = false; Assert.Equal(ConsoleColor.DarkRed, pc.ToCellCanvas(1, 1)[0, 0].Background);
        pc.ManualPresent = true; pc.ClearIndex(1); Assert.Equal(ConsoleColor.DarkRed, pc.ToCellCanvas(1, 1)[0, 0].Background);
        pc.ManualPresent = true; Assert.Equal(ConsoleColor.DarkRed, pc.ToCellCanvas(1, 1)[0, 0].Background); // già attivo: nessun present implicito
    }

    [Fact]
    public void ProjectionIntoAnExistingCanvasMatchesTheAllocatingOverload()
    {
        var pc = new PCCanvas(13); var random = new Random(13);
        for (int i = 0; i < 200; i++) pc.Ellipse(random.Next(320), random.Next(200), random.Next(1, 30), random.Next(1, 30), random.Next(256), true);
        var destination = new Canvas(160, 50); destination.Clear(new Cell('x')); pc.ToCellCanvas(destination);
        Assert.Equal(pc.ToCellCanvas(160, 50).Snapshot(), destination.Snapshot());
        Assert.Throws<ArgumentNullException>(() => pc.ToCellCanvas(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => pc.ToCellCanvas(new Canvas(301, 1)));
    }
}

[Collection(nameof(PerformanceCollection))] [Trait("Category", "Performance")]
public sealed class PresentPipelinePerformanceTests
{
    [Fact]
    public void Mode13FramePipelineDoesNotAllocateAndFitsInsideAFrameBudget()
    {
        var pc = new PCCanvas(13) { ManualPresent = true }; var screen = new Canvas(160, 100); var frame = new ConsoleFrameBuffer();
        var target = new FrameTarget(0, 0, 160, 100, 0, 0); int tick = 0;
        void Step()
        {
            pc.ClearIndex(1); pc.Ellipse(tick % 320, 100, 20, 20, 14, true); pc.SetDac(14, tick % 64, 63, 0); tick++;
            pc.Present(); pc.ToCellCanvas(screen);
            if (frame.Prepare(screen.Cells, screen.Width, screen.Height, target, out _)) frame.Commit();
        }
        for (int i = 0; i < 1000; i++) Step(); // oltre la ricompilazione a livelli del JIT
        long before = GC.GetAllocatedBytesForCurrentThread(); for (int i = 0; i < 100; i++) Step();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        double best = double.MaxValue;
        for (int i = 0; i < 5; i++) { var watch = Stopwatch.StartNew(); Step(); best = Math.Min(best, watch.Elapsed.TotalMilliseconds); }
        Assert.InRange(best, 0, TimeBudget.Milliseconds(4));
    }
}
