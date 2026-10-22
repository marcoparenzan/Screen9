using System.Diagnostics;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.QBasic;
using QBSharp.Cli;
using QBSharp.StandardLibrary;

namespace QBSharp.Tests;

public sealed class QBasicTests
{
    private static IScriptSession Session(TextWriter? output=null,QBasicOptions? options=null,HostBindings? host=null)=>new QBasicEngine(options??new(){Output=output??TextWriter.Null}).CreateSession(host);
    [Fact]
    public async Task SuffixesDefaultsFixedTypesAndFunctionNameReturn()
    {
        await using var session=Session();
        await session.ExecuteAsync(new("""
            DEFINT A-Z
            DIM plain, longValue&, realValue!, doubleValue#, textValue$
            plain = 42
            longValue& = 40000
            realValue! = .5
            doubleValue# = 1# / 3#
            textValue$ = "banana"
            DECLARE FUNCTION Twice# (n AS INTEGER)
            answer# = Twice(plain)
            FUNCTION Twice# (n AS INTEGER)
                Twice# = n * 2
            END FUNCTION
            """));
        Assert.IsType<short>(session.Variables["plain"]); Assert.IsType<int>(session.Variables["longValue&"]);
        Assert.IsType<float>(session.Variables["realValue!"]); Assert.IsType<double>(session.Variables["doubleValue#"]);
        Assert.Equal(84d,session.Variables["answer#"]); Assert.Equal("banana",session.Variables["textValue$"]);
    }
    [Fact]
    public async Task ByRefByValArraysRecordsAndSharedState()
    {
        await using var session=Session();await session.ExecuteAsync(new("""
            DEFINT A-Z
            TYPE Item
                Name AS STRING * 5
                Count AS INTEGER
            END TYPE
            DIM SHARED sharedTotal
            DIM value, list%(1 TO 2), records(0 TO 1) AS Item
            value = 10
            Increment value
            CopyOnly value
            ResizeAndFill list%()
            records(0).Name = "abcdef"
            records(0).Count = value
            records(1) = records(0)
            records(1).Count = 99
            SUB Increment (n AS INTEGER)
                n = n + 1
                sharedTotal = sharedTotal + 1
            END SUB
            SUB CopyOnly (BYVAL n AS INTEGER)
                n = 100
            END SUB
            SUB ResizeAndFill (a%())
                REDIM a%(1 TO 3)
                a%(3) = 42
            END SUB
            """));
        Assert.Equal((short)11,session.Variables["value"]);Assert.Equal((short)1,session.Variables["sharedTotal"]);
        Assert.Equal((short)42,await session.EvaluateAsync(new("list%(3)")));Assert.Equal(3,await session.EvaluateAsync(new("UBOUND(list%())")));
        Assert.Equal("abcde",await session.EvaluateAsync(new("records(0).Name")));Assert.Equal((short)11,await session.EvaluateAsync(new("records(0).Count")));
    }
    [Fact]
    public async Task MultidimensionalBoundsAndTypeOverflowHaveRuntimeDiagnostics()
    {
        await using var session=Session();await session.ExecuteAsync(new("DIM grid%(-1 TO 1, 2 TO 4)\ngrid%(0,3)=7"));
        Assert.Equal((short)7,await session.EvaluateAsync(new("grid%(0,3)")));
        var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.EvaluateAsync(new("grid%(2,3)","array.bas")));Assert.Contains("error 9",error.Message);Assert.Equal("array.bas",error.Diagnostic.Span.File);
        await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("grid%(0,3)=40000")));
    }
    [Fact]
    public async Task ForDoWhileSelectInlineIfLabelsAndGosub()
    {
        await using var session=Session();await session.ExecuteAsync(new("""
            DEFINT A-Z
            total = 0
            FOR i = 5 TO 1 STEP -2
                total = total + i
            NEXT i
            DO: total = total + 1: LOOP UNTIL total = 10
            WHILE total < 12
                total = total + 1
            WEND
            IF total = 12 THEN choice$ = "ok" ELSE choice$ = "bad"
            SELECT CASE total
                CASE 1 TO 5: choice$ = "bad"
                CASE IS >= 12: GOSUB AddMore
                CASE ELSE: choice$ = "bad"
            END SELECT
            GOTO EndMain
            AddMore:
            total = total + 1
            RETURN
            EndMain:
            """));
        Assert.Equal((short)13,session.Variables["total"]);Assert.Equal("ok",session.Variables["choice$"]);
    }
    [Fact]
    public async Task ErrorHandlersResumeNextRetryAndGlobalHandlerFromProcedure()
    {
        await using var session=Session();await session.ExecuteAsync(new("""
            DEFINT A-Z
            DIM SHARED seen
            ON ERROR GOTO Handler
            divisor = 0
            value = 10 / divisor
            completed = 1
            GOTO Finished
            Handler:
                seen = ERR
                divisor = 2
            RESUME
            Finished:
            """));
        Assert.Equal((short)11,session.Variables["seen"]);Assert.Equal((short)5,session.Variables["value"]);Assert.Equal((short)1,session.Variables["completed"]);
        await session.ExecuteAsync(new("""
            Probe
            GOTO Last
            OutsideHandler:
                seen = ERR
            RESUME NEXT
            Last:
            SUB Probe
                ON ERROR GOTO OutsideHandler
                dummy = 1 / 0
                seen = seen + 1
            END SUB
            """));
        Assert.Equal((short)12,session.Variables["seen"]);
    }
    [Fact]
    public async Task DataReadRestoreFunctionsStringsAndDeterministicRandom()
    {
        await using var session=Session();await session.ExecuteAsync(new("""
            DEFINT A-Z
            DATA 42, "banana", -10
            READ n, s$, neg
            RESTORE Extra
            READ later
            RANDOMIZE 7
            first# = RND
            RANDOMIZE 7
            repeat# = RND
            GOTO EndData
            Extra:
            DATA 99
            EndData:
            """));
        Assert.Equal((short)42,session.Variables["n"]);Assert.Equal("banana",session.Variables["s$"]);Assert.Equal((short)-10,session.Variables["neg"]);Assert.Equal((short)99,session.Variables["later"]);
        Assert.Equal(session.Variables["first#"],session.Variables["repeat#"]);
        Assert.Equal("ANA",await session.EvaluateAsync(new("UCASE$(MID$(\"banana\",2,3))")));
        Assert.Equal(4,await session.EvaluateAsync(new("INSTR(4,\"banana\",\"ana\")")));
    }
    [Theory]
    [InlineData("SQR(9)",3d)] [InlineData("INT(-1.2)",-2d)] [InlineData("FIX(-1.2)",-1d)] [InlineData("ABS(-5)",5d)] [InlineData("ATN(1)",0.7853981633974483)]
    public async Task MathBuiltins(string expression,double expected)
    {await using var session=Session();Assert.Equal(expected,Convert.ToDouble(await session.EvaluateAsync(new(expression))),10);}
    [Fact]
    public async Task NumberedFilesWriteInputLineInputEofAndHostPolicy()
    {
        string root=Path.Combine(Path.GetTempPath(),"qbs-basic-io-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            await using var session=Session(options:new(){BaseDirectory=root});await session.ExecuteAsync(new("""
                OPEN "values.txt" FOR OUTPUT AS #1
                WRITE #1, 42, "hello, world", 3.5
                PRINT #1, "last line"
                CLOSE #1
                OPEN "values.txt" FOR INPUT AS #2
                INPUT #2, n%, s$, d#
                LINE INPUT #2, line$
                ended = EOF(2)
                CLOSE
                """));
            Assert.Equal((short)42,session.Variables["n%"]);Assert.Equal("hello, world",session.Variables["s$"]);Assert.Equal(3.5d,session.Variables["d#"]);Assert.Equal("last line",session.Variables["line$"]);
            Assert.Equal(-1f,session.Variables["ended"]);
            await using var denied=Session(options:new(){BaseDirectory=root,AllowFileIO=false});await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await denied.ExecuteAsync(new("OPEN \"no.txt\" FOR OUTPUT AS #1")));Assert.False(File.Exists(Path.Combine(root,"no.txt")));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public async Task FunctionExpressionsAreEvaluatedOnceAndByRefTypesAreChecked()
    {
        await using var session=Session();await session.ExecuteAsync(new("""
            DEFINT A-Z
            DIM SHARED calls
            Consume MakeValue()
            SUB Consume (n AS INTEGER)
                result = n
            END SUB
            FUNCTION MakeValue
                calls = calls + 1
                MakeValue = 42
            END FUNCTION
            """));
        Assert.Equal((short)1,session.Variables["calls"]);
        await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("DIM wrong#\nConsume wrong#")));
    }
    [Fact]
    public async Task PrintFormattingHostBindingsAndPCCanvasCanBeUsedFromDialect()
    {
        using var output=new StringWriter();var host=await new StandardLibraryHost(new(){LoadConfiguration=false}).CreateBindingsAsync();
        await using var session=Session(output,host:host);await session.ExecuteAsync(new("""
            PRINT "A"; "B"
            DIM c AS ANY
            c = PC.Create(9)
            PC.PSet c, 0, 0, 6
            result = PC.Point(c,0,0)
            """));
        Assert.Equal("AB"+Environment.NewLine,output.ToString());Assert.Equal(6f,session.Variables["result"]);
    }
    [Fact]
    public async Task StepLimitsCancellationNoPartialParseAndSessionIsolation()
    {
        await using var session=Session(options:new(){MaximumSteps=20});
        var limit=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("ON ERROR RESUME NEXT\nDO:LOOP")));Assert.Equal("QBS2401",limit.Diagnostic.Code);
        using var cts=new CancellationTokenSource();cts.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await session.ExecuteAsync(new("x=1"),cts.Token));
        await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await session.ExecuteAsync(new("x=7\nFOR i=1 TO 2")));Assert.DoesNotContain("x",session.Variables.Keys);
        await using var other=Session();Assert.Empty(other.Variables);
    }
    [Theory]
    [InlineData("--dialect","wrong")]
    [InlineData("--dialect","qbasic","--dialect","qbasic")]
    public void InvalidDialectArguments(string a,string b,params string[] extra)
    {Assert.Throws<CliUsageException>(()=>CommandLine.Parse(new[]{"run","a.bas",a,b}.Concat(extra).ToArray()));}
    [Fact]
    public async Task RealCliTestFixtureUsesOptInDialect()
    {
        var info=new ProcessStartInfo("dotnet"){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};info.Environment.Remove("QBS_MAX_STEPS");info.Environment["QBS_AI_PROVIDER"]="";
        foreach(var arg in new[]{typeof(CliApplication).Assembly.Location,"test",Path.Combine(AppContext.BaseDirectory,"samples","qbasic-core.bas"),"--dialect","qbasic"})info.ArgumentList.Add(arg);
        using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();process.StandardInput.Close();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));try{await process.WaitForExitAsync(timeout.Token);}finally{if(!process.HasExited)process.Kill(true);}
        Assert.Equal(0,process.ExitCode);Assert.Empty(await errors);Assert.Contains("PASS",await output);
    }
}
