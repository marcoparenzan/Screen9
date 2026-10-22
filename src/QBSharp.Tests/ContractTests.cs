using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.Core.Syntax;

namespace QBSharp.Tests;

public sealed class ContractTests
{
    private static SourceSpan Span => new("example.bas", new SourcePosition(4, 2, 3), 5);

    [Fact]
    public void PositionAndDiagnosticRetainFileLineColumn()
    {
        var error = new ScriptDiagnostic("QBS1001", "Unexpected token", Span);
        var inner = new InvalidOperationException("host");
        var exception = new ScriptRuntimeException(error, inner);
        Assert.Equal("example.bas(2,3): Error QBS1001: Unexpected token", exception.Message);
        Assert.Same(inner, exception.InnerException);
        Assert.Equal(4, exception.Diagnostic.Span.Start.Offset);
        Assert.Equal(5, exception.Diagnostic.Span.Length);
    }

    [Theory]
    [InlineData(-1, 1, 1)]
    [InlineData(0, 0, 1)]
    [InlineData(0, 1, 0)]
    public void PositionRejectsInvalidCoordinates(int offset, int line, int column) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourcePosition(offset, line, column));

    [Fact]
    public void SpanRejectsInvalidInputs()
    {
        Assert.Throws<ArgumentException>(() => new SourceSpan(" ", Span.Start, 1));
        Assert.Throws<ArgumentNullException>(() => new SourceSpan("x", null!, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SourceSpan("x", Span.Start, -1));
    }

    [Fact]
    public void ScriptSourceAllowsVirtualFilesAndEmptyScripts()
    {
        Assert.Equal("<host>", new ScriptSource("").FileName);
        Assert.Equal("abc", new ScriptSource("abc", "<repl>").Text);
        Assert.Throws<ArgumentNullException>(() => new ScriptSource(null!));
        Assert.Throws<ArgumentException>(() => new ScriptSource("", ""));
    }

    [Fact]
    public void AstKeepsNestedSourcePositions()
    {
        var literal = new LiteralExpression(42, Span);
        var declaration = new VariableDeclaration("answer", "Integer", literal, Span);
        var script = new Script([declaration], Span);
        Assert.Same(literal, ((VariableDeclaration)script.Statements[0]).Initializer);
        Assert.Same(Span.Start.GetType(), literal.Span.Start.GetType());
        Assert.Equal(2, script.Statements[0].Span.Start.Line);
    }

    [Fact]
    public void ParseSuccessRequiresRootAndNoErrors()
    {
        var root = new LiteralExpression(1, Span);
        var warning = new ScriptDiagnostic("W1", "warning", Span, DiagnosticSeverity.Warning);
        Assert.True(new ParseResult<Expression>(root, [warning]).Success);
        Assert.False(new ParseResult<Expression>(null, []).Success);
        Assert.False(new ParseResult<Expression>(root, [new("E1", "error", Span)]).Success);
        Assert.True(new ParseResult<Expression>(root, ImmutableArray<ScriptDiagnostic>.Empty).Success);
    }

    [Fact]
    public void HostBindingsAreCaseInsensitiveAndPreserveDotNetObjects()
    {
        var bindings = new HostBindings();
        var list = new List<int> { 1, 2 };
        bindings.SetValue("items", list);
        Assert.True(bindings.TryGetValue("ITEMS", out var value));
        Assert.Same(list, value);
        bindings.SetValue("Items", null);
        Assert.Single(bindings.Snapshot());
        Assert.True(bindings.TryGetValue("items", out value));
        Assert.Null(value);
        Assert.False(bindings.TryGetValue("missing", out _));
    }

    [Fact]
    public void SnapshotsAreReadOnlyAndNotLive()
    {
        var bindings = new HostBindings();
        bindings.SetValue("x", 1);
        var snapshot = bindings.Snapshot();
        bindings.SetValue("X", 2);
        Assert.Equal(1, snapshot["X"]);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)snapshot).Add("y", 3));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("1name")]
    [InlineData("bad.name")]
    [InlineData("a-b")]
    public void HostBindingsRejectInvalidIdentifiers(string name) =>
        Assert.Throws<ArgumentException>(() => new HostBindings().SetValue(name, 1));

    [Fact]
    public async Task HostFunctionReceivesArgumentsAndCancellation()
    {
        var bindings = new HostBindings();
        bindings.RegisterFunction("echo", (context, args) =>
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(args[0]);
        });
        bindings.TryGetValue("ECHO", out var value);
        var function = Assert.IsType<HostFunction>(value);
        Assert.Equal("hello", await function(new(default), ["hello"]));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await function(new(cts.Token), ["x"]));
    }

    [Fact]
    public void RegistrationRejectsNullFunction() =>
        Assert.Throws<ArgumentNullException>(() => new HostBindings().RegisterFunction("f", null!));

    [Fact]
    public void DiRegistrationIsIdempotentAndBindingsAreIsolated()
    {
        var services = new ServiceCollection();
        services.AddQBSharpContracts().AddQBSharpContracts();
        Assert.Single(services);
        using var provider = services.BuildServiceProvider();
        Assert.NotSame(provider.GetRequiredService<HostBindings>(), provider.GetRequiredService<HostBindings>());
        Assert.Null(provider.GetService<IScriptEngine>());
    }
}
