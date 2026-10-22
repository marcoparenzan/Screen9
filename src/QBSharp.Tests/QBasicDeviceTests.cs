using Screen9;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.QBasic;

namespace QBSharp.Tests;

public sealed class QBasicDeviceTests
{
    [Fact]
    public async Task LineBoxCornersAreInclusiveAndNotWidthHeight()
    {
        // Regressione: B/BF passavano il secondo vertice come larghezza/altezza a PCCanvas.Box (edifici Gorillas pieni di finestre giganti).
        PCCanvas? frame=null;await using var session=new QBasicEngine(new(){PresentGraphics=c=>frame=c}).CreateSession();
        await session.ExecuteAsync(new("SCREEN 9\nLINE (103,285)-(106,291),14,BF\nLINE (30,26)-(10,20),2,B"));
        Assert.NotNull(frame);
        Assert.Equal(14,frame.Point(103,285));Assert.Equal(14,frame.Point(106,291));
        Assert.Equal(0,frame.Point(107,288));Assert.Equal(0,frame.Point(104,292));Assert.Equal(0,frame.Point(102,288));Assert.Equal(0,frame.Point(120,300));
        Assert.Equal(2,frame.Point(10,20));Assert.Equal(2,frame.Point(30,26));Assert.Equal(2,frame.Point(30,23));Assert.Equal(0,frame.Point(20,23));Assert.Equal(0,frame.Point(31,23));
    }
    [Fact]
    public async Task VirtualSegmentsAliasWrapAndStayIsolated()
    {
        await using var a=new QBasicEngine().CreateSession();await using var b=new QBasicEngine().CreateSession();
        await a.ExecuteAsync(new("DEF SEG=0\nPOKE 1047,32\nDEF SEG=&H40\nflag%=PEEK(23)\nDEF SEG=&HFFFF\nPOKE 16,123\nDEF SEG\nzero%=PEEK(0)"));
        Assert.Equal((short)32,a.Variables["flag%"]);Assert.Equal((short)123,a.Variables["zero%"]);
        Assert.Equal((short)0,await b.EvaluateAsync(new("PEEK(1047)")));
    }
    [Theory]
    [InlineData("DEF SEG=-1")][InlineData("DEF SEG=65536")][InlineData("POKE 0,256")][InlineData("POKE -1,0")][InlineData("a=PEEK(65536)")]
    public async Task InvalidMemoryAccessHasSourceDiagnostic(string text)
    {
        await using var s=new QBasicEngine().CreateSession();var e=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await s.ExecuteAsync(new(text,"device.bas")));
        Assert.Equal("device.bas",e.Diagnostic.Span.File);Assert.Equal(1,e.Diagnostic.Span.Start.Line);
    }
    [Fact]
    public async Task InkeyIsNonBlockingAndSleepCanBeInterruptedByKey()
    {
        Queue<string> keys=new(["a","", "", "\r"]);int delays=0;
        await using var s=new QBasicEngine(new(){ReadKey=()=>keys.Count>0?keys.Dequeue():"",Delay=(_,_)=>{delays++;return ValueTask.CompletedTask;}}).CreateSession();
        Assert.Equal("a",await s.EvaluateAsync(new("INKEY$")));Assert.Equal("",await s.EvaluateAsync(new("INKEY$")));
        await s.ExecuteAsync(new("SLEEP 1"));Assert.Equal(1,delays);
    }
    [Fact]
    public async Task SleepHonorsCancellation()
    {
        using var cancel=new CancellationTokenSource();
        await using var s=new QBasicEngine(new(){Delay=(_,token)=>{cancel.Cancel();token.ThrowIfCancellationRequested();return ValueTask.CompletedTask;}}).CreateSession();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await s.ExecuteAsync(new("SLEEP"),cancel.Token));
    }
    [Fact]
    public async Task AudioIsInjectedAndPreservesFrequencyDurationAndMusicState()
    {
        List<PCTone> tones=[];await using var s=new QBasicEngine(new(){Audio=(tone,_)=>{tones.Add(tone);return ValueTask.CompletedTask;}}).CreateSession();
        await s.ExecuteAsync(new("BEEP\nSOUND 440,18.2\nPLAY \"MLO4T120L4\"\nPLAY \"A\""));
        Assert.Equal(3,tones.Count);Assert.Equal(800,tones[0].Frequency);Assert.Equal(1,tones[1].Duration.TotalSeconds,3);
        Assert.Equal(440,tones[2].Frequency,3);Assert.Equal(.5,tones[2].Duration.TotalSeconds,3);
    }
    [Fact]
    public async Task AudioIsNotSilentlyIgnoredWithoutBackend()
    {
        await using var s=new QBasicEngine().CreateSession();var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await s.ExecuteAsync(new("BEEP")));
        Assert.Contains("Audio backend not configured",error.Message);
    }
    [Theory]
    [InlineData(1)][InlineData(2)][InlineData(9)][InlineData(12)][InlineData(13)]
    public async Task GraphicsPrimitivesAndSpriteXorRoundtrip(int mode)
    {
        PCCanvas? frame=null;await using var s=new QBasicEngine(new(){PresentGraphics=f=>frame=f}).CreateSession();
        await s.ExecuteAsync(new($"SCREEN {mode}\nDIM sprite&(200)\nPSET (1,1),1\nLINE (0,0)-(7,7),1,B\nLINE -(3,3),1\nGET (0,0)-(7,7),sprite&\nPUT (20,20),sprite&,XOR\na%=POINT(21,21)\nPUT (20,20),sprite&,XOR\nb%=POINT(21,21)\nCIRCLE (50,50),4,1\nPAINT (50,50),1,1"));
        Assert.Equal((short)1,s.Variables["a%"]);Assert.Equal((short)0,s.Variables["b%"]);Assert.NotNull(frame);Assert.Equal(1,frame.Point(50,50));
    }
    [Fact]
    public async Task OriginalBananaDataUsesExpectedCgaAndEgaHeaderAndColors()
    {
        await using var s=new QBasicEngine().CreateSession();
        await s.ExecuteAsync(new("SCREEN 1\nDIM banana&(2)\nDATA 327686,-252645316,60\nREAD banana&(0),banana&(1),banana&(2)\nPUT (0,0),banana&,PSET\na%=POINT(1,0)"));
        Assert.Equal((short)3,s.Variables["a%"]);
        await s.ExecuteAsync(new("SCREEN 9\nDIM ega&(8)\nDATA 458758,202116096,471604224,943208448,943208448,943208448,471604224,202116096,0\nFOR i=0 TO 8\nREAD ega&(i)\nNEXT\nPUT (0,0),ega&,PSET\nc%=POINT(4,0)"));
        Assert.Equal((short)14,s.Variables["c%"]);
    }
    [Theory]
    [InlineData("DIM a&(0)\nGET (0,0)-(10,10),a&")]
    [InlineData("DIM a%(20)\nPUT (0,0),a%,PSET")]
    [InlineData("DIM a&(1)\nPUT (0,0),a&,PSET")]
    [InlineData("PSET (0,0),16")]
    public async Task InvalidGraphicsCallsAreDiagnostics(string text)
    {
        await using var s=new QBasicEngine().CreateSession();await s.ExecuteAsync(new("SCREEN 9"));
        await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await s.ExecuteAsync(new(text)));
    }
    [Fact]
    public async Task FakeClockAdvancesOriginalGorillasToIntroInputWait()
    {
        string root=Path.Combine(Path.GetTempPath(),"qbs-device-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            long time=0;PCCanvas? frame=null;
            await using var s=new QBasicEngine(new(){BaseDirectory=root,MaximumSteps=100000,Audio=(_,_)=>ValueTask.CompletedTask,Clock=()=>new DateTime(2026,1,1).AddMilliseconds(Interlocked.Add(ref time,20)),PresentGraphics=f=>frame=f}).CreateSession();
            var e=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await s.ExecuteAsync(new(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,"samples","GORILLA.BAS")),"GORILLA.BAS")));
            Assert.Equal("QBS2401",e.Diagnostic.Code);Assert.DoesNotContain("DEF SEG",e.Message);Assert.NotNull(frame);Assert.Equal(9,frame.Mode);Assert.Empty(Directory.GetFiles(root));
        }
        finally{Directory.Delete(root,true);}
    }
}
