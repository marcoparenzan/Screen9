using Screen9;
using QBSharp.Cli;
using QBSharp.QBasic;

namespace QBSharp.Tests;

/// <summary>ScreenInput: INPUT/LINE INPUT con prompt ed eco sullo schermo QBasic, tasti da ReadKey (finestra unica).</summary>
public sealed class ScreenInputTests
{
    private static string Row(Canvas canvas,int y)=>new string(Enumerable.Range(0,canvas.Width).Select(x=>canvas[x,y].Character).ToArray()).TrimEnd();
    private static (QBasicEngine Engine,Func<Canvas?> Frame,StringWriter Output) Create(params string[] keys)
    {
        Canvas? frame=null;var queue=new Queue<string>(keys);var output=new StringWriter();
        var engine=new QBasicEngine(new()
        {
            ScreenInput=true,Output=output,PresentText=c=>frame=c,
            ReadKey=()=>queue.Count>0?queue.Dequeue():throw new InvalidOperationException("No more keys."),
            Delay=(_,_)=>ValueTask.CompletedTask
        });
        return (engine,()=>frame,output);
    }
    [Fact]
    public async Task InputEchoesPromptQuestionMarkAndKeysOnScreen()
    {
        var (engine,frame,output)=Create("","A","l","x","\b","i","\r","4","2","\r");
        await using var session=engine.CreateSession();
        await session.ExecuteAsync(new("INPUT \"Name\"; n$\nINPUT \"Age\", a%\nPRINT n$; a%"));
        Assert.Equal("Name? Ali",Row(frame()!,0));Assert.Equal("Age42",Row(frame()!,1));Assert.Equal("Ali 42",Row(frame()!,2));
        Assert.Equal("Ali",session.Variables["n$"]);Assert.Equal("",output.ToString());
    }
    [Fact]
    public async Task LineInputHasNoQuestionMarkAndBareInputHasOne()
    {
        var (engine,frame,_)=Create("a",",","b","\r","7","\r");
        await using var session=engine.CreateSession();
        await session.ExecuteAsync(new("LINE INPUT \"Text: \"; t$\nINPUT v"));
        Assert.Equal("Text: a,b",Row(frame()!,0));Assert.Equal("? 7",Row(frame()!,1));Assert.Equal("a,b",session.Variables["t$"]);
    }
    [Fact]
    public async Task BackspaceAtLineStartGoesToPreviousRowAndNotBeforePrompt()
    {
        // a in colonna 39, b in 40, c va a capo; due Backspace cancellano c e poi b sulla riga precedente.
        var (engine,frame,_)=Create("\b","a","b","c","\b","\b","\b","\b","z","\r");
        await using var session=engine.CreateSession();
        await session.ExecuteAsync(new("WIDTH 40,25\nLOCATE 1,39\nLINE INPUT \"\"; s$"));
        Assert.Equal("z",session.Variables["s$"]);Assert.Equal('z',frame()![38,0].Character);Assert.Equal(' ',frame()![39,0].Character);Assert.Equal(' ',frame()![0,1].Character);
    }
}
