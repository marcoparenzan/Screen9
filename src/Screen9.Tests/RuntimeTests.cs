namespace Screen9.Tests;

public sealed class RuntimeTests
{
    private static readonly Cell Ink = new('*', ConsoleColor.Yellow, ConsoleColor.Blue);
    [Fact] public void EllipseFilledOutlineAndDegenerateClipping()
    {
        var canvas = new Canvas(9,9); canvas.DrawEllipse(4,4,3,2,Ink);
        Assert.Equal(Ink,canvas[1,4]); Assert.Equal(Ink,canvas[7,4]); Assert.Equal(Ink,canvas[4,2]); Assert.Equal(Cell.Empty,canvas[4,4]);
        canvas.DrawEllipse(4,4,3,2,Ink,true); Assert.Equal(Ink,canvas[4,4]);
        canvas.Clear();canvas.DrawEllipse(1,1,0,2,Ink);Assert.Equal(Ink,canvas[1,0]);Assert.Equal(Ink,canvas[1,3]);
        Assert.Throws<ArgumentOutOfRangeException>(()=>canvas.DrawEllipse(0,0,-1,2,Ink));
        canvas.Clear();canvas.DrawEllipse(int.MaxValue,int.MaxValue,int.MaxValue,int.MaxValue,Ink);Assert.All(canvas.Snapshot(),c=>Assert.Equal(Cell.Empty,c));
    }
    [Fact] public void RegionCopyBlitTransparencyAndExtremeCoordinates()
    {
        var canvas = new Canvas(4,4);canvas.FillRectangle(0,0,2,2,Ink);var region=canvas.Capture(0,0,2,2);
        canvas.Clear();canvas.Blit(region,3,3);Assert.Equal(Ink,canvas[3,3]);var copy=region.Snapshot();copy[0]=Cell.Empty;Assert.Equal(Ink,region[0,0]);
        canvas.Blit(region,int.MinValue,int.MinValue);canvas.Blit(region,int.MaxValue,int.MaxValue);
        Assert.Throws<ArgumentException>(()=>canvas.Capture(3,3,2,2));
        var blank = new Canvas(1,1).Capture(0,0,1,1);canvas.Blit(blank,3,3,' ');Assert.Equal(Ink,canvas[3,3]);
    }
    [Fact] public void RegionValidatesAndCopiesSource()
    {
        Cell[] source=[Ink];var region=new CanvasRegion(1,1,source);source[0]=Cell.Empty;Assert.Equal(Ink,region[0,0]);
        Assert.Throws<ArgumentException>(()=>new CanvasRegion(2,2,source));Assert.Throws<ArgumentOutOfRangeException>(()=>region[1,0]);
    }
    [Fact] public async Task DialogsAndLiteralOutput()
    {
        using var writer=new StringWriter();var text=new TextConsole(writer,new StringReader("\n2\n1,2\n"),true);
        Assert.Equal("default",await text.InputBoxAsync("Name","default"));Assert.Equal(new[]{"b"},await text.SelectAsync("Pick",new[]{"a","b"}));
        Assert.Equal(new[]{"a","b"},await text.SelectAsync("Many",new[]{"a","b"},true));
        text.Window("[red]", "literal [red] and \u001b[2J");text.Table(new[]{"name"},new IReadOnlyList<string>[]{new[]{"[red]"}});text.Progress(100);
        Assert.Contains("[red]",writer.ToString());Assert.DoesNotContain('\u001b',writer.ToString());
    }
    [Fact] public async Task RedirectedDialogsDoNotConsumeInputAndCancellationWorks()
    {
        using var input=new StringReader("unused");using var writer=new StringWriter();var text=new TextConsole(writer,input);
        await Assert.ThrowsAsync<InvalidOperationException>(async()=>await text.InputBoxAsync("Name"));Assert.Equal("unused",input.ReadLine());
        Assert.Equal("OK",await text.MessageBoxAsync("Title","Message"));
        using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await text.MessageBoxAsync("Title","Message",cancel.Token));
        Assert.Throws<ArgumentOutOfRangeException>(()=>text.Progress(double.NaN));
    }
    [Fact] public void TextRendererHasNoAnsiAndRespectsPlacement()
    {
        var canvas=new Canvas(3,2);canvas.DrawText(0,0,"ABC\nDEF");using var output=new StringWriter();var renderer=new TextCanvasRenderer(output,new(5,3));renderer.Render(canvas,1,1);
        Assert.Equal(Environment.NewLine+" ABC"+Environment.NewLine+" DEF"+Environment.NewLine,output.ToString());
        Assert.Throws<ArgumentException>(()=>renderer.Render(canvas,3,0));
    }
}