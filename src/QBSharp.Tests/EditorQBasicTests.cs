using Screen9;
using QBSharp.Cli;
using QBSharp.Cli.Editor;

namespace QBSharp.Tests;

public sealed class EditorQBasicTests
{
    private static async Task Until(Func<bool> ready)
    {using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));while(!ready())await Task.Delay(10,timeout.Token);}
    [Fact]
    public void EditAcceptsExplicitDialectAndPreservesModernDefault()
    {
        Assert.Equal("qbasic",CommandLine.Parse(["edit","gorillas.bas","--dialect","qbasic"]).Dialect);
        Assert.Equal("qbs",CommandLine.Parse(["edit","gorillas.bas"]).Dialect);
        Assert.Throws<CliUsageException>(()=>CommandLine.Parse(["edit","--dialect","bad"]));
        Assert.Throws<CliUsageException>(()=>CommandLine.Parse(["edit","--dialect","qbasic","--dialect","qbs"]));
        Assert.Throws<CliUsageException>(()=>CommandLine.Parse(["repl","--dialect","qbasic"]));
    }
    [Fact]
    public async Task RunnerExecutesLegacySyntaxAndCapturesFinalFrameWithoutNativeConsole()
    {
        await using var runner=new EditorRunner();
        runner.Start("DEFINT A-Z\nanswer#=6#*7#\nSCREEN 0\nPRINT answer#;",null,dialect:"qbasic");
        var result=await runner.WaitAsync();Assert.Equal("Completed",result.Status);Assert.Equal("qbasic",result.Dialect);Assert.Null(result.Diagnostic);
        Assert.True(runner.TryTakeFrame(out var frame));Assert.NotNull(frame);Assert.Equal('4',frame[1,0].Character);Assert.Equal('2',frame[2,0].Character);
    }
    [Fact]
    public async Task F5UsesSelectedDialectAndF4NavigatesRuntimeDiagnostic()
    {
        var document=new EditorDocument();document.Insert("DEFINT A-Z\nanswer#=1#/0#");
        await using var runner=new EditorRunner();var controller=new EditorController(document,runner,initialDialect:"qbasic");
        await controller.HandleAsync(new(EditorKey.F5));await runner.WaitAsync();controller.Tick();
        Assert.Equal("QBS2400",controller.LastResult?.Diagnostic?.Code);Assert.False(controller.ProgramView);
        await controller.HandleAsync(new(EditorKey.F4));Assert.Equal(1,document.Position.Line);
    }
    [Fact]
    public async Task ProgramViewForwardsKeysWithoutEditingAndF8ReturnsToCode()
    {
        var document=new EditorDocument();document.Insert("SCREEN 0\nPRINT \"WAIT\";\nDO\nk$=INKEY$\nLOOP UNTIL k$=CHR$(0)+CHR$(72)\nPRINT \"DONE\";");
        string original=document.Text;
        await using var runner=new EditorRunner();var controller=new EditorController(document,runner,initialDialect:"qbasic");
        await controller.HandleAsync(new(EditorKey.F5));
        await Until(()=>{controller.Tick();return controller.ProgramFrame is not null;});
        Assert.True(controller.ProgramView);Assert.Contains("QBasic program",string.Concat(EditorFrame.Build(controller).Cells.SelectMany(row=>row).Select(cell=>cell.Character)));
        await controller.HandleAsync(new(EditorKey.Text,"n"));Assert.Equal(original,document.Text);
        await controller.HandleAsync(new(EditorKey.Up));Assert.Equal("Completed",(await runner.WaitAsync()).Status);controller.Tick();
        Assert.Equal(original,document.Text);Assert.False(controller.ProgramView);
        await controller.HandleAsync(new(EditorKey.F8));Assert.True(controller.ProgramView);
        await controller.HandleAsync(new(EditorKey.F8));Assert.False(controller.ProgramView);
    }
    [Fact]
    public async Task InputUsesEditorDialogAndCanBeCanceled()
    {
        var document=new EditorDocument();document.Insert("INPUT n%\nSCREEN 0\nPRINT n%;");
        await using var runner=new EditorRunner();var controller=new EditorController(document,runner,initialDialect:"qbasic");
        await controller.HandleAsync(new(EditorKey.F5));await Until(()=>{controller.Tick();return controller.Dialog is not null;});
        Assert.Equal("QBasic INPUT",controller.Dialog?.Title);
        await controller.HandleAsync(new(EditorKey.Text,"42"));await controller.HandleAsync(new(EditorKey.Enter));
        Assert.Equal("Completed",(await runner.WaitAsync()).Status);controller.Tick();Assert.Equal('4',controller.ProgramFrame?[1,0].Character);
        await controller.HandleAsync(new(EditorKey.F5));await Until(()=>{controller.Tick();return controller.Dialog is not null;});
        await controller.HandleAsync(new(EditorKey.Escape));Assert.Equal("Canceled",(await runner.WaitAsync()).Status);
    }
    [Fact]
    public async Task F6CancelsPollingAndDialectCannotChangeDuringRun()
    {
        var document=new EditorDocument();document.Insert("SCREEN 0\nDO\nk$=INKEY$\nLOOP");
        await using var runner=new EditorRunner();var controller=new EditorController(document,runner,initialDialect:"qbasic");
        await controller.HandleAsync(new(EditorKey.F5));Assert.Throws<InvalidOperationException>(()=>controller.SetDialect("qbs"));
        await controller.HandleAsync(new(EditorKey.F6));Assert.Equal("Canceled",(await runner.WaitAsync()).Status);controller.Tick();
        controller.SetDialect("qbs");Assert.Equal("qbs",controller.Dialect);Assert.Null(controller.ProgramFrame);
    }
    [Fact]
    public async Task DialectMenuDoesNotChangeDocumentAndClassicalAiIsExplicitlyBlocked()
    {
        var document=new EditorDocument();document.Insert("DEFINT A-Z");string text=document.Text;
        await using var runner=new EditorRunner();var controller=new EditorController(document,runner);
        await controller.HandleAsync(new(X:25,Y:0,Click:true));await controller.HandleAsync(new(X:25,Y:6,Click:true));
        Assert.Equal("Language / dialect",controller.Dialog?.Title);
        await controller.HandleAsync(new(EditorKey.Down));await controller.HandleAsync(new(EditorKey.Enter));
        Assert.Equal("qbasic",controller.Dialect);Assert.Equal(text,document.Text);
        await controller.HandleAsync(new(EditorKey.F7));Assert.Contains("QBS only",controller.Dialog?.Text);
    }
    [Fact]
    public void ProgramSnapshotsAreCopiedReadOnlyAndKeysHaveClassicCodes()
    {
        var canvas=new Canvas(2,1);canvas.DrawText(0,0,"A");var frame=EditorProgramFrame.Capture(canvas);canvas.DrawText(0,0,"B");
        Assert.Equal('A',frame[0,0].Character);Assert.Throws<NotSupportedException>(()=>((IList<Cell>)frame.Cells)[0]=default);
        Assert.Equal("\0S",EditorProgramFrame.EncodeKey(new(EditorKey.Delete)));Assert.Equal("\t",EditorProgramFrame.EncodeKey(new(EditorKey.Tab)));
        Assert.Equal("\x1b",EditorProgramFrame.EncodeKey(new(EditorKey.Escape)));Assert.Equal("",EditorProgramFrame.EncodeKey(new(EditorKey.F6)));
        Assert.Equal(EditorKey.F8,EditorInputDecoder.DecodeVt("\x1b[19~")?.Key);
    }
    [Fact]
    public async Task RunnerReusesSessionsWithoutLeakingKeysOrFrames()
    {
        await using var runner=new EditorRunner();runner.Start("SCREEN 0\nPRINT \"A\";",null,dialect:"qbasic");await runner.WaitAsync();
        Assert.True(runner.TryTakeFrame(out _));runner.Start("Print(42)",null);var result=await runner.WaitAsync();
        Assert.Equal("qbs",result.Dialect);Assert.Equal("Completed",result.Status);Assert.False(runner.TryTakeFrame(out _));
    }
    [Fact]
    public async Task GraphicsUsesConsoleLibFrameAndAudioRequiresExplicitHost()
    {
        int tones=0;await using var runner=new EditorRunner(audio:(_,_)=>{tones++;return ValueTask.CompletedTask;});
        runner.Start("SCREEN 9\nLINE (0,0)-(639,349),2,BF\nBEEP",null,dialect:"qbasic");
        Assert.Equal("Completed",(await runner.WaitAsync()).Status);Assert.Equal(1,tones);Assert.True(runner.TryTakeFrame(out var frame));
        Assert.NotNull(frame);Assert.Equal(80,frame.Width);Assert.Equal(25,frame.Height);Assert.NotEqual(ConsoleColor.Black,frame[0,0].Background);
    }
    [Fact]
    public async Task F5RunsQBasicInPixelWindowAndKeepsFinalScreenUntilKey()
    {
        if(!OperatingSystem.IsWindows())return;
        PixelWindow? shown=null;var document=new EditorDocument();document.Insert("SCREEN 9\nLINE (0,0)-(639,349),4,BF\nPRINT \"OK\";");
        await using var runner=new EditorRunner(window:()=>shown=new PixelWindow("QBSharp test"));var controller=new EditorController(document,runner,initialDialect:"qbasic");
        await controller.HandleAsync(new(EditorKey.F5));
        Assert.False(controller.ProgramView);Assert.Contains("QBasic window",controller.Status);
        await Until(()=>{controller.Tick();return controller.Output.Any(line=>line.Contains("Press any key",StringComparison.Ordinal));});
        Assert.True(runner.Running);Assert.NotNull(shown);Assert.False(shown.Closed);Assert.True(shown.CaptureCount>=1);Assert.False(runner.TryTakeFrame(out _));
        await controller.HandleAsync(new(EditorKey.F8));Assert.False(controller.ProgramView); // nothing to show in the terminal
        runner.SendKey(new(EditorKey.Text,"x"));
        Assert.Equal("Completed",(await runner.WaitAsync()).Status);Assert.True(shown.Closed);
    }
}
