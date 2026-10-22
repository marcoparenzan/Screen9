using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;

namespace QBSharp.Tests;

public sealed class ReflectionTests
{
    public sealed class HostObject
    {
        public int Value { get; set; } = 3;
        public string Field = "hello";
        public int ReadOnly => 7;
        public int PrivateSetter { get; private set; } = 1;
        public int Add(int n) => Value + n;
        public string Choose(int n) => $"int:{n}";
        public string Choose(string n) => $"string:{n}";
        public string Ambiguous(long n) => $"long:{n}";
        public string Ambiguous(double n) => $"double:{n}";
        public void Fail() => throw new ApplicationException("host failure");
        public void Cancel() => throw new OperationCanceledException();
        public void Set(int n) => Value = n;
        private int Hidden() => 1;
    }
    [Fact]
    public async Task PublicPropertiesFieldsMethodsAndOverloads()
    {
        var host = new HostObject(); var bindings = new HostBindings(); bindings.SetValue("host", host);
        bindings.SetValue("items", new List<int> { 1, 2 });
        bindings.SetValue("mapping", new Dictionary<string, int> { ["x"] = 4 });
        bindings.SetValue("date", new DateTime(2026, 1, 2));
        await using var session = new ScriptEngine().CreateSession(bindings);
        await session.ExecuteAsync(new("host.value = 5\nhost.Field = \"changed\"\nitems.Add(3)"));
        Assert.Equal(5, host.Value); Assert.Equal("changed", host.Field);
        Assert.Equal(7, await session.EvaluateAsync(new("host.ADD(2)")));
        Assert.Equal("int:1", await session.EvaluateAsync(new("host.Choose(1)")));
        Assert.Equal("string:x", await session.EvaluateAsync(new("host.Choose(\"x\")")));
        Assert.Equal(3, await session.EvaluateAsync(new("items.Count")));
        Assert.Equal(4, await session.EvaluateAsync(new("mapping.get_Item(\"x\")")));
        Assert.Equal(2026, await session.EvaluateAsync(new("date.Year")));
        Assert.Equal(true, await session.EvaluateAsync(new("date < date.AddDays(1)")));
        Assert.Null(await session.EvaluateAsync(new("host.Set(9)"))); Assert.Equal(9, host.Value);
        // Repeated calls and varying signatures exercise the engine's member candidate cache.
        Assert.Equal("int:2", await session.EvaluateAsync(new("host.Choose(2)")));
        Assert.Equal("string:y", await session.EvaluateAsync(new("host.Choose(\"y\")")));
    }
    [Theory]
    [InlineData("host.ReadOnly = 1")]
    [InlineData("host.PrivateSetter = 1")]
    [InlineData("host.Hidden()")]
    [InlineData("host.Missing")]
    [InlineData("host.Add(\"x\")")]
    [InlineData("host.Ambiguous(1)")]
    [InlineData("Nothing.Value")]
    public async Task InvalidReflectionIsDiagnosed(string code)
    {
        await using var session = new ScriptEngine().CreateSession(); session.SetVariable("host", new HostObject());
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(new(code, "reflection.bas")));
    }
    [Fact]
    public async Task HostExceptionIsUnwrappedAndCancellationPropagates()
    {
        await using var session = new ScriptEngine().CreateSession(); session.SetVariable("host", new HostObject());
        var error = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.EvaluateAsync(new("host.Fail()", "host.bas")));
        Assert.IsType<ApplicationException>(error.InnerException); Assert.Equal("host.bas", error.Diagnostic.Span.File);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await session.EvaluateAsync(new("host.Cancel()")));
    }
}
