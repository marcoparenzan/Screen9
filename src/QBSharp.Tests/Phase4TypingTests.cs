using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;

namespace QBSharp.Tests;

public sealed class Phase4TypingTests
{
    [Fact]
    public async Task MethodParametersReturnsAndPropertyAssignmentEnforceTypes()
    {
        await using var session = new ScriptEngine().CreateSession();
        await session.ExecuteAsync(new("""
            Class Box
                Property Number As Integer
                Function SetAndGet(value As Integer) As Integer
                    Number = value
                    Return Number
                End Function
                Function Wrong() As Integer
                    Return "wrong"
                End Function
            End Class
            Dim boxValue = New Box()
            """));
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(new("boxValue.Number = \"bad\"")));
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.EvaluateAsync(new("boxValue.SetAndGet(\"bad\")")));
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.EvaluateAsync(new("boxValue.Wrong()")));
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(new("boxValue.SetAndGet = Nothing")));
        Assert.Equal(42, await session.EvaluateAsync(new("boxValue.SetAndGet(42)")));
    }
    [Fact]
    public async Task PropertyDefaultsExistBeforeInitializersAndEachConstructionRunsInitializers()
    {
        await using var session = new ScriptEngine().CreateSession(); int count = 0;
        session.RegisterFunction("Tick", (_, _) => ValueTask.FromResult<object?>(++count));
        await session.ExecuteAsync(new("Class C\nProperty First As Integer = Later\nProperty Later As Integer = Tick()\nEnd Class\nDim a = New C()\nDim b = New C()"));
        Assert.Equal(0, await session.EvaluateAsync(new("a.First"))); Assert.Equal(1, await session.EvaluateAsync(new("a.Later"))); Assert.Equal(2, await session.EvaluateAsync(new("b.Later")));
    }
}
