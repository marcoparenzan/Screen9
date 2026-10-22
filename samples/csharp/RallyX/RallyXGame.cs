using System.Diagnostics;
using Screen9;

namespace RallyX;

/// <summary>How the game talks to the outside world: screen, keyboard, time and sound. No window, no browser.</summary>
public sealed class RallyXHost
{
    public required Action<PCCanvas> Present { get; init; }
    public required Func<string> ReadKey { get; init; }
    public required Action<TimeSpan> Sleep { get; init; }
    public Action<IReadOnlyList<PCTone>> Sound { get; init; } = _ => { };
    public Func<int> Seed { get; init; } = () => Environment.TickCount;
    public RallyXAssets? Assets { get; init; }
    /// <summary>The autopilot plays by itself, without the title screen; any key quits.</summary>
    public bool Demo { get; init; }
    /// <summary>Stops after this many frames (tests and smoke runs).</summary>
    public int? MaxFrames { get; init; }
    public Action<string, RallyXGame>? Checkpoint { get; init; }
}

/// <summary>The game: title, match (or demo) and back to the title, at 30 frames per second with two simulation steps
/// each. Mode 12 (640x480, 16 colours): the 320x240 art doubled, 272x240 of field plus 48 of HUD.</summary>
public sealed class RallyXGame
{
    public const int FramesPerSecond = 30;

    private readonly RallyXHost host;
    private readonly RallyXAssets assets;
    private readonly RallyXRenderer renderer;
    private readonly PCCanvas canvas = new(12);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private PCRgb[]? palette;
    private TimeSpan frameStart;
    private bool stopped;

    public int HighScore { get; private set; }
    public int FramesPresented { get; private set; }
    public int LastScore { get; private set; }
    public int LastRound { get; private set; }

    public RallyXGame(RallyXHost host)
    {
        this.host = host;
        assets = host.Assets ?? RallyXAssets.Load();
        renderer = new RallyXRenderer(assets);
    }

    /// <summary>Title, match, title... until Esc on the title screen (or the requested frames run out).</summary>
    public void Run()
    {
        while (!stopped)
        {
            if (!host.Demo && !Title()) return;
            Play();
            if (host.Demo) return;
        }
    }

    private bool Title()
    {
        SetPalette(assets.TitlePalette);
        host.Checkpoint?.Invoke("title", this);
        for (int frame = 0; !stopped; frame++)
        {
            BeginFrame();
            while (host.ReadKey() is { Length: > 0 } key)
            {
                if (key is " " or "\r") return true;
                if (key == "\x1b") return false;
            }
            Show(renderer.RenderTitle(HighScore, showPrompt: frame / 15 % 2 == 0));
            if (host.MaxFrames is { } max && FramesPresented >= max) stopped = true;
            EndFrame();
        }
        return false;
    }

    private void Play()
    {
        SetPalette(assets.GamePalette);
        var sim = new RallyXSim(assets, host.Seed());
        sim.Sound += OnSound;
        sim.StartRound(1, 0, 3);
        var bot = host.Demo ? new RallyXAutopilot() : null;
        bool paused = false;
        int announced = 1;
        host.Checkpoint?.Invoke("round", this);
        while (!sim.Finished && !stopped)
        {
            BeginFrame();
            while (host.ReadKey() is { Length: > 0 } key)
            {
                if (host.Demo) { stopped = true; break; }
                switch (key)
                {
                    case "\0H" or "w" or "W": sim.Steer(RallyXDir.Up); break;
                    case "\0M" or "d" or "D": sim.Steer(RallyXDir.Right); break;
                    case "\0P" or "s" or "S": sim.Steer(RallyXDir.Down); break;
                    case "\0K" or "a" or "A": sim.Steer(RallyXDir.Left); break;
                    case " ": sim.DropSmoke(); break;
                    case "p" or "P": paused = !paused; break;
                    case "\x1b": Leave(sim); return;
                }
            }
            // 60 simulation steps per second over 30 frames: two steps per frame (the autopilot drives at every step).
            if (!paused)
                for (int step = 0; step < RallyXSim.Hz / FramesPerSecond; step++) { bot?.Drive(sim); sim.Step(); }
            if (sim.Round != announced) { announced = sim.Round; host.Checkpoint?.Invoke("round", this); }
            LastScore = sim.Score; LastRound = sim.Round;
            Show(renderer.RenderGame(sim, Math.Max(HighScore, sim.Score), paused));
            if (host.MaxFrames is { } max && FramesPresented >= max) stopped = true;
            EndFrame();
        }
        Leave(sim);
        if (sim.Finished) host.Checkpoint?.Invoke("gameover", this);
    }

    private void Leave(RallyXSim sim)
    {
        LastScore = sim.Score; LastRound = sim.Round;
        HighScore = Math.Max(HighScore, sim.Score);
    }

    private void Show(byte[] frame)
    {
        canvas.PutPixels(frame, 0, 0, RallyXRenderer.Width, RallyXRenderer.Height);
        host.Present(canvas);
        FramesPresented++;
    }

    /// <summary>Title and match use different palettes (16 colours each): the DAC is loaded only when it changes. Values are
    /// 8-bit, the VGA DAC is 6-bit.</summary>
    private void SetPalette(PCRgb[] colors)
    {
        if (ReferenceEquals(palette, colors)) return;
        palette = colors;
        for (int i = 0; i < colors.Length; i++) canvas.SetDac(i, colors[i].Red >> 2, colors[i].Green >> 2, colors[i].Blue >> 2);
    }

    private void BeginFrame() => frameStart = clock.Elapsed;

    /// <summary>Sleeps the rest of the frame (1/30 s minus what has already passed): pacing belongs to the game, the host decides how to sleep.</summary>
    private void EndFrame()
    {
        var remaining = TimeSpan.FromSeconds(1.0 / FramesPerSecond) - (clock.Elapsed - frameStart);
        host.Sleep(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
    }

    private void OnSound(RallyXSound sound)
    {
        PCTone T(double frequency, int milliseconds) => new(frequency, TimeSpan.FromMilliseconds(milliseconds), Background: true);
        host.Sound(sound switch
        {
            RallyXSound.Start => [T(523, 70), T(659, 70), T(784, 110)],
            RallyXSound.Flag => [T(988, 50), T(1319, 90)],
            RallyXSound.Smoke => [T(196, 60)],
            RallyXSound.Crash => [T(392, 70), T(294, 70), T(196, 90), T(131, 160)],
            RallyXSound.RoundClear => [T(523, 90), T(659, 90), T(784, 90), T(1047, 220)],
            _ => [T(392, 140), T(330, 140), T(262, 140), T(196, 360)]
        });
    }
}
