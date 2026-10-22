namespace QBSharp.QBasic;

/// <summary>Total is the default safety budget. BetweenInputPolls is explicitly intended for trusted interactive programs.</summary>
public enum QBasicRasterFont { Auto, Compact }
public enum QBasicStepBudgetMode
{
    Total,
    BetweenInputPolls
}

internal sealed partial class QBasicSession
{
    private async ValueTask<string> PollKeyAsync()
    {
        cancellation.ThrowIfCancellationRequested();
        var interval=options.InputPollingInterval;
        if(interval==TimeSpan.Zero && options.StepBudgetMode==QBasicStepBudgetMode.BetweenInputPolls)
            interval=TimeSpan.FromMilliseconds(10);
        string key=options.ReadKey() ?? throw new InvalidOperationException("ReadKey returned null.");
        cancellation.ThrowIfCancellationRequested();
        if(key.Length==0 && interval>TimeSpan.Zero)await options.Delay(interval,cancellation);
        cancellation.ThrowIfCancellationRequested();
        if(options.StepBudgetMode==QBasicStepBudgetMode.BetweenInputPolls)steps=0;
        return key;
    }
}
