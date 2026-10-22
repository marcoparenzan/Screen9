using Screen9;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.QBasic;

namespace QBSharp.Tests;

public sealed class QBasicTextRasterTests
{
    [Fact]
    public async Task TextCellsLocateColorsAndOmittedArguments()
    {
        Canvas? frame=null;using var output=new StringWriter();
        await using var session=new QBasicEngine(new(){PresentText=c=>frame=c,Output=output}).CreateSession();
        await session.ExecuteAsync(new("SCREEN 0\nWIDTH 20,5\nCOLOR 12,1\nLOCATE 2,3\nPRINT \"Hi\";\nCOLOR ,2\nLOCATE ,8\nPRINT \"!\";"));
        Assert.NotNull(frame);Assert.Equal('H',frame[2,1].Character);Assert.Equal('i',frame[3,1].Character);
        Assert.Equal(ConsoleColor.Red,frame[2,1].Foreground);Assert.Equal(ConsoleColor.DarkBlue,frame[2,1].Background);
        Assert.Equal('!',frame[7,1].Character);Assert.Equal(ConsoleColor.DarkGreen,frame[7,1].Background);Assert.Equal("",output.ToString());
    }
    [Fact]
    public async Task TabContinuationUsesActualColumnAfterWrap()
    {
        Canvas? frame=null;await using var session=new QBasicEngine(new(){PresentText=c=>frame=c}).CreateSession();
        await session.ExecuteAsync(new("WIDTH 4,3\nPRINT \"ABCDE\";\nPRINT TAB(3);\"Z\";"));
        Assert.NotNull(frame);Assert.Equal('E',frame[0,1].Character);Assert.Equal('Z',frame[2,1].Character);
    }
    [Fact]
    public async Task ViewPrintScrollPreservesRowsOutsideRegionAndResetUsesFullScreen()
    {
        Canvas? frame=null;await using var session=new QBasicEngine(new(){PresentText=c=>frame=c}).CreateSession();
        await session.ExecuteAsync(new("WIDTH 8,5\nLOCATE 1,1\nPRINT \"TOP\";\nLOCATE 5,1\nPRINT \"BOTTOM\";\nVIEW PRINT 2 TO 3\nPRINT \"one\"\nPRINT \"two\"\nPRINT \"three\";"));
        Assert.NotNull(frame);Assert.Equal('T',frame[0,0].Character);Assert.Equal('t',frame[0,1].Character);Assert.Equal('w',frame[1,1].Character);
        Assert.Equal('t',frame[0,2].Character);Assert.Equal('h',frame[1,2].Character);Assert.Equal('B',frame[0,4].Character);
        await session.ExecuteAsync(new("VIEW PRINT\nPRINT \"R\";"));Assert.Equal('R',frame[0,0].Character);
    }
    [Theory]
    [InlineData(1,8)][InlineData(2,8)][InlineData(7,8)][InlineData(8,8)][InlineData(9,14)][InlineData(11,16)][InlineData(12,16)][InlineData(13,8)]
    public async Task RasterFontWritesExpectedPixelsAndOverwritesWithSpace(int mode,int cellHeight)
    {
        PCCanvas? frame=null;await using var session=new QBasicEngine(new(){PresentGraphics=c=>frame=c,RasterFont=QBasicRasterFont.Compact}).CreateSession();
        await session.ExecuteAsync(new($"SCREEN {mode}\nCOLOR 1,0\nLOCATE 2,2\nPRINT \"A\";"));
        Assert.NotNull(frame);Assert.Equal(1,frame.Point(10,cellHeight));Assert.Equal(0,frame.Point(9,cellHeight));
        Assert.Equal(0,frame.Point(8,cellHeight));Assert.Equal(0,frame.Point(10,0));
        await session.ExecuteAsync(new("LOCATE 2,2\nPRINT \" \";"));Assert.Equal(0,frame.Point(10,cellHeight));
    }
    [Theory]
    [InlineData("Ä")][InlineData("Í")][InlineData("³")][InlineData("º")][InlineData("Û")][InlineData("☃")]
    public async Task ExtendedGlyphsAndFallbackAreBounded(string glyph)
    {
        PCCanvas? frame=null;await using var session=new QBasicEngine(new(){PresentGraphics=c=>frame=c,RasterFont=QBasicRasterFont.Compact}).CreateSession();
        await session.ExecuteAsync(new($"SCREEN 9\nCOLOR 1\nPRINT \"{glyph}\";"));
        Assert.NotNull(frame);Assert.Contains(frame.PixelSnapshot(),v=>v==1);Assert.Equal(0,frame.Point(8,0));
    }
    [Fact]
    public async Task Cls2ClearsOnlyTextViewportPixelsAndResetsCursor()
    {
        PCCanvas? frame=null;await using var session=new QBasicEngine(new(){PresentGraphics=c=>frame=c,RasterFont=QBasicRasterFont.Compact}).CreateSession();
        await session.ExecuteAsync(new("SCREEN 9\nPSET (1,1),2\nPSET (1,15),2\nPSET (1,29),2\nVIEW PRINT 2 TO 2\nCLS 2"));
        Assert.NotNull(frame);Assert.Equal(2,frame.Point(1,1));Assert.Equal(0,frame.Point(1,15));Assert.Equal(2,frame.Point(1,29));
        await session.ExecuteAsync(new("COLOR 1\nPRINT \"A\";"));Assert.Equal(1,frame.Point(2,14));
    }
    [Fact]
    public async Task Cls1ClearsPixelsButPreservesTextCellsAndCursor()
    {
        PCCanvas? graphic=null;Canvas? text=null;
        await using var session=new QBasicEngine(new(){PresentGraphics=c=>graphic=c,PresentText=c=>text=c,RasterFont=QBasicRasterFont.Compact}).CreateSession();
        await session.ExecuteAsync(new("SCREEN 9\nCOLOR 1\nLOCATE 2,3\nPRINT \"A\";\nCLS 1\nPRINT \"B\";"));
        Assert.NotNull(graphic);Assert.Equal(0,graphic.Point(18,14));Assert.Equal(1,graphic.Point(25,14));
        // A text callback is not sent in graphics mode; verify saved cell state indirectly via cursor/raster continuation.
        Assert.Null(text);
    }
    [Theory]
    [InlineData("CLS")][InlineData("CLS 0")]
    public async Task ClsDefaultClearsWholeFrameEvenWithTextViewport(string command)
    {
        PCCanvas? frame=null;await using var session=new QBasicEngine(new(){PresentGraphics=c=>frame=c,RasterFont=QBasicRasterFont.Compact}).CreateSession();
        await session.ExecuteAsync(new($"SCREEN 9\nPSET (1,1),2\nPSET (1,300),2\nVIEW PRINT 2 TO 3\n{command}"));
        Assert.NotNull(frame);Assert.All(frame.PixelSnapshot(),v=>Assert.Equal((byte)0,v));
    }
    [Theory]
    [InlineData("VIEW PRINT 0 TO 3")][InlineData("VIEW PRINT 4 TO 2")][InlineData("VIEW PRINT 1 TO 26")]
    [InlineData("LOCATE 0,1")][InlineData("LOCATE 1,81")][InlineData("CLS 3")][InlineData("WIDTH 301,25")]
    public async Task InvalidTextCommandsKeepFileLineColumn(string statement)
    {
        await using var session=new QBasicEngine().CreateSession();
        var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("SCREEN 0\n"+statement,"raster.bas")));
        Assert.Equal("QBS2400",error.Diagnostic.Code);Assert.Equal("raster.bas",error.Diagnostic.Span.File);Assert.Equal(2,error.Diagnostic.Span.Start.Line);
    }
    [Fact]
    public async Task RejectedWidthAndColorDoNotPoisonLaterCommands()
    {
        Canvas? frame=null;await using var session=new QBasicEngine(new(){PresentText=c=>frame=c}).CreateSession();
        await session.ExecuteAsync(new("WIDTH 4,3\nCOLOR 2,1"));
        await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("WIDTH 0,3")));
        await session.ExecuteAsync(new("WIDTH ,3"));Assert.NotNull(frame);Assert.Equal(4,frame.Width);
        await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("COLOR 16,0")));
        await session.ExecuteAsync(new("PRINT \"X\";"));Assert.Equal(ConsoleColor.DarkGreen,frame[0,0].Foreground);Assert.Equal(ConsoleColor.DarkBlue,frame[0,0].Background);
    }
    [Fact]
    public async Task CancellationAfterHostExpressionStopsRasterWrite()
    {
        using var cancellation=new CancellationTokenSource();var bindings=new HostBindings();
        bindings.RegisterFunction("CancelText",(_,_)=>{cancellation.Cancel();return ValueTask.FromResult<object?>("A");});
        await using var session=new QBasicEngine().CreateSession(bindings);
        await session.ExecuteAsync(new("SCREEN 9"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await session.ExecuteAsync(new("PRINT CancelText();"),cancellation.Token));
    }
    [Fact]
    public async Task GraphicsScrollMovesPixelsOnlyWithinTextViewport()
    {
        PCCanvas? frame=null;await using var session=new QBasicEngine(new(){PresentGraphics=c=>frame=c,RasterFont=QBasicRasterFont.Compact}).CreateSession();
        await session.ExecuteAsync(new("SCREEN 9\nPSET (1,1),2\nPSET (1,28),3\nPSET (1,42),2\nVIEW PRINT 2 TO 3\nPRINT\nPRINT"));
        Assert.NotNull(frame);Assert.Equal(2,frame.Point(1,1));Assert.Equal(3,frame.Point(1,14));Assert.Equal(0,frame.Point(1,28));Assert.Equal(2,frame.Point(1,42));
    }
    [Fact]
    public async Task ScreenSwitchResetsColorsAfterColoredTextMode()
    {
        PCCanvas? frame=null;await using var session=new QBasicEngine(new(){PresentGraphics=c=>frame=c,RasterFont=QBasicRasterFont.Compact}).CreateSession();
        await session.ExecuteAsync(new("SCREEN 0\nCOLOR 7,12\nSCREEN 1\nPRINT \"A\";"));
        Assert.NotNull(frame);Assert.Equal(0,frame.Point(0,0));
    }
}
