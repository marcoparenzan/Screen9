using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.QBasic;
using QBSharp.Cli;

namespace QBSharp.Tests;

public sealed class QBasicPollingTests
{
    [Theory]
    [InlineData("INKEY$")][InlineData("INKEY$()")]
    public async Task InteractiveWaitExceedsTotalBudgetButResumesOnKey(string expression)
    {
        int polls=0,delays=0;
        await using var session=new QBasicEngine(new()
        {
            MaximumSteps=30,StepBudgetMode=QBasicStepBudgetMode.BetweenInputPolls,
            ReadKey=()=>++polls>=200?"x":"",
            Delay=(interval,token)=>{Assert.Equal(TimeSpan.FromMilliseconds(10),interval);token.ThrowIfCancellationRequested();delays++;return ValueTask.CompletedTask;}
        }).CreateSession();
        await session.ExecuteAsync(new($"DO\nkey$={expression}\nLOOP UNTIL key$=\"x\"\nfinished=1"));
        Assert.Equal(200,polls);Assert.Equal(199,delays);Assert.Equal(1f,session.Variables["finished"]);
    }
    [Fact]
    public async Task DefaultTotalBudgetStillLimitsEmptyInputLoops()
    {
        int polls=0;
        await using var session=new QBasicEngine(new(){MaximumSteps=30,ReadKey=()=>{polls++;return "";}}).CreateSession();
        var e=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("DO\nk$=INKEY$\nLOOP UNTIL k$<>\"\"")));
        Assert.Equal("QBS2401",e.Diagnostic.Code);Assert.InRange(polls,1,29);
    }
    [Theory]
    [InlineData("DO\ni=i+1\nLOOP")]
    [InlineData("k$=INKEY$\nDO\ni=i+1\nLOOP")]
    public async Task CpuCodeWithoutFurtherPollIsStillLimited(string source)
    {
        await using var session=new QBasicEngine(new(){MaximumSteps=30,StepBudgetMode=QBasicStepBudgetMode.BetweenInputPolls,Delay=(_,_)=>ValueTask.CompletedTask}).CreateSession();
        var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new(source)));
        Assert.Equal("QBS2401",error.Diagnostic.Code);
    }
    [Fact]
    public async Task WaitingPollIsAsyncAndCancellable()
    {
        using var cancel=new CancellationTokenSource();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session=new QBasicEngine(new()
        {
            StepBudgetMode=QBasicStepBudgetMode.BetweenInputPolls,
            Delay=async(_,token)=>{entered.SetResult();await Task.Delay(Timeout.Infinite,token);}
        }).CreateSession();
        var run=session.ExecuteAsync(new("DO\nk$=INKEY$\nLOOP"),cancel.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));Assert.False(run.IsCompleted);cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>run);Assert.DoesNotContain("k$",session.Variables.Keys);
    }
    [Fact]
    public async Task TotalModePacingDoesNotResetBudget()
    {
        int delays=0;await using var session=new QBasicEngine(new()
        {
            MaximumSteps=30,InputPollingInterval=TimeSpan.FromMilliseconds(10),
            Delay=(_,_)=>{delays++;return ValueTask.CompletedTask;}
        }).CreateSession();
        var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("DO\nk$=INKEY$\nLOOP")));
        Assert.Equal("QBS2401",error.Diagnostic.Code);Assert.InRange(delays,1,29);
    }
    [Theory]
    [InlineData(true,"run",false,false,QBasicStepBudgetMode.BetweenInputPolls)]
    [InlineData(true,"run",true,false,QBasicStepBudgetMode.Total)]
    [InlineData(true,"run",false,true,QBasicStepBudgetMode.Total)]
    [InlineData(false,"run",false,false,QBasicStepBudgetMode.Total)]
    [InlineData(true,"test",false,false,QBasicStepBudgetMode.Total)]
    public void CliPolicyHonorsExplicitBudgetBatchAndTests(bool interactive,string command,bool explicitSteps,bool configured,QBasicStepBudgetMode expected)
        =>Assert.Equal(expected,QBasicRunPolicy.BudgetMode(interactive,command,explicitSteps?30:null,configured?"30":null));
    [Theory]
    [InlineData(-1)][InlineData(1001)]
    public void InvalidPollingIntervalsRejected(int ms)=>Assert.Throws<ArgumentOutOfRangeException>(()=>new QBasicEngine(new(){InputPollingInterval=TimeSpan.FromMilliseconds(ms)}));
    [Fact]
    public void InvalidBudgetModeRejected()=>Assert.Throws<ArgumentOutOfRangeException>(()=>new QBasicEngine(new(){StepBudgetMode=(QBasicStepBudgetMode)99}));
}
