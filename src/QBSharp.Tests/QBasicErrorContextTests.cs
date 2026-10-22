using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.QBasic;

namespace QBSharp.Tests;

public sealed class QBasicErrorContextTests
{
    [Fact]
    public async Task HandlerFailureReportsBothFailureAndOrigin()
    {
        await using var session=new QBasicEngine().CreateSession();
        var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("ON ERROR GOTO Handler\na=1/0\nGOTO Done\nHandler:\nBEEP\nRESUME NEXT\nDone:","fault.bas")));
        Assert.Equal(5,error.Diagnostic.Span.Start.Line);Assert.Contains("Audio backend",error.Message);Assert.Contains("Division by zero",error.Message);Assert.Contains("fault.bas(2,1)",error.Message);
    }
    [Fact]
    public async Task SuccessfulResumeDoesNotContaminateLaterErrors()
    {
        await using var session=new QBasicEngine().CreateSession();
        await session.ExecuteAsync(new("ON ERROR GOTO Handler\na=1/0\nGOTO Done\nHandler:\nRESUME NEXT\nDone:"));
        var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("bad=1/0","later.bas")));
        Assert.DoesNotContain("Handler failed",error.Message);Assert.Equal("later.bas",error.Diagnostic.Span.File);
    }
    [Fact]
    public async Task GlobalHandlerResumeWithinProcedureClearsItsOrigin()
    {
        await using var session=new QBasicEngine().CreateSession();
        var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("""
            Probe
            GOTO Done
            Handler:
                RESUME NEXT
            Done:
            SUB Probe
                ON ERROR GOTO Handler
                first=1/0
                ON ERROR GOTO 0
                later=1/0
            END SUB
            ""","global-handler.bas")));
        Assert.DoesNotContain("Handler failed",error.Message);Assert.Contains("error 11",error.Message);
    }
    [Fact]
    public async Task GorillasReachesCalibrationAfterVirtualNumLockAndEgaInitialization()
    {
        string root=Path.Combine(Path.GetTempPath(),"qbs-gorillas-qbasic-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            string path=Path.Combine(AppContext.BaseDirectory,"samples","GORILLA.BAS");
            await using var session=new QBasicEngine(new(){BaseDirectory=root,MaximumSteps=10000}).CreateSession();
            var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new(await File.ReadAllTextAsync(path),path)));
            Assert.Equal("QBS2401",error.Diagnostic.Code);
            Assert.Equal(9,Convert.ToInt32(session.Variables["Mode"]));
            Assert.Equal(0,Convert.ToInt32(session.Variables["ScrWidth"])); // This variant calibrates before screen dimensions are set.
            Assert.Equal(32,Convert.ToInt32(await session.EvaluateAsync(new("PEEK(1047)"))));
            Assert.Empty(Directory.GetFiles(root));
        }
        finally{Directory.Delete(root,true);}
    }
}
