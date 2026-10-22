using Screen9;
using Canvas = Screen9.Canvas;
using Microsoft.Extensions.Configuration;
using QBSharp.AI;
using QBSharp.Cli;
using QBSharp.StandardLibrary;
using Spectre.Console;

namespace QBSharp.Tests;

public sealed class QBasicTerminalOutputTests
{
    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override long GetTimestamp()=>ticks;
        public void Advance(int ms)=>ticks+=TimeSpan.FromMilliseconds(ms).Ticks;
    }
    [Fact]
    public void ScopedOutputRestoresScreenAndCursorExactlyOnce()
    {
        using var writer=new StringWriter();var terminal=new QBasicTerminalOutput(writer,()=>(80,25),configureNative:false);
        var canvas=new Canvas(8,2);canvas.DrawText(0,0,"HELLO");terminal.Present(canvas);terminal.Dispose();string text=writer.ToString();terminal.Dispose();
        Assert.Equal(text,writer.ToString());Assert.StartsWith("\x1b[?1049h",text,StringComparison.Ordinal);Assert.Contains("HELLO",text,StringComparison.Ordinal);
        Assert.EndsWith("\x1b[0m\x1b[?7h\x1b[?25h\x1b[?1049l",text,StringComparison.Ordinal);
    }
    [Fact]
    public void DeferredFrameRefreshesOnPollingAndFlushesOnExit()
    {
        using var writer=new StringWriter();var clock=new Clock();using var terminal=new QBasicTerminalOutput(writer,()=>(80,25),false,clock);
        var first=new Canvas(8,1);first.DrawText(0,0,"FIRST");terminal.Present(first);
        var second=new Canvas(8,1);second.DrawText(0,0,"SECOND");terminal.Present(second);Assert.DoesNotContain("SECOND",writer.ToString(),StringComparison.Ordinal);
        clock.Advance(34);terminal.Refresh();Assert.Contains("SECOND",writer.ToString(),StringComparison.Ordinal);
        var last=new Canvas(8,1);last.DrawText(0,0,"LAST");terminal.Present(last);terminal.Dispose();Assert.Contains("LAST",writer.ToString(),StringComparison.Ordinal);
    }
    [Fact]
    public void TextGlyphMappingAndViewportClippingPreventControlInjection()
    {
        using var writer=new StringWriter();using var terminal=new QBasicTerminalOutput(writer,()=>(3,1),false);
        var canvas=new Canvas(8,2);canvas.DrawText(0,0,"ÄÍºMORE");terminal.Present(canvas);
        Assert.Contains("─═║",writer.ToString(),StringComparison.Ordinal);Assert.DoesNotContain("MORE",writer.ToString(),StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("SCREEN 0\nPRINT \"OK\";",0)]
    [InlineData("SCREEN 0\nDO\nLOOP",4)]
    [InlineData("SCREEN 0\nPRINT 1/0",4)]
    public async Task CliRestoresBeforeReportingSuccessOrError(string source,int expected)
    {
        using var writer=new StringWriter();var console=AnsiConsole.Create(new AnsiConsoleSettings{Ansi=AnsiSupport.No,Out=new AnsiConsoleOutput(writer)});
        var library=new StandardLibraryHost(new(){LoadConfiguration=false});var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["QBS_AI_PROVIDER"]=""}).Build();
        var app=new CliApplication(console,console,new StringReader(source),interactive:true,configuration:config,standardLibrary:library,ralfHost:RalfHost.FromConfiguration(config),
            qbasicTerminalFactory:()=>new QBasicTerminalOutput(writer,()=>(80,25),false));
        Assert.Equal(expected,await app.RunAsync(["run","-","--dialect","qbasic","--max-steps","30"]));
        string output=writer.ToString();int restoration=output.IndexOf("\x1b[?1049l",StringComparison.Ordinal);Assert.True(restoration>=0);
        if(expected==4)Assert.True(output.IndexOf("QBS240",StringComparison.Ordinal)>restoration);
    }
}
