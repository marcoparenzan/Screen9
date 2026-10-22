using Screen9;
using QBSharp.Cli.Editor;
using QBSharp.Core.Hosting;
using QBSharp.QBasic;

namespace QBSharp.Tests;

public sealed class EditorPerformanceTests
{
    private sealed class Clock:TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>ticks;
        public void Advance(int ms)=>ticks+=TimeSpan.FromMilliseconds(ms).Ticks;
    }
    [Fact]
    public void ProjectionIsThrottledBeforeAllocationAndFinalImageIsCurrent()
    {
        var clock=new Clock();var mailbox=new EditorFrameMailbox(clock);var canvas=new PCCanvas(9);
        mailbox.SubmitGraphics(canvas);Assert.Equal(1,mailbox.GraphicsProjections);Assert.True(mailbox.TryTake(out var first));
        for(int i=0;i<1000;i++){canvas.PSet(0,0,i%16);mailbox.SubmitGraphics(canvas);}
        Assert.Equal(1,mailbox.GraphicsProjections);Assert.Equal(ConsoleColor.Black,first![0,0].Background);
        canvas.PSet(0,0,14);mailbox.SubmitGraphics(canvas);mailbox.Refresh(true);
        Assert.Equal(2,mailbox.GraphicsProjections);Assert.True(mailbox.TryTake(out var final));Assert.Equal(ConsoleColor.Yellow,final![0,0].Background);
    }
    [Fact]
    public void PendingTextFrameRefreshesWithoutNewPrintAndIdleDoesNotRepublish()
    {
        var clock=new Clock();var mailbox=new EditorFrameMailbox(clock);var canvas=new Canvas(4,1);
        canvas.DrawText(0,0,"OLD");mailbox.SubmitText(canvas);Assert.True(mailbox.TryTake(out var old));
        canvas.DrawText(0,0,"NEW");mailbox.SubmitText(canvas);Assert.False(mailbox.TryTake(out _));
        clock.Advance(34);mailbox.Refresh();Assert.True(mailbox.TryTake(out var current));Assert.Equal('N',current![0,0].Character);Assert.Equal('O',old![0,0].Character);
        long count=mailbox.PublishedFrames;clock.Advance(1000);mailbox.Refresh();Assert.Equal(count,mailbox.PublishedFrames);Assert.False(mailbox.TryTake(out _));
    }
    [Fact]
    public void LatestSlotAndModeChangesDoNotLeakOldGraphics()
    {
        var mailbox=new EditorFrameMailbox(new Clock());mailbox.SubmitGraphics(new PCCanvas(9));
        var text=new Canvas(2,1);text.DrawText(0,0,"OK");mailbox.SubmitText(text);mailbox.Refresh(true);
        Assert.True(mailbox.TryTake(out var frame));Assert.Equal(2,frame!.Width);Assert.Equal('O',frame[0,0].Character);Assert.Equal(1,mailbox.GraphicsProjections);
        mailbox.Reset();Assert.Equal(0,mailbox.PublishedFrames);Assert.False(mailbox.TryTake(out _));
    }
    [Theory]
    [InlineData(true,false,false,1000,false)]
    [InlineData(true,true,false,32,false)]
    [InlineData(true,true,false,33,true)]
    [InlineData(true,false,true,0,true)]
    [InlineData(false,false,false,99,false)]
    [InlineData(false,false,false,100,true)]
    public void RefreshPolicySkipsUnchangedProgramFrames(bool program,bool changed,bool ui,long elapsed,bool expected)
        =>Assert.Equal(expected,EditorRefreshPolicy.ShouldDraw(program,changed,ui,elapsed));
    [Fact]
    public void FrameBecomesScreen9CanvasWithQBasicColors()
    {
        var style=EditorFrame.CodeStyle;
        var frame=new EditorFrame([Enumerable.Repeat(new EditorCell('A',style),80).ToArray()]);
        var canvas=frame.ToCanvas();Assert.Equal((80,1),(canvas.Width,canvas.Height));
        Assert.Equal(new Cell('A',ConsoleColor.Gray,ConsoleColor.DarkBlue),canvas[79,0]);
        Assert.Equal(0x0000AA,CgaPalette.Rgb(canvas[0,0].Background).Packed);
    }
    [Theory]
    [InlineData("a\nb\nc")][InlineData("a\r\nb\r\nc\r\n")][InlineData("a\rb\rc")][InlineData("a😀\nb")]
    public void CachedLineIndexPreservesCoordinatesAndSnapshotIsolation(string text)
    {
        var document=new EditorDocument();document.Insert(text);var lines=document.Lines;lines[0]="tampered";
        Assert.NotEqual("tampered",document.Lines[0]);
        for(int offset=0;offset<=document.Text.Length;offset++)
        {
            document.Move(offset);int line=0,start=0;
            for(int i=0;i<document.Cursor;i++)if(document.Text[i] is '\r' or '\n')
            {if(document.Text[i]=='\r' && i+1<document.Cursor && document.Text[i+1]=='\n')i++;line++;start=i+1;}
            Assert.Equal((line,document.Cursor-start),document.Position);
        }
        document.MoveTo(1,0);Assert.Equal((1,0),document.Position);
        document.Insert("prefix");Assert.StartsWith("prefix",document.Lines[1]);document.Undo();Assert.DoesNotContain("prefix",document.Lines[1]);
        document.Redo();Assert.StartsWith("prefix",document.Lines[1]);document.New();Assert.Equal(new[]{""},document.Lines);Assert.Equal((0,0),document.Position);
    }
    [Fact]
    public async Task QueuedKeyHasNoArtificialDelayAndCancellationStillPropagates()
    {
        int delays=0;await using var session=new QBasicEngine(new(){StepBudgetMode=QBasicStepBudgetMode.BetweenInputPolls,ReadKey=()=>"x",Delay=(_,_)=>{delays++;return ValueTask.CompletedTask;}}).CreateSession();
        Assert.Equal("x",await session.EvaluateAsync(new("INKEY$")));Assert.Equal(0,delays);
        using var cancel=new CancellationTokenSource();await using var canceled=new QBasicEngine(new(){ReadKey=()=>{cancel.Cancel();return "x";}}).CreateSession();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await canceled.EvaluateAsync(new("INKEY$"),cancel.Token));
    }
}
