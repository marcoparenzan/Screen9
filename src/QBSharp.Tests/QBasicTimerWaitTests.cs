using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.QBasic;
namespace QBSharp.Tests;
public sealed class QBasicTimerWaitTests
{
    [Theory]
    [InlineData("DO\nLOOP UNTIL TIMER-s#>.2")]
    [InlineData("DO WHILE TIMER()-s#<.2\nLOOP")]
    [InlineData("WHILE TIMER-s#<.2\nWEND")]
    public async Task EmptyTimerWaitYieldsWithoutChangingTimerPrecision(string loop)
    {
        double ms=0;int delays=0;
        await using var session=new QBasicEngine(new()
        {
            MaximumSteps=30,StepBudgetMode=QBasicStepBudgetMode.BetweenInputPolls,
            Clock=()=>new DateTime(2026,1,1).AddMilliseconds(ms),
            Delay=(duration,token)=>{token.ThrowIfCancellationRequested();Assert.Equal(1,duration.TotalMilliseconds);ms++;delays++;return ValueTask.CompletedTask;}
        }).CreateSession();
        await session.ExecuteAsync(new("s#=TIMER\n"+loop+"\nfinished=1"));
        Assert.Equal(1f,session.Variables["finished"]);Assert.InRange(delays,199,202);
    }
    [Fact]
    public async Task OriginalRestRoutineCompletesWithSmallInteractiveQuota()
    {
        string source=await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"samples","GORILLA.BAS"));
        int begin=source.LastIndexOf("SUB Rest (t#)",StringComparison.OrdinalIgnoreCase);
        Assert.True(begin>=0);int end=source.IndexOf("END SUB",begin,StringComparison.OrdinalIgnoreCase)+7;
        double ms=0;int delays=0;
        await using var session=new QBasicEngine(new()
        {
            MaximumSteps=100,StepBudgetMode=QBasicStepBudgetMode.BetweenInputPolls,
            Clock=()=>new DateTime(2026,1,1).AddMilliseconds(ms),
            Delay=(_,token)=>{token.ThrowIfCancellationRequested();ms++;delays++;return ValueTask.CompletedTask;}
        }).CreateSession();
        await session.ExecuteAsync(new("DIM SHARED MachSpeed\nMachSpeed=100\nCONST SPEEDCONST=100\nCALL Rest(.2#)\nfinished=1\n"+source[begin..end],"rest.bas"));
        Assert.Equal(1f,session.Variables["finished"]);Assert.True(delays>=200);
    }
    [Theory]
    [InlineData(QBasicStepBudgetMode.Total,false)]
    [InlineData(QBasicStepBudgetMode.BetweenInputPolls,true)]
    public async Task TotalOrFrozenClockCannotEvadeQuota(QBasicStepBudgetMode mode,bool frozen)
    {
        double ms=0;await using var session=new QBasicEngine(new()
        {
            MaximumSteps=30,StepBudgetMode=mode,Clock=()=>new DateTime(2026,1,1).AddMilliseconds(ms),
            Delay=(_,_)=>{if(!frozen)ms++;return ValueTask.CompletedTask;}
        }).CreateSession();
        var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("s#=TIMER\nDO\nLOOP UNTIL TIMER-s#>10")));
        Assert.Equal("QBS2401",error.Diagnostic.Code);
    }
    [Fact]
    public async Task NonemptyBodyDoesNotGetTimerBudgetReset()
    {
        await using var session=new QBasicEngine(new(){MaximumSteps=30,StepBudgetMode=QBasicStepBudgetMode.BetweenInputPolls}).CreateSession();
        var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("s#=TIMER\nDO\nx=x+1\nLOOP UNTIL TIMER-s#>100")));
        Assert.Equal("QBS2401",error.Diagnostic.Code);
    }
    [Fact]
    public async Task TimerWaitCanBeCanceledDuringDelay()
    {
        using var cancel=new CancellationTokenSource();
        await using var session=new QBasicEngine(new()
        {
            StepBudgetMode=QBasicStepBudgetMode.BetweenInputPolls,
            Delay=(_,token)=>{cancel.Cancel();token.ThrowIfCancellationRequested();return ValueTask.CompletedTask;}
        }).CreateSession();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await session.ExecuteAsync(new("s#=TIMER\nDO\nLOOP UNTIL TIMER-s#>10"),cancel.Token));
    }
}
