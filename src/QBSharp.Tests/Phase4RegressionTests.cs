using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.Core.Modules;

namespace QBSharp.Tests;

public sealed class Phase4RegressionTests
{
    private sealed class Resolver : IModuleResolver
    {
        public ValueTask<ResolvedModule> ResolveAsync(ModuleRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ResolvedModule("library.bas", new("Dim value = 42", "library.bas")));
    }
    [Fact]
    public async Task DefaultAliasCannotBeALiteralAndModuleCannotBeReopenedAsNamespace()
    {
        await using var session = new ScriptEngine(moduleResolver: new Resolver()).CreateSession();
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(new("Import \"True\"")));
        await session.ExecuteAsync(new("Import \"True\" As Lib")); Assert.Equal(42, await session.EvaluateAsync(new("Lib.value")));
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(new("Namespace Lib\nEnd Namespace")));
    }
    [Fact]
    public async Task PropertyConversionErrorsPointToPropertyDeclaration()
    {
        await using var session = new ScriptEngine().CreateSession();
        await session.ExecuteAsync(new("Class C\nProperty X As Integer = \"wrong\"\nEnd Class", "properties.bas"));
        var error = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.EvaluateAsync(new("New C()", "caller.bas")));
        Assert.Equal("properties.bas", error.Diagnostic.Span.File); Assert.Equal(2, error.Diagnostic.Span.Start.Line);
    }
    [Fact]
    public async Task VariableNamesDoNotHideBuiltinClrTypeAliases()
    {
        await using var session = new ScriptEngine().CreateSession();
        await session.ExecuteAsync(new("Dim list = 42\nDim date = \"value\"\nDim x As List(Of Date) = New List(Of Date)()\nx.Add(New Date())"));
        Assert.IsType<List<DateTime>>(session.Variables["x"]);
    }
}
