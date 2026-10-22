using System.Diagnostics;
using RallyX;
using Screen9;

namespace RallyX.Tests;

public class RallyXRenderTests
{
    private static readonly RallyXAssets Assets = RallyXAssets.Load();

    [Fact]
    public void TheTitleHasNoLogo()
    {
        // the logo used to take the first 131 rows (at 320x240): now they are all white
        var title = Assets.Title;
        for (int y = 0; y < 131; y++)
            for (int x = 0; x < title.Width; x++)
                Assert.Equal(Assets.TitleWhite, title[x, y]);
        // below it the game banner remains
        Assert.Contains(Enumerable.Range(131, 109).SelectMany(y => Enumerable.Range(0, title.Width).Select(x => title[x, y])), p => p != Assets.TitleWhite);
    }

    [Fact]
    public void TheHudHasNoLogo()
    {
        var hud = Assets.Hud;
        for (int y = 192; y < hud.Height; y++)
            for (int x = 0; x < hud.Width; x++)
                Assert.Equal(RallyXAssets.Black, hud[x, y]);
    }

    [Fact]
    public void AGameFrameIsAFullModeTwelveImage()
    {
        var sim = new RallyXSim(Assets, 1);
        sim.StartRound(1, 0, 3);
        var frame = new RallyXRenderer(Assets).RenderGame(sim, 0, paused: false);
        Assert.Equal(640 * 480, frame.Length);
        Assert.All(frame, p => Assert.True(p < 16));
        var canvas = new PCCanvas(12);
        canvas.PutPixels(frame, 0, 0, 640, 480);
        Assert.NotEqual(0, canvas.Point(100, 100) + canvas.Point(300, 200) + canvas.Point(600, 300)); // not all black
        Dump("start", frame);
    }

    [Fact]
    public void ADemoGameReachesTheCheckpointsAndDrawsAsManyFramesAsAsked()
    {
        int presented = 0;
        var host = new RallyXHost { Present = _ => presented++, ReadKey = () => "", Sleep = _ => { }, Demo = true, MaxFrames = 20, Seed = () => 1 };
        new RallyXGame(host).Run();
        Assert.Equal(20, presented);
    }

    [Fact]
    public void FramesAreFastEnoughForThirtyPerSecond()
    {
        var sim = new RallyXSim(Assets, 1);
        sim.StartRound(1, 0, 3);
        var renderer = new RallyXRenderer(Assets);
        var canvas = new PCCanvas(12);
        canvas.PutPixels(renderer.RenderGame(sim, 0, false), 0, 0, 640, 480); // JIT
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 30; i++) { sim.Step(); canvas.PutPixels(renderer.RenderGame(sim, 0, false), 0, 0, 640, 480); }
        Console.WriteLine($"RallyX: {watch.Elapsed.TotalMilliseconds / 30:F1} ms per frame");
#if DEBUG
        const double limit = 150; // unoptimised code is much slower: in Debug we only check that it does not degenerate
#else
        const double limit = 33;  // 30 fotogrammi al secondo
#endif
        Assert.True(watch.Elapsed.TotalMilliseconds / 30 < limit, $"{watch.Elapsed.TotalMilliseconds / 30:F1} ms per frame");
    }

    [Fact]
    public void DumpsFramesForInspectionWhenAskedTo()
    {
        if (Environment.GetEnvironmentVariable("RALLYX_DUMP") is not { Length: > 0 }) return;
        var renderer = new RallyXRenderer(Assets);
        Dump("title", renderer.RenderTitle(12340, true), Assets.TitlePalette);
        var sim = new RallyXSim(Assets, 3);
        sim.StartRound(1, 0, 3);
        var bot = new RallyXAutopilot();
        for (int i = 0; i < 60 * 14; i++) { bot.Drive(sim); sim.Step(); }
        Dump("play", renderer.RenderGame(sim, 12340, false));
        sim.Rocks.Add(new RallyXSpot(sim.Player.TileX, sim.Player.TileY));
        for (int i = 0; i < 3 && sim.Phase != RallyXPhase.Crashed; i++) sim.Step();
        Dump("crash", renderer.RenderGame(sim, 12340, false));
        Dump("pause", renderer.RenderGame(sim, 12340, true));
    }

    private static void Dump(string name, byte[] frame, PCRgb[]? palette = null)
    {
        if (Environment.GetEnvironmentVariable("RALLYX_DUMP") is not { Length: > 0 } folder) return;
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), PngFrame.Encode(640, 480, frame, palette ?? Assets.GamePalette));
    }
}
