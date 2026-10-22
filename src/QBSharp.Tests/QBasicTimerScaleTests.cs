using QBSharp.Core.Hosting;
using QBSharp.Core.Diagnostics;
using QBSharp.QBasic;
using QBSharp.Cli;
namespace QBSharp.Tests;
public sealed class QBasicTimerScaleTests
{
    [Theory]
    [InlineData(1.0,200)] [InlineData(0.01,2)] [InlineData(0.001,1)]
    public async Task ScalingChangesOnlyEmptyElapsedTimerWaits(double scale,int expected)
    {
        double ms=0;int delays=0;
        await using var session=new QBasicEngine(new()
        {
            MaximumSteps=100,StepBudgetMode=QBasicStepBudgetMode.BetweenInputPolls,TimerWaitScale=scale,
            Clock=()=>new DateTime(2026,1,1).AddMilliseconds(ms),
            Delay=(_,token)=>{token.ThrowIfCancellationRequested();ms++;delays++;return ValueTask.CompletedTask;}
        }).CreateSession();
        await session.ExecuteAsync(new("s#=TIMER\nDO\nLOOP UNTIL TIMER-s#>=.2\nafter#=TIMER"));
        Assert.InRange(delays,expected,expected+1);
        Assert.InRange(Convert.ToDouble(session.Variables["after#"]),ms/1000-0.000001,ms/1000+0.000001);
    }
    [Theory]
    [InlineData("DO WHILE TIMER()-s#<.2\nLOOP")]
    [InlineData("WHILE TIMER-s#<.2\nWEND")]
    public async Task PreconditionWaitsAreScaled(string loop)
    {
        int ms=0;
        await using var session=new QBasicEngine(new(){TimerWaitScale=.01,StepBudgetMode=QBasicStepBudgetMode.BetweenInputPolls,
            Clock=()=>new DateTime(2026,1,1).AddMilliseconds(ms),Delay=(_,_)=>{ms++;return ValueTask.CompletedTask;}}).CreateSession();
        await session.ExecuteAsync(new("s#=TIMER\n"+loop));Assert.InRange(ms,2,3);
    }
    [Fact]
    public async Task NonemptyLoopsAreNotScaledOrExemptFromBudget()
    {
        await using var session=new QBasicEngine(new(){MaximumSteps=30,TimerWaitScale=.001,StepBudgetMode=QBasicStepBudgetMode.BetweenInputPolls,
            Clock=()=>new DateTime(2026,1,1)}).CreateSession();
        var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("s#=TIMER\nDO\nx=x+1\nLOOP UNTIL TIMER-s#>10")));
        Assert.Equal("QBS2401",error.Diagnostic.Code);
    }
    [Fact]
    public async Task OriginalRestWithLargeMachSpeedNeedsOnlyMillisecondsWhenOptedIn()
    {
        var text=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"samples","GORILLA.BAS"));
        int begin=text.LastIndexOf("SUB Rest (t#)",StringComparison.OrdinalIgnoreCase),end=text.IndexOf("END SUB",begin,StringComparison.OrdinalIgnoreCase)+7;
        int ms=0;
        await using var session=new QBasicEngine(new(){TimerWaitScale=.001,MaximumSteps=100,StepBudgetMode=QBasicStepBudgetMode.BetweenInputPolls,
            Clock=()=>new DateTime(2026,1,1).AddMilliseconds(ms),Delay=(_,_)=>{ms++;return ValueTask.CompletedTask;}}).CreateSession();
        await session.ExecuteAsync(new("DIM SHARED MachSpeed\nMachSpeed=256774*1.3\nCONST SPEEDCONST=500\nCALL Rest(.02#)\nfinished=1\n"+text[begin..end]));
        Assert.InRange(ms,13,15);Assert.Equal(1f,session.Variables["finished"]);
    }
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(1001)]
    public void InvalidHostScaleRejected(double scale)=>Assert.Throws<ArgumentOutOfRangeException>(()=>new QBasicEngine(new(){TimerWaitScale=scale}));
    [Theory]
    [InlineData("windows")] [InlineData("0")] [InlineData("NaN")] [InlineData("1,5")]
    public void InvalidCliScaleRejected(string scale)=>Assert.Throws<CliUsageException>(()=>QBasicRunPolicy.TimerWaitScale(scale));
    [Fact]
    public void CliScaleUsesInvariantNumbersAndPreservesDefault()
    {Assert.Equal(1,QBasicRunPolicy.TimerWaitScale(null));Assert.Equal(.001,QBasicRunPolicy.TimerWaitScale("0.001"));}
    [Fact]
    public void AutoScaleIsTheCalibratedOneAndShrinksQBasicEraWaits()
    {
        // This interpreter runs CalcDelay's empty loop far faster than the 500 per half second GORILLA.BAS was tuned
        // for, so "auto" must shrink TIMER waits (scale < 1), within the engine's accepted range.
        double scale=QBasicRunPolicy.CalibratedTimerWaitScale;
        Assert.InRange(scale,.0001,.5);
        Assert.Equal(scale,QBasicRunPolicy.TimerWaitScale("auto"));
        Assert.Equal(scale,QBasicRunPolicy.TimerWaitScale(" AUTO "));
    }
}
