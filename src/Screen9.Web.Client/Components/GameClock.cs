namespace Screen9.Web.Client.Components;

/// <summary>Waits for the games running in the browser: there is no thread to sleep on, so every pause is awaited.</summary>
public static class GameClock
{
    /// <summary>Waits <paramref name="time"/>; a zero or negative wait still yields to the browser, so a game that is behind
    /// schedule lets keys and painting through instead of starving them.</summary>
    public static async ValueTask Delay(TimeSpan time, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (time > TimeSpan.Zero) await Task.Delay(time, token);
        else await Task.Yield();
    }

    /// <summary>PLAY "MB..." and the like: the tones play one after the other while the game goes on.</summary>
    public static async Task PlayBackground(IReadOnlyList<PCTone> tones, Action<PCTone> play, CancellationToken token)
    {
        try
        {
            foreach (var tone in tones)
            {
                play(tone);
                await Task.Delay(tone.Duration, token);
            }
        }
        catch (OperationCanceledException) { } // page left
    }
}
