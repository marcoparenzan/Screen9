using QBSharp.Core.Diagnostics;
using QBSharp.QBasic;

namespace QBSharp.Tests;

public sealed class QBasicConsoleInputRegressionTests
{
    [Fact]
    public async Task PlayerNamesThenEmptyPointsAnswerUsesDefault()
    {
        using var input=new StringReader("Marco\nPlayer 2\n\n");
        await using var session=new QBasicEngine(new(){Input=input}).CreateSession();
        await session.ExecuteAsync(new("""
            INPUT "Player 1"; Player1$
            INPUT "Player 2"; Player2$
            INPUT "Play to how many total points (Default = 3)"; game$
            NumGames = VAL(LEFT$(game$, 2))
            IF NumGames = 0 THEN NumGames = 3
            started = 1
            ""","input.bas"));
        Assert.Equal("Marco",session.Variables["Player1$"]);
        Assert.Equal("Player 2",session.Variables["Player2$"]);
        Assert.Equal("",session.Variables["game$"]);
        Assert.Equal(3f,session.Variables["NumGames"]);
        Assert.Equal(1f,session.Variables["started"]);
    }

    [Theory]
    [InlineData("\n", "INPUT value$", "value$", "")]
    [InlineData("   \n", "INPUT value$", "value$", "")]
    [InlineData("\"\"\n", "INPUT value$", "value$", "")]
    [InlineData("\n", "LINE INPUT value$", "value$", "")]
    public async Task EmptyStringAnswersAreNotEof(string text,string script,string name,string expected)
    {
        using var input=new StringReader(text);
        await using var session=new QBasicEngine(new(){Input=input}).CreateSession();
        await session.ExecuteAsync(new(script));
        Assert.Equal(expected,session.Variables[name]);
    }

    [Fact]
    public async Task EmptyNumericAndCommaFieldsUseDefaults()
    {
        using var input=new StringReader("\n,hello,\n");
        await using var session=new QBasicEngine(new(){Input=input}).CreateSession();
        await session.ExecuteAsync(new("INPUT number%\nINPUT first$,middle$,last$"));
        Assert.Equal((short)0,session.Variables["number%"]);
        Assert.Equal("",session.Variables["first$"]);
        Assert.Equal("hello",session.Variables["middle$"]);
        Assert.Equal("",session.Variables["last$"]);
    }

    [Fact]
    public async Task ActualConsoleEofStillFails()
    {
        await using var session=new QBasicEngine(new(){Input=TextReader.Null}).CreateSession();
        var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("INPUT value$")));
        Assert.Contains("error 62",error.Message);
        Assert.Contains("Console input ended",error.Message);
    }

    [Fact]
    public async Task NumberedFileEofRemainsStrict()
    {
        var root=Path.Combine(Path.GetTempPath(),"qbs-eof-"+Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root,"empty.txt"),"");
            await using var session=new QBasicEngine(new(){BaseDirectory=root}).CreateSession();
            var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("OPEN \"empty.txt\" FOR INPUT AS #1\nINPUT #1,value$")));
            Assert.Contains("error 62",error.Message);
        }
        finally { Directory.Delete(root,true); }
    }
}
