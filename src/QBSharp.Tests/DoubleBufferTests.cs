using Screen9;
using QBSharp.Core.Hosting;
using QBSharp.QBasic;
namespace QBSharp.Tests;
public sealed class DoubleBufferTests
{
    [Fact]
    public async Task TimerRefreshFlushesWithoutResubmittingUnchangedGraphics()
    {
        int ms=0,graphics=0,refresh=0;
        await using var session=new QBasicEngine(new()
        {
            StepBudgetMode=QBasicStepBudgetMode.BetweenInputPolls,MaximumSteps=100,
            Clock=()=>new DateTime(2026,1,1).AddMilliseconds(ms),
            Delay=(_,_)=>{ms++;return ValueTask.CompletedTask;},
            PresentGraphics=_=>graphics++,RefreshDisplay=()=>refresh++
        }).CreateSession();
        await session.ExecuteAsync(new("SCREEN 9\ns#=TIMER\nDO\nLOOP UNTIL TIMER-s#>.2"));
        Assert.Equal(1,graphics);Assert.InRange(refresh,200,202);
    }
}
