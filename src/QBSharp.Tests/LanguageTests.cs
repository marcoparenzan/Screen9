using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.Core.Syntax;

namespace QBSharp.Tests;

public sealed class LanguageTests
{
    private static ScriptSource Source(string text) => new(text, "test.bas");
    [Theory]
    [InlineData("2 + 3 * 4", 14)]
    [InlineData("(2 + 3) * 4", 20)]
    [InlineData("10 - 3 - 2", 5)]
    [InlineData("17 \\ 3", 5)]
    [InlineData("17 Mod 3", 2)]
    [InlineData("+2", 2)]
    [InlineData("-2", -2)]
    [InlineData("2 * -3", -6)]
    public async Task IntegralArithmeticPreservesClrInteger(string code, int expected)
    {
        await using var session = new ScriptEngine().CreateSession();
        Assert.Equal(expected, Assert.IsType<int>(await session.EvaluateAsync(Source(code))));
    }
    [Theory]
    [InlineData("2 ^ 3 ^ 2", 512d)]
    [InlineData("-2 ^ 2", -4d)]
    [InlineData("2 ^ -2", 0.25d)]
    [InlineData("5 / 2", 2.5d)]
    [InlineData("1.25 + 2.5", 3.75d)]
    [InlineData("5.5 Mod 2", 1.5d)]
    public async Task FloatingOperators(string code, double expected)
    {
        await using var session = new ScriptEngine().CreateSession();
        Assert.Equal(expected, Assert.IsType<double>(await session.EvaluateAsync(Source(code))));
    }
    [Theory]
    [InlineData("1 = 1.0", true)]
    [InlineData("1 <> 2", true)]
    [InlineData("1 < 2", true)]
    [InlineData("1 <= 1", true)]
    [InlineData("2 > 1", true)]
    [InlineData("2 >= 2", true)]
    [InlineData("Not 1 = 2", true)]
    [InlineData("True And False", false)]
    [InlineData("False Or True", true)]
    [InlineData("True Or False And False", true)]
    [InlineData("Nothing = Nothing", true)]
    [InlineData("\"a\" < \"b\"", true)]
    [InlineData("\"A\" = \"a\"", false)]
    public async Task BooleanAndComparisonOperators(string code, bool expected)
    {
        await using var session = new ScriptEngine().CreateSession();
        Assert.Equal(expected, Assert.IsType<bool>(await session.EvaluateAsync(Source(code))));
    }
    [Fact]
    public async Task ClrTypesDefaultsConversionsAndDynamicVariables()
    {
        await using var session = new ScriptEngine().CreateSession();
        await session.ExecuteAsync(Source("""
            Dim i As Integer
            Dim l As Long = 2
            Dim d As Double = 3
            Dim money As Decimal = 4
            Dim flag As Boolean
            Dim text As String
            Dim date As Date
            Dim obj As Object = Nothing
            Dim dynamic = 1
            dynamic = "changed"
            """));
        Assert.Equal(0, session.Variables["i"]);
        Assert.IsType<long>(session.Variables["l"]);
        Assert.IsType<double>(session.Variables["d"]);
        Assert.Equal(4m, session.Variables["money"]);
        Assert.Equal(false, session.Variables["flag"]);
        Assert.Null(session.Variables["text"]);
        Assert.Equal(default(DateTime), session.Variables["date"]);
        Assert.Null(session.Variables["obj"]);
        Assert.Equal("changed", session.Variables["dynamic"]);
        Assert.Equal(6m, await session.EvaluateAsync(Source("money + 2")));
        Assert.Equal(3L, await session.EvaluateAsync(Source("l + 1")));
        Assert.Equal("q\"bs:4", await session.EvaluateAsync(Source("\"q\"\"bs:\" & money")));
    }
    [Fact]
    public async Task NestedBranchesWhileFunctionsAndRecursion()
    {
        await using var session = new ScriptEngine().CreateSession();
        await session.ExecuteAsync(Source("""
            Dim total As Integer = 0
            Dim n As Integer = 4
            While n > 0
                If n > 2 Then
                    total = total + n
                Else
                    If n = 2 Then
                        total = total + 10
                    End If
                End If
                n = n - 1
            End While
            Function Factorial(n As Integer) As Integer
                If n <= 1 Then
                    Return 1
                End If
                Return n * Factorial(n - 1)
            End Function
            Dim answer = Factorial(5)
            """));
        Assert.Equal(17, session.Variables["total"]);
        Assert.Equal(120, session.Variables["answer"]);
        Assert.Equal(0, session.Variables["n"]);
    }
    [Fact]
    public async Task FunctionsAreLexicalAndDeclareInExecutionOrder()
    {
        await using var session = new ScriptEngine().CreateSession();
        await session.ExecuteAsync(Source("""
            Dim x = 10
            Function Outer(x As Integer)
                Function Inner()
                    x = x + 1
                    Return x
                End Function
                Return Inner
            End Function
            Dim f = Outer(20)
            Dim first = f()
            Dim second = f()
            """));
        Assert.Equal(21, session.Variables["first"]);
        Assert.Equal(22, session.Variables["second"]);
        Assert.Equal(10, session.Variables["x"]);
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.EvaluateAsync(Source("Inner()")));
    }
    [Fact]
    public async Task VoidAndEmptyFunctionsAndBlocks()
    {
        await using var session = new ScriptEngine().CreateSession();
        await session.ExecuteAsync(Source("If True Then\nElse\nEnd If\nFunction Empty()\nEnd Function\nFunction Stop()\nReturn\nEnd Function"));
        Assert.Null(await session.EvaluateAsync(Source("Empty()")));
        Assert.Null(await session.EvaluateAsync(Source("Stop()")));
        await session.ExecuteAsync(Source(""));
    }
    [Theory]
    [InlineData("False AndAlso Fail()", false, 0)]
    [InlineData("True OrElse Fail()", true, 0)]
    [InlineData("True AndAlso Fail()", true, 1)]
    [InlineData("False OrElse Fail()", true, 1)]
    [InlineData("False And Fail()", false, 1)]
    [InlineData("True Or Fail()", true, 1)]
    public async Task ShortCircuitVersusEager(string code, bool expected, int calls)
    {
        int count = 0; await using var session = new ScriptEngine().CreateSession();
        session.RegisterFunction("Fail", (_, _) => { count++; return ValueTask.FromResult<object?>(true); });
        Assert.Equal(expected, await session.EvaluateAsync(Source(code))); Assert.Equal(calls, count);
    }
    [Theory]
    [InlineData("1 / 0")]
    [InlineData("1 Mod 0")]
    [InlineData("1 \\ 0")]
    [InlineData("1.5 \\ 1")]
    [InlineData("2147483647 + 1")]
    [InlineData("9223372036854775807 + 1")]
    [InlineData("\"x\" + \"y\"")]
    [InlineData("Not 1")]
    [InlineData("missing")]
    [InlineData("1()")]
    [InlineData("2 ^ 100000")]
    public async Task RuntimeErrorsHaveLocationsAndInnerExceptions(string code)
    {
        await using var session = new ScriptEngine().CreateSession();
        var error = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.EvaluateAsync(new(code, "bad.bas")));
        Assert.Equal("QBS2001", error.Diagnostic.Code);
        Assert.Equal("bad.bas", error.Diagnostic.Span.File);
        Assert.Equal(1, error.Diagnostic.Span.Start.Line); Assert.NotNull(error.InnerException);
    }
    [Theory]
    [InlineData("Dim x As Integer = 1.5")]
    [InlineData("Dim x As Integer = Nothing")]
    [InlineData("Dim x As Integer = \"1\"")]
    [InlineData("Dim x As Unknown")]
    [InlineData("Dim x = 1\nDim X = 2")]
    [InlineData("unknown = 1")]
    [InlineData("If 1 Then\nEnd If")]
    [InlineData("Function F(x As Integer)\nReturn x\nEnd Function\nF()")]
    [InlineData("Function F() As Integer\nReturn \"x\"\nEnd Function\nF()")]
    [InlineData("Function F() As Integer\nEnd Function\nF()")]
    public async Task RuntimeRejectsInvalidStatements(string code)
    {
        await using var session = new ScriptEngine().CreateSession();
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(Source(code)));
    }
    [Fact]
    public async Task BindingsSnapshotsSessionsHostFunctionsAndCaseInsensitivity()
    {
        var bindings = new HostBindings(); bindings.SetValue("x", 2);
        bindings.RegisterFunction("Twice", (context, args) => { context.CancellationToken.ThrowIfCancellationRequested(); return ValueTask.FromResult<object?>((int)args[0]! * 2); });
        var engine = new ScriptEngine(); await using var a = engine.CreateSession(bindings); await using var b = engine.CreateSession(bindings);
        bindings.SetValue("x", 99);
        var snapshot = a.Variables; await a.ExecuteAsync(Source("X = twice(x)"));
        Assert.Equal(4, a.Variables["x"]); Assert.Equal(2, b.Variables["x"]); Assert.Equal(2, snapshot["x"]);
        a.SetVariable("host", new List<int> { 1 }); Assert.IsType<List<int>>(a.Variables["host"]);
        await a.ExecuteAsync(Source("Dim typed As Integer = 1"));
        Assert.Throws<InvalidOperationException>(() => a.SetVariable("typed", "bad"));
        Assert.Throws<ArgumentException>(() => a.SetVariable("bad.name", 1));
    }
    [Fact]
    public async Task AsyncHostCancellationAndConcurrentExecutionGuard()
    {
        await using var session = new ScriptEngine().CreateSession();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.RegisterFunction("Wait", async (context, _) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, context.CancellationToken); return null; });
        using var cts = new CancellationTokenSource(); var execution = session.EvaluateAsync(Source("Wait()"), cts.Token).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.ExecuteAsync(Source("")));
        Assert.Throws<InvalidOperationException>(() => session.SetVariable("x", 1));
        Assert.Throws<InvalidOperationException>(() => session.Variables);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await session.DisposeAsync());
        cts.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
        Assert.Equal(2, await session.EvaluateAsync(Source("1 + 1")));
    }
    [Fact]
    public async Task ExecutionLimitsCancellationAndDisposal()
    {
        await using var limited = new ScriptEngine(options: new() { MaximumSteps = 50, MaximumCallDepth = 4 }).CreateSession();
        var step = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await limited.ExecuteAsync(Source("While True\nEnd While")));
        Assert.Equal("QBS2002", step.Diagnostic.Code);
        await limited.ExecuteAsync(Source("Function Recurse()\nReturn Recurse()\nEnd Function"));
        var recursion = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await limited.EvaluateAsync(Source("Recurse()")));
        Assert.Equal("QBS2003", recursion.Diagnostic.Code);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await limited.ExecuteAsync(Source(""), canceled.Token));
        await limited.DisposeAsync(); await limited.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await limited.EvaluateAsync(Source("1")));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScriptEngine(options: new() { MaximumSteps = 0 }));
    }
    [Fact]
    public async Task LoopChecksCancellationFromHost()
    {
        using var cts = new CancellationTokenSource(); await using var session = new ScriptEngine().CreateSession();
        int calls = 0;
        session.RegisterFunction("Tick", (_, _) => { if (++calls == 10) cts.Cancel(); return ValueTask.FromResult<object?>(true); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await session.ExecuteAsync(Source("While Tick()\nEnd While"), cts.Token));
        Assert.Equal(10, calls);
    }
    [Fact]
    public async Task NoPartialExecutionOnParseFailureAndNoRollbackOnRuntimeFailure()
    {
        await using var session = new ScriptEngine().CreateSession();
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(Source("Dim x = 1\nDim = 2")));
        Assert.Empty(session.Variables);
        var error = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(Source("Dim x = 1\nx = missing")));
        Assert.Equal(1, session.Variables["x"]); Assert.Equal(2, error.Diagnostic.Span.Start.Line); Assert.Equal(5, error.Diagnostic.Span.Start.Column);
    }
    [Fact]
    public async Task CultureInvariantLiteralsAndFormatting()
    {
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = new("it-IT"); await using var session = new ScriptEngine().CreateSession(); Assert.Equal("1.5", await session.EvaluateAsync(Source("\"\" & 1.5"))); }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Fact]
    public async Task DiRegistrationIsIdempotentAndProvidesEngine()
    {
        var services = new ServiceCollection(); services.AddQBSharp().AddQBSharp(); Assert.Equal(3, services.Count);
        using var provider = services.BuildServiceProvider(); await using var session = provider.GetRequiredService<IScriptEngine>().CreateSession();
        Assert.Equal(42, await session.EvaluateAsync(Source("6 * 7")));
    }
}
