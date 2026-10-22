#!/usr/bin/env dotnet
#:project ../../../src/Screen9/Screen9.csproj
#:project RallyXCore.csproj
#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable

// Rally-X for Screen9: a C# game in the engine mode 12 (640x480, 16 colours), with the art and rules of the
// RallyX project (Phaser) brought to true pixel art (24 px tiles, 16 px sprites, 320x240 screen doubled).
// The original Rally-X art (Namco/Midway) is used for demonstration only; the logo of the RallyX project author
// is not included. The game (RallyXHost, RallyXGame, RallyXSim) lives in RallyXCore.csproj: this
// file is just the desktop launcher in a pixel window, so the same game also runs elsewhere without duplication.
//
//   dotnet run -c Release samples/csharp/RallyX/RallyX.cs                  play in a pixel window (Windows)
//   dotnet run -c Release samples/csharp/RallyX/RallyX.cs -- --sound       with the speaker (Console.Beep)
//   dotnet run -c Release samples/csharp/RallyX/RallyX.cs -- --demo        the autopilot plays by itself
//   dotnet run samples/csharp/RallyX/RallyX.cs -- --selftest               autopilot matches without a window (seed: RALLYX_SEED)
//   dotnet run samples/csharp/RallyX/RallyX.cs -- --smoke                  demo in the window, with checks
// Controls: arrows or WASD to drive, SPACE for smoke (3 puffs, 1 fuel each), P pause, ESC quits.
using RallyX;
using Screen9;

return RallyXDemo.Run(args);

static class RallyXDemo
{
    public static int Run(string[] args)
    {
        if (args.Contains("--selftest")) return SelfTest();
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("La finestra a pixel richiede Windows (GDI)."); return 1; }
        bool smoke = args.Contains("--smoke"), demo = smoke || args.Contains("--demo"), sound = args.Contains("--sound") && !smoke;
        // Window with a 1280x960 client area (exactly 2x of 640x480) and scaling by whole multiples only: all pixels the same size.
        using var window = new PixelWindow("Rally-X — Screen9 (close to stop)", windowWidth: 1296, windowHeight: 1000) { IntegerScale = true };
        void Sleep(TimeSpan delay)
        {
            // In slices, refreshing the window: the last frame still shows up even if capture was limited to 30 Hz.
            long end = Environment.TickCount64 + (long)delay.TotalMilliseconds;
            do { window.Refresh(); Thread.Sleep((int)Math.Clamp(end - Environment.TickCount64, 0, 10)); } while (Environment.TickCount64 < end);
            window.Refresh();
        }
        var host = new RallyXHost
        {
            Present = window.Present,
            ReadKey = window.ReadKey,
            Sleep = Sleep,
            Sound = tones => { if (sound) _ = Task.Run(async () => { foreach (var tone in tones) await PCSpeaker.Beep(tone, default); }); },
            Demo = demo,
            MaxFrames = smoke ? 240 : null
        };
        try
        {
            var game = new RallyXGame(host);
            game.Run();
            if (!smoke) return 0;
            Sleep(TimeSpan.FromMilliseconds(400));
            string? failure = null;
            if (game.FramesPresented != 240) failure = $"{game.FramesPresented} fotogrammi presentati invece di 240";
            else if (window.CaptureCount == 0) failure = "la finestra non ha catturato frame";
            else if (window.CompositionCount == 0) failure = "la finestra non ha composto frame (WM_PAINT)";
            else if (game.LastScore <= 0) failure = $"il pilota automatico non ha fatto punti ({game.LastScore})";
            if (failure is not null) { Console.Error.WriteLine("Smoke RallyX fallito: " + failure); return 2; }
            Console.WriteLine($"Smoke RallyX: punteggio {game.LastScore}, round {game.LastRound}, {window.CaptureCount} frame catturati, {window.CompositionCount} composizioni.");
            return 0;
        }
        catch (OperationCanceledException) { return 0; } // window closed by the user
    }

    /// <summary>Three autopilot matches without devices: no exceptions, points scored, rounds completed.</summary>
    private static int SelfTest()
    {
        int seed = int.TryParse(Environment.GetEnvironmentVariable("RALLYX_SEED"), out var s) ? s : 1;
        var assets = RallyXAssets.Load();
        int failures = 0;
        for (int game = 0; game < 3; game++)
        {
            var sim = new RallyXSim(assets, seed + game);
            sim.StartRound(1, 0, 3);
            var bot = new RallyXAutopilot();
            for (long step = 0; step < 60 * 60 * 3 && !sim.Finished; step++) { bot.Drive(sim); sim.Step(); }
            Console.WriteLine($"Partita {game + 1} (seme {seed + game}): punteggio {sim.Score}, round {sim.Round}, vite {sim.Lives}, {sim.Time / RallyXSim.Hz} s.");
            if (sim.Score <= 0 || sim.Round < 1) failures++;
        }
        if (failures > 0) { Console.Error.WriteLine("Selftest RallyX fallito."); return 2; }
        Console.WriteLine("Selftest RallyX: ok.");
        return 0;
    }
}
