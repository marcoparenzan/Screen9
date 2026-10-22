using System.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.Core.Diagnostics;
using QBSharp.Cli;
using QBSharp.Cli.Editor;
using QBSharp.AI;
namespace QBSharp.Tests;
public sealed class ModernSelectionIntegrationTests
{
    [Fact] public async Task ReturnAndNestedSelectionEscapeLoops()
    {
        await using var s=new ScriptEngine().CreateSession();
        await s.ExecuteAsync(new("Function F(xs) As Integer\nFor Each n As Integer In xs\nSelect Case n\nCase 1\nIf False Then\nElseIf True Then\nReturn n\nEnd If\nEnd Select\nNext\nReturn 0\nEnd Function\nDim xs=New List(Of Integer)()\nxs.Add(1)\nDim result=F(xs)"));
        Assert.Equal(1,s.Variables["result"]);
    }
    [Fact] public async Task EmptySequenceAndOrdinalStrings()
    {
        await using var s=new ScriptEngine().CreateSession();s.SetVariable("xs",Array.Empty<int>());
        await s.ExecuteAsync(new("For Each n As Integer In xs\nmissing()\nNext\nDim result=0\nSelect Case \"A\"\nCase \"a\"\nresult=1\nCase \"A\"\nresult=2\nEnd Select"));
        Assert.Equal(2,s.Variables["result"]);
    }
    [Fact] public async Task CancellationAndBudgetStayActive()
    {
        using var cts=new CancellationTokenSource(); var b=new HostBindings();b.SetValue("xs",new[]{1,2});
        b.RegisterFunction("StopNow",(_,_)=>{cts.Cancel();return ValueTask.FromResult<object?>(null);});
        await using var s=new ScriptEngine().CreateSession(b);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await s.ExecuteAsync(new("For Each n As Integer In xs\nStopNow()\nNext"),cts.Token));
        await using var limited=new ScriptEngine(options:new(){MaximumSteps=10}).CreateSession(b);
        await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await limited.ExecuteAsync(new("For Each n As Integer In xs\nDo\nLoop\nNext")));
    }
    [Fact] public async Task ImportCanExposeMethodsWithNewConstructs()
    {
        var dir=Path.Combine(Path.GetTempPath(),"qbs-selection-"+Guid.NewGuid());Directory.CreateDirectory(dir);
        try {
            await File.WriteAllTextAsync(Path.Combine(dir,"helper.bas"),"Function Choose(x)\nSelect Case x\nCase 1\nReturn 42\nCase Else\nReturn 0\nEnd Select\nEnd Function");
            await using var s=new ScriptEngine().CreateSession();await s.ExecuteAsync(new("Import \"helper\" As H\nDim result=H.Choose(1)",Path.Combine(dir,"main.bas")));
            Assert.Equal(42,s.Variables["result"]);
        }finally{Directory.Delete(dir,true);}
    }
    [Fact] public async Task EditorRunsNewConstructs()
    {
        await using var r=new EditorRunner();r.Start("Dim xs=New List(Of Integer)()\nxs.Add(1)\nFor Each n As Integer In xs\nSelect Case n\nCase 1\nPrint(42)\nEnd Select\nNext",null);
        Assert.Equal("Completed",(await r.WaitAsync()).Status);
    }
    [Theory] [InlineData("test")] [InlineData("repl")]
    public async Task RealCli(string command)
    {
        var info=new ProcessStartInfo("dotnet"){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.Environment.Remove("QBS_MAX_STEPS");info.Environment["QBS_AI_PROVIDER"]="";
        info.ArgumentList.Add(typeof(CliApplication).Assembly.Location);info.ArgumentList.Add(command);
        if(command=="test")info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"samples","modern-selection.bas"));
        using var p=Process.Start(info)!;var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();
        if(command=="repl")await p.StandardInput.WriteAsync("Dim value=0\nSelect Case 2\nCase 1\nvalue=1\nCase Else\nvalue=42\nEnd Select\nvalue\n:quit\n");
        p.StandardInput.Close();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try{await p.WaitForExitAsync(timeout.Token);}finally{if(!p.HasExited)p.Kill(true);}
        Assert.Equal(0,p.ExitCode);Assert.Empty(await errors);Assert.Contains(command=="test"?"PASS":"42",await output);
    }
    private sealed class Client:IRalfClient
    {
        public string Guide="";
        public ValueTask<string> CompleteAsync(RalfRequest request,CancellationToken token)
        {Guide=request.SystemPrompt;return ValueTask.FromResult("Select Case 1\nCase 1\nIf False Then\nElseIf True Then\nEnd If\nEnd Select");}
    }
    [Fact] public async Task AiGuideMatchesParserWithoutCloud()
    {
        var client=new Client();var service=new RalfService(client,new RalfSettings{Provider="openai",Model="offline",ApiKey="offline"});
        await using var ai=new EditorAiWorkflow(service);ai.Start("selection","",null);Assert.NotNull((await ai.WaitAsync()).Proposal);
        Assert.Contains("For Each",client.Guide);Assert.DoesNotContain("No ForEach/SelectCase/ElseIf",client.Guide);
    }
}
