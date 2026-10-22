using System.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.Cli;
using QBSharp.Cli.Editor;
using QBSharp.AI;

namespace QBSharp.Tests;

public sealed class ModernLoopIntegrationTests
{
    [Fact]
    public async Task DecimalLoopUsesCheckedDecimalValuesAndIterationClosuresAreIndependent()
    {
        var bindings=new HostBindings();bindings.SetValue("BeginValue",0.1m);bindings.SetValue("EndValue",0.3m);bindings.SetValue("StepValue",0.1m);
        await using var s=new ScriptEngine().CreateSession(bindings);
        await s.ExecuteAsync(new("""
            Dim total As Decimal
            For value As Decimal=BeginValue To EndValue Step StepValue
                total=total+value
            Next
            Dim callbacks=New List(Of Object)()
            For i As Integer=1 To 2
                Dim snapshot=i
                Function Read() As Integer
                    Return snapshot
                End Function
                callbacks.Add(Read)
            Next
            Dim first=callbacks.get_Item(0)()
            Dim second=callbacks.get_Item(1)()
            """));
        Assert.Equal(0.6m,s.Variables["total"]);Assert.Equal(1,s.Variables["first"]);Assert.Equal(2,s.Variables["second"]);
    }
    private sealed class Client:IRalfClient
    {
        public string Guide="";
        public ValueTask<string> CompleteAsync(RalfRequest request,CancellationToken token)
        {Guide=request.SystemPrompt;return ValueTask.FromResult("For i As Integer=1 To 2\nDo\nExit Do\nLoop\nNext");}
    }
    [Fact]
    public async Task OfflineAiGuideAndSyntaxGateUnderstandModernLoops()
    {
        var client=new Client();var service=new RalfService(client,new RalfSettings{Provider="openai",Model="fake",ApiKey="offline"});
        await using var ai=new EditorAiWorkflow(service);ai.Start("generate a loop","",null);
        var result=await ai.WaitAsync();Assert.NotNull(result.Proposal);Assert.Contains("For counter",client.Guide);Assert.DoesNotContain("No For/Do",client.Guide);
    }
    [Fact]
    public async Task EditorModernDefaultRunsClassesWithLoops()
    {
        await using var runner=new EditorRunner();runner.Start("""
            Class Job
                Function Total() As Integer
                    Dim result=0
                    For i As Integer=1 To 3
                        result=result+i
                    Next
                    Return result
                End Function
            End Class
            Dim instance=New Job()
            Print(instance.Total())
            """,null);
        Assert.Equal("Completed",(await runner.WaitAsync()).Status);
        List<string> output=[];while(runner.TryReadOutput(out var line))output.Add(line!);Assert.Contains("6",output);
    }
    [Theory]
    [InlineData("test")][InlineData("repl")]
    public async Task RealCliRunsModernLoopsAndImportFixture(string command)
    {
        var info=new ProcessStartInfo("dotnet"){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        info.Environment.Remove("QBS_MAX_STEPS");info.Environment["QBS_AI_PROVIDER"]="";
        info.ArgumentList.Add(typeof(CliApplication).Assembly.Location);info.ArgumentList.Add(command);
        if(command=="test")info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"samples","modern-loops.bas"));
        using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        if(command=="repl")await process.StandardInput.WriteAsync("Dim total=0\nFor i As Integer=1 To 3\ntotal=total+i\nNext\nDo While total<8\ntotal=total+1\nLoop\ntotal\n:quit\n");
        process.StandardInput.Close();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try{await process.WaitForExitAsync(timeout.Token);}finally{if(!process.HasExited)process.Kill(true);}
        Assert.Equal(0,process.ExitCode);Assert.Empty(await errors);Assert.Contains(command=="test"?"PASS":"8",await output);
    }
}
