using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.QBasic;

namespace QBSharp.Tests;

public sealed class QBasicRegressionTests
{
    private static IScriptSession Session(QBasicOptions? options=null)=>new QBasicEngine(options).CreateSession();
    [Fact]
    public async Task RedimRetainsDeclaredElementTypeIncludingEmptyDynamicArrays()
    {
        await using var s=Session();await s.ExecuteAsync(new("DIM a(0 TO 1) AS INTEGER\nREDIM a(2 TO 4)\na(2)=7\nDIM empty() AS LONG\nREDIM empty(1 TO 2)"));
        Assert.Equal("INTEGER",Assert.IsType<QBasicArray>(s.Variables["a"]).ElementType);
        Assert.IsType<short>(await s.EvaluateAsync(new("a(2)")));
        Assert.Equal("LONG",Assert.IsType<QBasicArray>(s.Variables["empty"]).ElementType);
        await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await s.ExecuteAsync(new("REDIM a(0 TO 1) AS STRING")));
    }
    [Fact]
    public async Task TypedArraySuffixAliasesResolveToTheSameStorage()
    {
        await using var s=Session();await s.ExecuteAsync(new("DIM a(0 TO 1) AS INTEGER\na%(1)=42"));
        Assert.Equal((short)42,await s.EvaluateAsync(new("a(1)")));Assert.Equal((short)42,await s.EvaluateAsync(new("a%(1)")));
    }
    [Fact]
    public async Task ArrayByRefChecksElementTypeAndPreservesTypedAlias()
    {
        await using var s=Session();await s.ExecuteAsync(new("""
            DIM a(1 TO 2) AS INTEGER
            Fill a()
            SUB Fill (p() AS INTEGER)
                REDIM p(1 TO 3)
                p(3)=42
            END SUB
            """));
        Assert.Equal("INTEGER",Assert.IsType<QBasicArray>(s.Variables["a"]).ElementType);
        Assert.Equal((short)42,await s.EvaluateAsync(new("a(3)")));
        await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await s.ExecuteAsync(new("DIM bad(1 TO 2) AS DOUBLE\nFill bad()")));
    }
    [Fact]
    public async Task ConstantsPassedToProceduresUseTemporaryCells()
    {
        await using var s=Session();await s.ExecuteAsync(new("""
            DEFINT A-Z
            DIM SHARED seen
            CONST limit=3
            Update limit
            SUB Update (n AS INTEGER)
                n=n+1
                seen=n
            END SUB
            """));
        Assert.Equal((short)4,s.Variables["seen"]);Assert.Equal((short)3,s.Variables["limit"]);
        await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await s.ExecuteAsync(new("limit=99")));
    }
    [Fact]
    public async Task NullByRefArrayElementDoesNotEvaluateIndexTwice()
    {
        await using var s=Session();await s.ExecuteAsync(new("""
            DEFINT A-Z
            DIM SHARED calls
            DIM slots(0 TO 0) AS ANY
            Consume slots(NextIndex())
            FUNCTION NextIndex
                calls=calls+1
                NextIndex=0
            END FUNCTION
            SUB Consume (value AS ANY)
            END SUB
            """));
        Assert.Equal((short)1,s.Variables["calls"]);
    }
    [Fact]
    public async Task ReadAndRecordAssignmentEvaluateTargetsOnce()
    {
        await using var s=Session();await s.ExecuteAsync(new("""
            DEFINT A-Z
            DIM SHARED calls
            TYPE Item
                Value AS INTEGER
            END TYPE
            DIM values(0 TO 1) AS INTEGER, items(0 TO 1) AS Item
            DATA 42
            READ values(NextIndex())
            items(NextIndex()).Value=7
            FUNCTION NextIndex
                calls=calls+1
                NextIndex=0
            END FUNCTION
            """));
        Assert.Equal((short)2,s.Variables["calls"]);Assert.Equal((short)42,await s.EvaluateAsync(new("values(0)")));Assert.Equal((short)7,await s.EvaluateAsync(new("items(0).Value")));
    }
    [Fact]
    public async Task HostUpdatesPreserveDeclaredTypes()
    {
        await using var s=Session();await s.ExecuteAsync(new("DIM n AS INTEGER\nSUB Inc (value AS INTEGER)\nvalue=value+1\nEND SUB"));
        s.SetVariable("n",7);await s.ExecuteAsync(new("Inc n"));Assert.Equal((short)8,s.Variables["n"]);
        Assert.ThrowsAny<Exception>(()=>s.SetVariable("n",40000));Assert.Equal((short)8,s.Variables["n"]);
    }
    [Theory]
    [InlineData("CSNG(1E300#)")]
    [InlineData("1E30! * 1E30!")]
    public async Task SingleOverflowCannotEscapeAsInfinity(string expression)
    {
        await using var s=Session();var error=await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await s.EvaluateAsync(new(expression)));
        Assert.Contains("error 6",error.Message);
    }
    [Fact]
    public async Task NestedLoopsExitAndCaseSelectorEvaluateOnce()
    {
        await using var s=Session();await s.ExecuteAsync(new("""
            DEFINT A-Z
            DIM SHARED calls
            total=0
            DO WHILE total<100
                FOR i=1 TO 4
                    total=total+1
                    IF i=2 THEN EXIT FOR
                NEXT i
                EXIT DO
            LOOP
            SELECT CASE Pick()
                CASE 1,2: selected=10
                CASE 3 TO 4: selected=20
                CASE ELSE: selected=30
            END SELECT
            FUNCTION Pick
                calls=calls+1
                Pick=3
            END FUNCTION
            """));
        Assert.Equal((short)2,s.Variables["total"]);Assert.Equal((short)20,s.Variables["selected"]);Assert.Equal((short)1,s.Variables["calls"]);
    }
    [Fact]
    public async Task GosubReturnLabelResumeLabelAndErrorInHandlerAreNotSwallowed()
    {
        await using var s=Session();await s.ExecuteAsync(new("""
            DEFINT A-Z
            value=0
            GOSUB Work
            GOTO EndMain
            Work:
                value=value+1
                RETURN After
            After:
                value=value+1
                GOTO EndMain
            EndMain:
            ON ERROR GOTO Recover
            invalid=1/0
            skipped=99
            GOTO Finished
            Recover:
                code=ERR
                RESUME Finished
            Finished:
            """));
        Assert.Equal((short)2,s.Variables["value"]);Assert.Equal((short)11,s.Variables["code"]);Assert.DoesNotContain("skipped",s.Variables.Keys);
        await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await s.ExecuteAsync(new("ON ERROR GOTO Bad\nx=1/0\nGOTO EndBad\nBad:\ny=1/0\nRESUME NEXT\nEndBad:")));
    }
    [Fact]
    public async Task ReadIntoMixedTypesRestoreAndOutOfDataAreChecked()
    {
        await using var s=Session();await s.ExecuteAsync(new("DIM n AS LONG\nDATA 42, \"word\"\nREAD n,text$\nRESTORE\nREAD again#"));
        Assert.Equal(42,s.Variables["n"]);Assert.Equal("word",s.Variables["text$"]);Assert.Equal(42d,s.Variables["again#"]);
        await Assert.ThrowsAsync<ScriptRuntimeException>(async()=>await s.ExecuteAsync(new("DATA 1\nREAD a,b")));
    }
    [Fact]
    public async Task NumberedFilesAppendQuotedFieldsAndErrorResumeNext()
    {
        string root=Path.Combine(Path.GetTempPath(),"qbs-io-regression-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            await using var s=Session(new(){BaseDirectory=root});await s.ExecuteAsync(new("""
                OPEN "data.txt" FOR OUTPUT AS #1
                WRITE #1, "", "a""b, c", -2.5
                CLOSE #1
                OPEN "data.txt" FOR APPEND AS #1
                PRINT #1, "tail"
                CLOSE #1
                OPEN "data.txt" FOR INPUT AS #2
                INPUT #2, empty$, quoted$, number#
                LINE INPUT #2, tail$
                ended=EOF(2)
                ON ERROR RESUME NEXT
                OPEN "absent.txt" FOR INPUT AS #3
                completed=42
                CLOSE
                """));
            Assert.Equal("",s.Variables["empty$"]);Assert.Equal("a\"b, c",s.Variables["quoted$"]);Assert.Equal(-2.5d,s.Variables["number#"]);
            Assert.Equal("tail",s.Variables["tail$"]);Assert.Equal(-1f,s.Variables["ended"]);Assert.Equal(42f,s.Variables["completed"]);
            Assert.Single(Directory.GetFiles(root));
        }
        finally{Directory.Delete(root,true);}
    }
}
