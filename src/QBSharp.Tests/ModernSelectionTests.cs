using System.Collections;
using QBSharp.Core.Hosting;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Syntax;
using QBSharp.Cli;
using Xunit;
namespace QBSharp.Tests;
public sealed class ModernSelectionTests
{
    [Theory]
    [InlineData(0,10)] [InlineData(1,20)] [InlineData(2,30)] [InlineData(9,40)]
    public async Task ElseIfBranches(int input,int expected)
    {
        await using var s=new ScriptEngine().CreateSession(); s.SetVariable("x",input);
        await s.ExecuteAsync(new("Dim result=0\nIf x=0 Then\nresult=10\nElseIf x=1 Then\nresult=20\nElseIf x=2 Then\nresult=30\nElse\nresult=40\nEnd If"));
        Assert.Equal(expected,s.Variables["result"]);
    }
    [Theory]
    [InlineData(-1,1)] [InlineData(0,2)] [InlineData(3,3)] [InlineData(7,4)] [InlineData(99,5)]
    public async Task SelectSupportsComparisonListRangeAndElse(int input,int expected)
    {
        await using var s=new ScriptEngine().CreateSession();s.SetVariable("x",input);
        await s.ExecuteAsync(new("Dim result=0\nSelect Case x\nCase Is < 0\nresult=1\nCase 0,1\nresult=2\nCase 2 To 5\nresult=3\nCase 7\nresult=4\nCase Else\nresult=5\nEnd Select"));
        Assert.Equal(expected,s.Variables["result"]);
    }
    [Fact] public async Task HeadersAndCasesAreLazyAndSelectorEvaluatedOnce()
    {
        int calls=0; var b=new HostBindings();b.RegisterFunction("Tick",(_,_)=>{calls++;return ValueTask.FromResult<object?>(2);});
        await using var s=new ScriptEngine().CreateSession(b);
        await s.ExecuteAsync(new("If True Then\nElseIf Tick()=2 Then\nEnd If\nSelect Case Tick()\nCase 2,Tick()\nCase Tick()\nEnd Select"));
        Assert.Equal(1,calls);
    }
    [Fact] public async Task ForEachWorksWithClassesGenericsAndExit()
    {
        await using var s=new ScriptEngine().CreateSession();
        await s.ExecuteAsync(new("Class Counter\nProperty Sum As Integer\nSub AddAll(xs)\nFor Each n As Integer In xs\nIf n=3 Then\nExit For\nEnd If\nMe.Sum=Me.Sum+n\nNext n\nEnd Sub\nEnd Class\nDim xs=New List(Of Integer)()\nxs.Add(1)\nxs.Add(2)\nxs.Add(3)\nDim c=New Counter()\nc.AddAll(xs)\nDim result=c.Sum"));
        Assert.Equal(3,s.Variables["result"]);Assert.False(s.Variables.ContainsKey("n"));
    }
    [Fact] public async Task ExistingCounterCollectionsAndDisposal()
    {
        var sequence=new TrackingSequence();await using var s=new ScriptEngine().CreateSession();s.SetVariable("items",sequence);
        await s.ExecuteAsync(new("Dim n As Integer=0\nDim sum=0\nFor Each n In items\nsum=sum+n\nNext"));
        Assert.Equal(3,s.Variables["sum"]);Assert.Equal(2,s.Variables["n"]);Assert.True(sequence.Disposed);
    }
    [Theory]
    [InlineData("Exit For")]
    [InlineData("Select Case 1\nCase Else\nCase 1\nEnd Select")]
    [InlineData("Select Case 1\nCase Is + 2\nEnd Select")]
    [InlineData("For Each n As Integer In xs\nNext other")]
    [InlineData("If True Then\nElse\nElseIf True Then\nEnd If")]
    public void InvalidGrammarRejected(string source)=>Assert.False(new ScriptParser().ParseScript(new(source)).Success);
    [Theory]
    [InlineData("For Each n As Integer In Nothing\nNext")]
    [InlineData("For Each n As Integer In 1\nNext")]
    [InlineData("For Each n In xs\nNext")]
    [InlineData("For Each n As Integer In xs\nNext",true)]
    [InlineData("If False Then\nElseIf 1 Then\nEnd If")]
    public async Task RuntimeErrorsHavePosition(string source,bool badElement=false)
    {
        await using var s=new ScriptEngine().CreateSession();s.SetVariable("xs",badElement?new object[]{"bad"}:new object[]{1});
        var e=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await s.ExecuteAsync(new(source,"selection.bas")));
        Assert.Equal("selection.bas",e.Diagnostic.Span!.File);
    }
    [Fact] public async Task EnumeratorDisposedOnExitAndFailure()
    {
        foreach(var body in new[]{"Exit For","missing()"}) {
        var sequence=new TrackingSequence();await using var s=new ScriptEngine().CreateSession();s.SetVariable("xs",sequence);
        try {await s.ExecuteAsync(new("For Each n As Integer In xs\n"+body+"\nNext"));}catch(ScriptRuntimeException){}
        Assert.True(sequence.Disposed);}
    }
    [Fact] public void ReplAndCompletion()
    {
        var b=new ReplBuffer(); Assert.False(b.Append("Select Case 1"));Assert.False(b.Append("Case 1"));Assert.True(b.Append("End Select"));
        Assert.Contains("ElseIf",Completion.Repl("ElseI",[]));Assert.Contains("Each",Completion.Keywords);
    }
    private sealed class TrackingSequence:IEnumerable
    {
        public bool Disposed;
        public IEnumerator GetEnumerator()=>new Iterator(this);
        private sealed class Iterator(TrackingSequence owner):IEnumerator,IDisposable
        {private int i;public object Current=>i;public bool MoveNext()=>++i<=2;public void Reset()=>i=0;public void Dispose()=>owner.Disposed=true;}
    }
}
