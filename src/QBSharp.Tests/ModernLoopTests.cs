using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.Core.Syntax;
using QBSharp.Cli;

namespace QBSharp.Tests;

public sealed class ModernLoopTests
{
    [Fact]
    public async Task ForCapturesHeadersOnceAndReusesExistingTypedCounter()
    {
        List<int> calls=[];var bindings=new HostBindings();bindings.RegisterFunction("Header",(_,args)=>{calls.Add((int)args[0]!);return ValueTask.FromResult(args[0]);});
        await using var session=new ScriptEngine().CreateSession(bindings);
        await session.ExecuteAsync(new("Dim i As Integer\nDim total=0\nFor i=Header(1) To Header(3) Step Header(1)\nDim item=i\ntotal=total+item\nNext i"));
        Assert.Equal(new[]{1,3,1},calls);Assert.Equal(6,session.Variables["total"]);Assert.Equal(4,session.Variables["i"]);Assert.DoesNotContain("item",session.Variables.Keys);
    }
    [Fact]
    public async Task DeclaredCounterIsLocalAndShadowsWithoutChangingOuterVariable()
    {
        await using var s=new ScriptEngine().CreateSession();
        await s.ExecuteAsync(new("Dim i=99\nDim sum=0\nFor i As Integer=5 To 1 Step -2\nsum=sum+i\nNext I"));
        Assert.Equal(9,s.Variables["sum"]);Assert.Equal(99,s.Variables["i"]);
    }
    [Fact]
    public async Task ForSupportsFloatingStepAndEmptyRange()
    {
        await using var s=new ScriptEngine().CreateSession();await s.ExecuteAsync(new("Dim total=0\nFor x As Double=0 To 1 Step 0.25\ntotal=total+1\nNext\nFor empty As Integer=5 To 1\ntotal=99\nNext"));
        Assert.Equal(5,s.Variables["total"]);Assert.DoesNotContain("x",s.Variables.Keys);
    }
    [Theory]
    [InlineData("Do While n<3\nn=n+1\nLoop",3)]
    [InlineData("Do Until n=3\nn=n+1\nLoop",3)]
    [InlineData("Do\nn=n+1\nLoop While n<3",3)]
    [InlineData("Do\nn=n+1\nLoop Until n=3",3)]
    [InlineData("Do While False\nn=n+1\nLoop",0)]
    [InlineData("Do\nn=n+1\nLoop While False",1)]
    public async Task DoPreAndPostConditionsHaveCorrectIterationCount(string body,int count)
    {
        await using var s=new ScriptEngine().CreateSession();await s.ExecuteAsync(new("Dim n=0\n"+body));Assert.Equal(count,s.Variables["n"]);
    }
    [Fact]
    public async Task DoTailSeesIterationLocalsButTheyDoNotLeak()
    {
        await using var s=new ScriptEngine().CreateSession();await s.ExecuteAsync(new("Dim count=0\nDo\nDim finished As Boolean = count=2\ncount=count+1\nLoop Until finished"));
        Assert.Equal(3,s.Variables["count"]);Assert.DoesNotContain("finished",s.Variables.Keys);
    }
    [Fact]
    public async Task ExitTargetsNearestMatchingLoopKindAndReturnEscapesAllLoops()
    {
        await using var s=new ScriptEngine().CreateSession();await s.ExecuteAsync(new("""
            Dim visits=0
            For i As Integer=1 To 3
                Do
                    visits=visits+1
                    Exit For
                Loop
            Next
            Dim stopped=0
            Do
                For j As Integer=1 To 3
                    stopped=stopped+1
                    Exit Do
                Next
            Loop
            While True
                Exit While
            End While
            Function Find() As Integer
                For index As Integer=1 To 3
                    Do
                        Return index
                    Loop
                Next
                Return 0
            End Function
            Dim found=Find()
            """));
        Assert.Equal(1,s.Variables["visits"]);Assert.Equal(1,s.Variables["stopped"]);Assert.Equal(1,s.Variables["found"]);
    }
    [Fact]
    public async Task LoopsWorkInsideClassesWithPropertiesAndClrCollections()
    {
        await using var s=new ScriptEngine().CreateSession();await s.ExecuteAsync(new("""
            Namespace Ops
                Class Counter
                    Property Total As Integer
                    Function Sum(n As Integer) As Integer
                        Dim values=New List(Of Integer)()
                        For i As Integer=1 To n
                            values.Add(i)
                            Total=Total+i
                        Next
                        Return Total
                    End Function
                End Class
            End Namespace
            Dim counter=New Ops.Counter()
            Dim answer=counter.Sum(3)
            """));
        Assert.Equal(6,s.Variables["answer"]);
    }
    [Theory]
    [InlineData("For i As Integer=1 To 3 Step 0\nNext")]
    [InlineData("For i=1 To 3\nNext")]
    [InlineData("For i As String=1 To 3\nNext")]
    [InlineData("For i As Integer=1 To 3 Step 0.5\nNext")]
    [InlineData("For i As Integer=2147483647 To 2147483647\nNext")]
    [InlineData("Do While 1\nLoop")]
    [InlineData("Do\nLoop Until 1")]
    public async Task InvalidRuntimeCasesHaveLocatedDiagnostics(string source)
    {
        await using var s=new ScriptEngine().CreateSession();var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await s.ExecuteAsync(new(source,"loop.bas")));
        Assert.Equal("QBS2001",error.Diagnostic.Code);Assert.Equal("loop.bas",error.Diagnostic.Span.File);
    }
    [Theory]
    [InlineData("For i As Integer=1 To 3\nNext other")]
    [InlineData("For i As Integer=1 To 3\nEnd For")]
    [InlineData("Exit For")]
    [InlineData("Exit Do")]
    [InlineData("Exit While")]
    [InlineData("Do While True\nLoop Until False")]
    [InlineData("For i As Integer=1 To 3\nFunction F()\nExit For\nEnd Function\nNext")]
    [InlineData("Do\nEnd Do")]
    public void InvalidGrammarAndCrossFunctionExitAreRejected(string source)
    {var parsed=new ScriptParser().ParseScript(new(source,"invalid.bas"));Assert.False(parsed.Success);Assert.Equal("QBS1100",parsed.Diagnostics[0].Code);}
    [Fact]
    public async Task NonFiniteAndNonAdvancingCountersFailWithoutInfiniteLoop()
    {
        var bindings=new HostBindings();bindings.SetValue("Big",1e30);bindings.SetValue("Bad",double.PositiveInfinity);
        await using var s=new ScriptEngine().CreateSession(bindings);
        var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await s.ExecuteAsync(new("For i As Double=Big To Big Step 1\nNext")));Assert.Contains("advance",error.Message);
        await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await s.ExecuteAsync(new("For i As Double=0 To Bad\nNext")));
    }
    [Theory]
    [InlineData("Do\nLoop")][InlineData("For i As Long=1 To 1000000\nNext")]
    public async Task EmptyLoopsHonorBudgetAndSessionRemainsUsable(string source)
    {
        await using var s=new ScriptEngine(options:new(){MaximumSteps=20}).CreateSession();var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await s.ExecuteAsync(new(source)));
        Assert.Equal("QBS2002",error.Diagnostic.Code);await s.ExecuteAsync(new("Dim value=42"));Assert.Equal(42,s.Variables["value"]);
    }
    [Fact]
    public async Task CancellationFromHostInLoopPropagatesAndNoPartialParseRuns()
    {
        using var cancel=new CancellationTokenSource();int calls=0;var bindings=new HostBindings();bindings.RegisterFunction("Tick",(_,_)=>{calls++;cancel.Cancel();return ValueTask.FromResult<object?>(null);});
        await using var s=new ScriptEngine().CreateSession(bindings);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await s.ExecuteAsync(new("Do\nTick()\nLoop"),cancel.Token));Assert.Equal(1,calls);
        await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await s.ExecuteAsync(new("Tick()\nFor i As Integer=1 To 3")));Assert.Equal(1,calls);
    }
    [Fact]
    public void ReplTracksLoopTerminatorsAndCompletionIncludesNewKeywords()
    {
        var repl=new ReplBuffer();Assert.False(repl.Append("For i As Integer=1 To 3"));Assert.False(repl.Append("Do"));Assert.False(repl.Append("Exit Do"));Assert.False(repl.Append("Loop"));Assert.True(repl.Append("Next"));
        Assert.Contains("Until",Completion.Repl("Un",[]));
    }
    [Fact]
    public void AstContainsNewStructuredNodesAndSourcePositions()
    {
        var parsed=new ScriptParser().ParseScript(new("For i As Integer=1 To 2\nDo\nExit Do\nLoop\nNext","ast.bas"));Assert.True(parsed.Success);
        var loop=Assert.IsType<ForStatement>(parsed.Root!.Statements[0]);Assert.Equal("Integer",loop.CounterType);Assert.Equal(1,loop.Span.Start.Line);
        Assert.IsType<DoStatement>(loop.Body.Statements[0]);
    }
}
