using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.QBasic;

namespace Screen9.Web.Client.Components;

/// <summary>Runs a QBasic program on the QBSharp interpreter inside the browser, on a <see cref="PixelScreen"/>. The engine is async
/// end to end (INKEY$ polls and TIMER waits await a delay instead of spinning), so it shares the one browser thread with painting and keys.</summary>
public static class BrowserBasic
{
    private static Task<double>? calibration;

    /// <summary>Empty-loop iterations per half second that QBasic-era programs were tuned for: GORILLA.BAS's SPEEDCONST (500)
    /// is the value at which its Rest(t) lasts t seconds.</summary>
    private const double ReferenceLoopsPerHalfSecond = 500;

    /// <summary>The scale that brings the TIMER waits of QBasic-era games back to their intended length (this interpreter runs their
    /// empty-loop calibration far faster than a 1990 PC). Measured once, on this machine, the way the server does (<c>QBasicRunPolicy</c>,
    /// which lives in <c>QBSharp.Cli</c> and is not referenced from the browser): about half a second.</summary>
    public static Task<double> CalibrateAsync() => calibration ??= MeasureAsync();

    private static async Task<double> MeasureAsync()
    {
        await Task.Yield(); // let a pending paint happen before the half second of measuring
        await using var session = new QBasicEngine(new QBasicOptions { MaximumSteps = 100_000_000, AllowFileIO = false }).CreateSession();
        await session.ExecuteAsync(new ScriptSource("result=CalcDelay\nFUNCTION CalcDelay!\ns!=TIMER\nDO\ni!=i!+1\nLOOP UNTIL TIMER-s!>=.5\nCalcDelay!=i!\nEND FUNCTION", "calibrate.bas"));
        double loops = Convert.ToDouble(session.Variables["result"], System.Globalization.CultureInfo.InvariantCulture);
        return loops <= 0 ? 1 : Math.Clamp(ReferenceLoopsPerHalfSecond / loops, 0.0001, 1);
    }

    /// <summary>Returns "Completed", "Canceled" or "Error". A runtime error is also written to <paramref name="log"/>
    /// (and is on the last presented screen, as in QBasic).</summary>
    public static async Task<string> RunAsync(string source, string fileName, PixelScreen screen, Action<string> log, CancellationToken token)
    {
        double timerWaitScale = await CalibrateAsync();
        var engine = new QBasicEngine(new QBasicOptions
        {
            AllowFileIO = false, // no file access from a web page
            MaximumSteps = 10_000_000,
            StepBudgetMode = QBasicStepBudgetMode.BetweenInputPolls,
            InputPollingInterval = TimeSpan.FromMilliseconds(10),
            TimerWaitScale = timerWaitScale,
            ScreenInput = true, // one screen for text, graphics and INPUT echo, as the pixel window does
            ReadKey = screen.ReadKey,
            Audio = async (tone, t) =>
            {
                screen.RequestTone(tone.Frequency, tone.Duration.TotalMilliseconds);
                await Task.Delay(tone.Duration, t);
            },
            PresentText = cells => screen.PresentText(cells),
            PresentGraphics = graphics => screen.PresentGraphics(graphics),
        });
        await using var session = engine.CreateSession();
        try
        {
            await session.ExecuteAsync(new ScriptSource(source, fileName), token);
            return "Completed";
        }
        catch (OperationCanceledException) { return "Canceled"; } // stopped, or the page was left
        catch (ScriptRuntimeException error) { log(error.Diagnostic.ToString() ?? error.Message); return "Error"; }
        catch (Exception error) { log(error.Message); return "Error"; }
    }
}
