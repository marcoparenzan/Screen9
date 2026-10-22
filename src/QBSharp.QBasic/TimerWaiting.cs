namespace QBSharp.QBasic;

internal sealed partial class BParser
{
    // Only side-effect-free arithmetic comparisons are eligible, never arbitrary calls or loop bodies.
    private static bool IsTimerWait(BExpr condition)
    {
        if(condition is BUnary { Op: "NOT" } not)condition=not.Value;
        if(condition is not BBinary { Op: "<" or "<=" or ">" or ">=" } comparison)return false;
        return Pure(comparison.Left)&&Pure(comparison.Right)&&(HasTimer(comparison.Left)||HasTimer(comparison.Right));
    }
    private static bool Pure(BExpr expression)=>expression switch
    {
        BLiteral=>true,BName=>true,
        BCall { Target: BName name, Args.Count: 0 }=>name.Name.Equals("TIMER",StringComparison.OrdinalIgnoreCase),
        BUnary { Op: "+" or "-" } unary=>Pure(unary.Value),
        BBinary { Op: "+" or "-" or "*" or "/" } binary=>Pure(binary.Left)&&Pure(binary.Right),_=>false
    };
    private static bool HasTimer(BExpr expression)=>expression switch
    {
        BName name=>name.Name.Equals("TIMER",StringComparison.OrdinalIgnoreCase),
        BCall { Target: BName name, Args.Count: 0 }=>name.Name.Equals("TIMER",StringComparison.OrdinalIgnoreCase),
        BUnary unary=>HasTimer(unary.Value),BBinary binary=>HasTimer(binary.Left)||HasTimer(binary.Right),_=>false
    };
}
internal sealed partial class QBasicSession
{
    private async ValueTask YieldTimerWait(Dictionary<int,long> started,int pc)
    {
        long now=Environment.TickCount64;
        if(!started.TryGetValue(pc,out long first))started[pc]=first=now;
        if(now-first>60_000)throw new QBSharp.Core.Diagnostics.ScriptRuntimeException(new("QBS2401","QBasic TIMER wait exceeded 60 seconds.",currentSpan));
        cancellation.ThrowIfCancellationRequested();
        var before=options.Clock();
        await options.Delay(TimeSpan.FromMilliseconds(1),cancellation);
        cancellation.ThrowIfCancellationRequested();
        // A frozen/backwards clock must not disable the step budget.
        if(options.Clock()>before)steps=0;
        if(options.RefreshDisplay is not null)options.RefreshDisplay();
        else if(graphics is not null)options.PresentGraphics?.Invoke(graphics);
    }
}
