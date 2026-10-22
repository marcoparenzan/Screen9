using QBSharp.QBasic;

namespace QBSharp.Cli;

public static class QBasicRunPolicy
{
    public const long InteractiveMaximumSteps=10_000_000;
    /// <summary>Empty-loop iterations per half second that QBasic-era programs were tuned for: GORILLA.BAS's
    /// SPEEDCONST (500) is the value at which its Rest(t) lasts t seconds.</summary>
    public const double ReferenceLoopsPerHalfSecond=500;
    private static readonly Lazy<double> calibrated=new(MeasureTimerWaitScale);
    /// <summary>
    /// QBasic-era games time their pauses by counting how fast an empty loop runs against TIMER (GORILLA.BAS's
    /// CalcDelay) and then waiting MachSpeed * t / SPEEDCONST seconds. This interpreter runs that loop ~100x faster
    /// than a PC of the time, so every pause becomes ~100x longer (a banana step: seconds instead of 20 ms).
    /// Measured once per process: the scale that brings pure TIMER waits back to their intended length.
    /// </summary>
    public static double CalibratedTimerWaitScale=>calibrated.Value;
    private static double MeasureTimerWaitScale()
    {
        var session=new QBasicEngine(new(){MaximumSteps=100_000_000,AllowFileIO=false}).CreateSession();
        try
        {
            // The same shape as GORILLA.BAS's CalcDelay (a FUNCTION with a local counter), so the rate matches the game's own.
            // Several rounds, keeping the fastest: the first ones run on cold, unoptimized JIT code (tier 0) and count
            // ~5x fewer loops than the warmed-up interpreter the game will actually measure with.
            double loops=0;
            for(int round=0;round<4;round++)
            {
                session.ExecuteAsync(new("result=CalcDelay\nFUNCTION CalcDelay!\ns!=TIMER\nDO\ni!=i!+1\nLOOP UNTIL TIMER-s!>=.5\nCalcDelay!=i!\nEND FUNCTION")).AsTask().GetAwaiter().GetResult();
                loops=Math.Max(loops,Convert.ToDouble(session.Variables["result"],System.Globalization.CultureInfo.InvariantCulture));
            }
            return loops<=0?1:Math.Clamp(ReferenceLoopsPerHalfSecond/loops,0.0001,1);
        }
        finally{session.DisposeAsync().AsTask().GetAwaiter().GetResult();}
    }
    /// <summary>QBS_TIMER_WAIT_SCALE: a number in 0.0001..1000, or "auto" for <see cref="CalibratedTimerWaitScale"/>.</summary>
    public static double TimerWaitScale(string? value)
    {
        if(value is null)return 1;
        if(value.Trim().Equals("auto",StringComparison.OrdinalIgnoreCase))return CalibratedTimerWaitScale;
        if(!double.TryParse(value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out double scale)||!double.IsFinite(scale)||scale is <0.0001 or >1000)throw new CliUsageException("QBS_TIMER_WAIT_SCALE must be a number in 0.0001..1000 or auto (default 1).");
        return scale;
    }
    /// <summary>Never changes explicit CLI/env budgets, redirected runs, script tests or embedding defaults.</summary>
    public static QBasicStepBudgetMode BudgetMode(bool interactive,string command,long? explicitSteps,string? configuredSteps)
        =>interactive && command=="run" && explicitSteps is null && configuredSteps is null
            ?QBasicStepBudgetMode.BetweenInputPolls:QBasicStepBudgetMode.Total;
    public static long MaximumSteps(bool interactive,string command,long? explicitSteps,string? configuredSteps,long configuredValue)
        =>BudgetMode(interactive,command,explicitSteps,configuredSteps)==QBasicStepBudgetMode.BetweenInputPolls
            ?InteractiveMaximumSteps:explicitSteps??configuredValue;
}
