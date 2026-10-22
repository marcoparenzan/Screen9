namespace Screen9.Scripting;

/// <summary>The devices a C# script (.csx) game gets from whoever runs it, as global variables — the same
/// contract as <c>GorillasHost</c> and as the <c>web_host</c> of <c>gorillas.py</c>: a screen to present, keys to
/// read, a clock to sleep on, a speaker. The command line (<c>ops/csx.cs</c>, over a PixelWindow) and the
/// website (over a browser canvas) both build one with <see cref="ForDisplay"/>, so the script is identical in
/// both and contains no host code at all.</summary>
public sealed class GameGlobals
{
    /// <summary>Presents the screen: the graphics canvas, or (when null) the text cells.</summary>
    public Action<PCCanvas?, Canvas> Present { get; init; } = (_, _) => { };
    /// <summary>INKEY$: "" when no key is waiting.</summary>
    public Func<string> ReadKey { get; init; } = () => "";
    public Action FlushKeys { get; init; } = () => { };
    public Action<TimeSpan> Sleep { get; init; } = Thread.Sleep;
    /// <summary>Plays one tone and returns when it is over (pacing depends on it).</summary>
    public Action<PCTone> Tone { get; init; } = _ => { };
    /// <summary>PLAY "MB...": returns at once, the notes play meanwhile.</summary>
    public Action<IReadOnlyList<PCTone>> Background { get; init; } = _ => { };
    public Func<int> Seed { get; init; } = () => Environment.TickCount;
    public string[] Args { get; init; } = [];

    /// <summary>Globals over a <see cref="IPixelDisplay"/> (a PixelWindow, a browser canvas). Closing the display
    /// or cancelling <paramref name="token"/> makes the next device call throw OperationCanceledException, which ends
    /// the script. <paramref name="playTone"/> must not block: pacing is done here, as PCSpeaker.Beep does.</summary>
    public static GameGlobals ForDisplay(IPixelDisplay display, Action<PCTone> playTone, CancellationToken token, string[]? args = null)
    {
        void Sleep(TimeSpan delay)
        {
            long end = Environment.TickCount64 + (long)delay.TotalMilliseconds;
            do
            {
                token.ThrowIfCancellationRequested();
                display.Refresh();
                Thread.Sleep((int)Math.Clamp(end - Environment.TickCount64, 0, 15));
            } while (Environment.TickCount64 < end);
        }
        return new GameGlobals
        {
            Present = (graphics, cells) => { if (graphics is not null) display.Present(graphics); else display.Present(cells); },
            ReadKey = () => { token.ThrowIfCancellationRequested(); return display.ReadKey(); },
            FlushKeys = () => { while (display.ReadKey().Length > 0) { } },
            Sleep = Sleep,
            Tone = tone => { playTone(tone); Sleep(tone.Duration); },
            Background = tones => _ = Task.Run(async () =>
            {
                foreach (var tone in tones)
                {
                    token.ThrowIfCancellationRequested();
                    playTone(tone);
                    await Task.Delay(tone.Duration, token);
                }
            }, token),
            Seed = () => Random.Shared.Next(),
            Args = args ?? [],
        };
    }
}
