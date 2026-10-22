using System.Text;
using Screen9;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.QBasic;

namespace QBSharp.Tests;

public sealed class CurrentGorillaTests
{
    private static string FilePath=>Path.Combine(AppContext.BaseDirectory,"samples","GORILLA.BAS");
    [Fact]
    public async Task CurrentFileParsesEntirelyAndReachesIntroWithVirtualDevices()
    {
        string text=await File.ReadAllTextAsync(FilePath);long clock=0;int tones=0;
        await using var session=new QBasicEngine(new()
        {
            MaximumSteps=100000,AllowFileIO=false,
            Clock=()=>new DateTime(2026,1,1).AddMilliseconds(Interlocked.Add(ref clock,20)),
            Audio=(_,_)=>{tones++;return ValueTask.CompletedTask;}
        }).CreateSession();
        var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new(text,FilePath)));
        Assert.Equal("QBS2401",error.Diagnostic.Code);Assert.True(tones>0);Assert.Equal(9,Convert.ToInt32(session.Variables["Mode"]));
        Assert.Equal(640,Convert.ToInt32(session.Variables["ScrWidth"]));Assert.Equal(32,Convert.ToInt32(await session.EvaluateAsync(new("PEEK(1047)"))));
    }
    [Fact]
    public async Task OneRoundAndExitWithControlledClockAudioAndInput()
    {
        string text=await File.ReadAllTextAsync(FilePath);long clock=0;int tones=0;bool gameOver=false;char? aliceScore=null,bobScore=null;
        var keys=new Queue<string>([""," ","P","","0","\r","","1","\r"," ",""," ","n"]);
        var consumed=new List<string>();using var input=new StringReader("Alice\nBob\n1\n9.8\n");using var output=new StringWriter();
        await using var session=new QBasicEngine(new()
        {
            MaximumSteps=10_000_000,AllowFileIO=false,Input=input,Output=output,
            Clock=()=>new DateTime(2026,1,1).AddMilliseconds(Interlocked.Add(ref clock,20)),
            Audio=(_,_)=>{tones++;return ValueTask.CompletedTask;},
            Delay=(_,token)=>{token.ThrowIfCancellationRequested();return ValueTask.CompletedTask;},
            ReadKey=()=>{string key=keys.Count>0?keys.Dequeue():"";if(consumed.Count<100)consumed.Add(key);return key;},
            PresentText=canvas=>
            {
                if(canvas.Height>=12)
                {
                    string first=new(Enumerable.Range(0,canvas.Width).Select(x=>canvas[x,10].Character).ToArray());
                    string second=new(Enumerable.Range(0,canvas.Width).Select(x=>canvas[x,11].Character).ToArray());
                    if(first.Contains("Alice",StringComparison.Ordinal)&&second.Contains("Bob",StringComparison.Ordinal)){aliceScore=canvas[50,10].Character;bobScore=canvas[50,11].Character;}
                }
                for(int y=0;y<canvas.Height;y++)
                {
                    string row=new(Enumerable.Range(0,canvas.Width).Select(x=>canvas[x,y].Character).ToArray());
                    if(row.Contains("GAME OVER!",StringComparison.Ordinal))gameOver=true;
                }
            }
        }).CreateSession();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try{await session.ExecuteAsync(new(text,FilePath),timeout.Token);}
        catch(ScriptRuntimeException error){throw new Xunit.Sdk.XunitException(error.Message+" / consumed keys: "+string.Join(",",consumed.Select(k=>Convert.ToHexString(Encoding.UTF8.GetBytes(k)))));}
        Assert.True(gameOver);Assert.True(tones>0);Assert.Equal('0',aliceScore);Assert.Equal('1',bobScore); // originale 1990: dopo GAME OVER un tasto e END, nessuna domanda "play again"
        Assert.Equal("Alice",session.Variables["Name1$"]);Assert.Equal("Bob",session.Variables["Name2$"]);
        Assert.Equal(0,Convert.ToInt32(await session.EvaluateAsync(new("PEEK(1047)"))));
    }
}
