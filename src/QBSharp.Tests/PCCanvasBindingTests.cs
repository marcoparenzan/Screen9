using Screen9;
using System.Diagnostics;
using QBSharp.Cli;
using QBSharp.Cli.Editor;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.StandardLibrary;

namespace QBSharp.Tests;

public sealed class PCCanvasBindingTests
{
    private sealed class Renderer : ICanvasRenderer
    {
        public Canvas? Frame; public int Calls;
        public CanvasSize GetViewportSize()=>new(80,25);
        public void Render(Canvas canvas,int x=0,int y=0){Frame=canvas;Calls++;}
    }
    [Fact]
    public async Task WrapperUsesPackagedDerivedCanvasAndAllBasicGraphics()
    {
        var renderer=new Renderer();
        await using var session=new ScriptEngine().CreateSession(await new StandardLibraryHost(new(){LoadConfiguration=false},renderer:renderer).CreateBindingsAsync());
        await session.ExecuteAsync(new("""
            Dim c = PC.Create(9)
            PC.Clear(c, 0)
            PC.Line(c, -100, 0, 700, 0, 1)
            PC.Box(c, 10, 10, 5, 5, 2)
            Dim painted = PC.Paint(c, 12, 12, 3, 2)
            PC.Ellipse(c, 30, 30, 5, 3, 4, True)
            PC.Circle(c, 50, 50, 5.0, 5)
            Dim sprite = PC.Get(c, 10, 10, 5, 5)
            PC.Put(c, sprite, 100, 100, "copy")
            PC.Palette(c, 1, 63)
            Dim rgb = PC.Rgb(c, 1)
            Dim frame = PC.Preview(c, 80, 25)
            PC.Render(c)
            """));
        Assert.IsType<PCCanvas>(session.Variables["c"]); Assert.IsNotAssignableFrom<Canvas>(session.Variables["c"]); // Screen9 2.x: PCCanvas non è un Canvas
        Assert.Equal(9,session.Variables["painted"]); Assert.Equal(16777215,session.Variables["rgb"]);
        Assert.Equal(3,await session.EvaluateAsync(new("PC.Point(c, 102, 102)")));
        Assert.Equal(4,await session.EvaluateAsync(new("PC.Point(c, 30, 30)")));
        Assert.Equal(5,await session.EvaluateAsync(new("PC.Point(c, 55, 50)")));
        Assert.Equal(-1,await session.EvaluateAsync(new("PC.Point(c, -1, 0)")));
        Assert.Equal(1,renderer.Calls); Assert.Equal((80,25),(renderer.Frame!.Width,renderer.Frame.Height));
        Assert.Equal(ConsoleColor.White,renderer.Frame[0,0].Background);
        Assert.Equal(8,await session.EvaluateAsync(new("PC.Modes().Count")));
    }
    [Fact]
    public async Task CgaVgaPagesMemorySnapshotsAndRasterAliasesAreAvailable()
    {
        await using var session=new ScriptEngine().CreateSession(await new StandardLibraryHost(new(){LoadConfiguration=false}).CreateBindingsAsync());
        await session.ExecuteAsync(new("""
            Dim cga = PC.Create(1)
            PC.CgaPalette(cga, 0, True, 4)
            PC.WriteByte(cga, 8192, 27)
            Dim ega = PC.Create(7)
            PC.Active(ega, 1)
            PC.Visual(ega, 1)
            PC.PSet(ega, 0, 0, 6)
            Dim image = PC.Get(ega, 0, 0, 1, 1)
            PC.Put(ega, image, 1, 1, "pset")
            PC.Put(ega, image, 1, 1, "xor")
            Dim vga = PC.Create(13)
            PC.Dac(vga, 255, 63, 0, 21)
            PC.WriteByte(vga, 63999, 255)
            Dim bytes = PC.Snapshot(vga)
            """));
        Assert.Equal(3,await session.EvaluateAsync(new("PC.Point(cga, 3, 1)")));
        Assert.Equal(1,await session.EvaluateAsync(new("PC.Active(ega)"))); Assert.Equal(1,await session.EvaluateAsync(new("PC.Visual(ega)")));
        Assert.Equal(0,await session.EvaluateAsync(new("PC.Point(ega, 1, 1)"))); Assert.Equal(255,await session.EvaluateAsync(new("PC.ReadByte(vga, 63999)")));
        Assert.Equal(16711765,await session.EvaluateAsync(new("PC.Rgb(vga, 255)")));
        var bytes=Assert.IsType<int[]>(session.Variables["bytes"]); bytes[63999]=0;
        Assert.Equal(255,await session.EvaluateAsync(new("PC.Point(vga, 319, 199)")));
        await session.ExecuteAsync(new("PC.ResetPalette(vga)")); Assert.Equal(255,await session.EvaluateAsync(new("PC.Point(vga,319,199)")));
    }
    [Theory]
    [InlineData("PC.Create(0)")] [InlineData("PC.Create(3)")]
    [InlineData("PC.PSet(c,0,0,16)")] [InlineData("PC.Palette(c,0,64)")]
    [InlineData("PC.Dac(c,0,63,0,0)")] [InlineData("PC.WriteByte(c,-1,0)")]
    [InlineData("PC.WriteByte(c,0,256)")] [InlineData("PC.ReadByte(c,0,4)")]
    [InlineData("PC.Circle(c,0,0,-1,1)")] [InlineData("PC.Paint(c,0,0,1,99)")]
    [InlineData("PC.Active(c,2)")] [InlineData("PC.Get(c,-1,0,1,1)")]
    [InlineData("PC.Put(c,1,0,0)")] [InlineData("PC.Put(c,img,0,0,\"wrong\")")]
    [InlineData("PC.Preview(c,301,1)")] [InlineData("PC.Render(c)")]
    [InlineData("c.VideoMode.Linear = True")]
    public async Task InvalidCallsRetainSourceDiagnostics(string expression)
    {
        await using var session=new ScriptEngine().CreateSession(await new StandardLibraryHost(new(){LoadConfiguration=false}).CreateBindingsAsync());
        await session.ExecuteAsync(new("Dim c = PC.Create(9)\nDim img = PC.Get(c,0,0,1,1)"));
        var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new(expression,"pc.bas")));
        Assert.Equal("QBS2001",error.Diagnostic.Code); Assert.Equal("pc.bas",error.Diagnostic.Span.File);
    }
    [Fact]
    public async Task ProjectionUsesVisualPageAndCancellationDoesNotRender()
    {
        var renderer=new Renderer();
        await using var session=new ScriptEngine().CreateSession(await new StandardLibraryHost(new(){LoadConfiguration=false},renderer:renderer).CreateBindingsAsync());
        await session.ExecuteAsync(new("Dim c = PC.Create(7)\nPC.PSet(c,0,0,4)\nPC.Active(c,1)\nPC.PSet(c,0,0,2)"));
        Assert.Equal('4',Assert.IsType<string[]>(await session.EvaluateAsync(new("PC.Ascii(c,1,1)")))[0][0]);
        await session.ExecuteAsync(new("PC.Visual(c,1)")); Assert.Equal('2',Assert.IsType<string[]>(await session.EvaluateAsync(new("PC.Ascii(c,1,1)")))[0][0]);
        using var cts=new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await session.EvaluateAsync(new("PC.Render(c)"),cts.Token)); Assert.Equal(0,renderer.Calls);
    }
    [Fact]
    public async Task EditorUsesReducedConsoleLibFrameWithoutNativeSideEffects()
    {
        await using var runner=new EditorRunner(); runner.Start("Dim c = PC.Create(13)\nPC.PSet(c,0,0,255)\nPrint(PC.Point(c,0,0))\nPC.Render(c,5,2)",null);
        Assert.Equal("Completed",(await runner.WaitAsync()).Status); Assert.True(runner.TryReadOutput(out var text)); Assert.Equal("255",text);
    }
    [Theory]
    [InlineData("run","pc-canvas.bas","PCCanvas EGA 640x350 colors=16")]
    [InlineData("test","pc-canvas-test.bas","PASS")]
    public async Task SamplesExecuteThroughRealCliProcesses(string command,string file,string expected)
    {
        var info=new ProcessStartInfo("dotnet"){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.Environment["QBS_AI_PROVIDER"]=""; info.Environment.Remove("QBS_MAX_STEPS");
        foreach(var arg in new[]{typeof(CliApplication).Assembly.Location,command,Path.Combine(AppContext.BaseDirectory,"samples",file)})info.ArgumentList.Add(arg);
        using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();process.StandardInput.Close();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try{await process.WaitForExitAsync(timeout.Token);}finally{if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}}
        Assert.Equal(0,process.ExitCode); Assert.Empty(await errors); Assert.Contains(expected,await output);
    }
}
