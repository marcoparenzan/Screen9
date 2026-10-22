using QBSharp.Cli;
using QBSharp.Cli.Editor;

namespace QBSharp.Tests;

public sealed class EditorDialectRegressionTests
{
    [Fact]
    public void InteractiveBurstDoesNotChangeExplicitOrBatchBudgets()
    {
        Assert.Equal(10_000_000,QBasicRunPolicy.MaximumSteps(true,"run",null,null,1_000_000));
        Assert.Equal(123,QBasicRunPolicy.MaximumSteps(true,"run",123,null,1_000_000));
        Assert.Equal(456,QBasicRunPolicy.MaximumSteps(true,"run",null,"456",456));
        Assert.Equal(1_000_000,QBasicRunPolicy.MaximumSteps(false,"run",null,null,1_000_000));
        Assert.Equal(1_000_000,QBasicRunPolicy.MaximumSteps(true,"test",null,null,1_000_000));
    }
    [Fact]
    public async Task InvalidEditorDialectDoesNotOpenTerminal()
    {
        bool opened=false;
        var app=new EditorApplication(()=>{opened=true;throw new InvalidOperationException();},()=>throw new InvalidOperationException(),dialect:"bad");
        await Assert.ThrowsAsync<ArgumentException>(()=>app.RunAsync(null));Assert.False(opened);
    }
}
