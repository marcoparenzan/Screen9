using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.Core.Modules;

namespace QBSharp.Tests;

public sealed class ModuleTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "qbs-modules-" + Guid.NewGuid());
    public ModuleTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private string Write(string name, string text)
    {
        string path = Path.Combine(root, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); return path;
    }
    private ScriptEngine Engine() => new(options: new() { BaseDirectory = root, UserLibraryDirectory = Path.Combine(root, "user"), StandardLibraryDirectory = Path.Combine(root, "standard") });
    [Fact]
    public async Task RuntimeDynamicImportAliasExportsStateCacheAndIsolatedSessions()
    {
        Write("helpers.bas", "Tick()\nDim counter As Integer = 0\nFunction NextValue() As Integer\ncounter = counter + 1\nReturn counter\nEnd Function");
        int loads = 0; var bindings = new HostBindings(); bindings.RegisterFunction("Tick", (_, _) => { loads++; return ValueTask.FromResult<object?>(null); });
        var engine = Engine(); await using var session = engine.CreateSession(bindings);
        await session.ExecuteAsync(new("Dim path = \"helpers\"\nIf True Then\nImport path As H\nEnd If\nImport \"helpers.bas\" As Again\nImport \"helpers\" As H", Path.Combine(root, "main.bas")));
        Assert.Equal(1, loads); Assert.Equal(1, await session.EvaluateAsync(new("H.NextValue()"))); Assert.Equal(2, await session.EvaluateAsync(new("Again.NextValue()")));
        await session.ExecuteAsync(new("H.counter = 10")); Assert.Equal(11, await session.EvaluateAsync(new("Again.NextValue()")));
        Assert.False(session.Variables.ContainsKey("counter"));
        await using var other = engine.CreateSession(bindings); await other.ExecuteAsync(new("Import \"helpers\"")); Assert.Equal(2, loads); Assert.Equal(1, await other.EvaluateAsync(new("helpers.NextValue()")));
    }
    [Fact]
    public async Task ResolverOrderingImporterUserStandardAndPackageCandidate()
    {
        string main = Write("nested/main.bas", "");
        Write("nested/choice.bas", "Dim value = 1"); Write("user/choice.bas", "Dim value = 2"); Write("standard/choice.bas", "Dim value = 3");
        Write("user/useronly.bas", "Dim value = 4"); Write("standard/stdonly.bas", "Dim value = 5"); Write("nested/pkg/__init__.bas", "Dim value = 6");
        await using var session = Engine().CreateSession();
        await session.ExecuteAsync(new("Import \"choice\"\nImport \"useronly\"\nImport \"stdonly\"\nImport \"pkg\"", main));
        Assert.Equal(1, await session.EvaluateAsync(new("choice.value"))); Assert.Equal(4, await session.EvaluateAsync(new("useronly.value")));
        Assert.Equal(5, await session.EvaluateAsync(new("stdonly.value"))); Assert.Equal(6, await session.EvaluateAsync(new("pkg.value")));
        var resolver = new FileModuleResolver(root, Path.Combine(root, "user"), Path.Combine(root, "standard"));
        Assert.Equal(Path.Combine(root, "user", "choice.bas"), (await resolver.ResolveAsync(new("choice", "<repl>"))).CanonicalPath);
        Write("nested/pkg.bas", "Dim value = 7"); Assert.Equal(Path.Combine(root, "nested", "pkg.bas"), (await resolver.ResolveAsync(new("pkg", main))).CanonicalPath);
    }
    [Fact]
    public async Task ImportFromImportedFunctionUsesDefiningFileNotCaller()
    {
        Write("lib/tools.bas", "Function Value()\nImport \"detail\" As D\nReturn D.value\nEnd Function"); Write("lib/detail.bas", "Dim value = 42"); Write("detail.bas", "Dim value = 0");
        await using var session = Engine().CreateSession(); await session.ExecuteAsync(new("Import \"lib/tools\" As Tools"));
        Assert.Equal(42, await session.EvaluateAsync(new("Tools.Value()", Path.Combine(root, "caller.bas"))));
    }
    [Fact]
    public async Task ModuleClassesAndNamespaceTypesAreConstructible()
    {
        Write("models.bas", "Namespace Ops\nClass Person\nProperty Name As String = \"Ada\"\nEnd Class\nEnd Namespace");
        await using var session = Engine().CreateSession();
        await session.ExecuteAsync(new("Import \"models\"\nDim person As models.Ops.Person = New models.Ops.Person()"));
        Assert.Equal("Ada", await session.EvaluateAsync(new("person.Name")));
    }
    [Fact]
    public async Task ModulesDoNotSeeCallerLocalsButDoReceiveExplicitHostBindings()
    {
        Write("module.bas", "Dim exposed = HostValue\nFunction Value()\nReturn caller\nEnd Function");
        await using var session = Engine().CreateSession(); session.SetVariable("HostValue", 42);
        await session.ExecuteAsync(new("Dim caller = 7\nImport \"module\" As M")); Assert.Equal(42, await session.EvaluateAsync(new("M.exposed")));
        var error = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.EvaluateAsync(new("M.Value()")));
        Assert.EndsWith("module.bas", error.Diagnostic.Span.File);
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.EvaluateAsync(new("M.HostValue")));
    }
    [Fact]
    public async Task UnexecutedBranchesDoNotResolveImport()
    {
        await using var session = Engine().CreateSession(); await session.ExecuteAsync(new("If False Then\nImport \"missing\"\nEnd If")); Assert.Empty(session.Variables);
    }
    [Fact]
    public async Task CyclesAndDepthAreDiagnosed()
    {
        Write("a.bas", "Import \"b\""); Write("b.bas", "Import \"a\"");
        await using var cyclic = Engine().CreateSession();
        var cycle = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await cyclic.ExecuteAsync(new("Import \"a\"")));
        Assert.Equal("QBS3002", cycle.Diagnostic.Code); Assert.EndsWith("b.bas", cycle.Diagnostic.Span.File);
        await using var shallow = new ScriptEngine(options: new() { BaseDirectory = root, MaximumImportDepth = 1 }).CreateSession();
        var depth = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await shallow.ExecuteAsync(new("Import \"a\""))); Assert.Equal("QBS3003", depth.Diagnostic.Code);
    }
    [Fact]
    public async Task FailedModulesAreNotExportedAndFailureIsCached()
    {
        Write("broken.bas", "Tick()\nDim x = unknown"); int count = 0;
        await using var session = Engine().CreateSession(); session.RegisterFunction("Tick", (_, _) => { count++; return ValueTask.FromResult<object?>(null); });
        var first = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(new("Import \"broken\"")));
        var second = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(new("Import \"broken\"")));
        Assert.Same(first, second); Assert.Equal(1, count); Assert.False(session.Variables.ContainsKey("broken"));
        Write("syntax.bas", "Dim = 1"); var parse = await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(new("Import \"syntax\"")));
        Assert.Equal("QBS1100", parse.Diagnostic.Code); Assert.EndsWith("syntax.bas", parse.Diagnostic.Span.File);
    }
    [Fact]
    public async Task CanceledModuleCanBeRetriedInSameSession()
    {
        Write("cancel.bas", "Tick()\nDim value = 42"); using var cts = new CancellationTokenSource(); int count = 0;
        await using var session = Engine().CreateSession(); session.RegisterFunction("Tick", (_, _) => { if (++count == 1) cts.Cancel(); return ValueTask.FromResult<object?>(null); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await session.ExecuteAsync(new("Import \"cancel\""), cts.Token));
        await session.ExecuteAsync(new("Import \"cancel\" As C")); Assert.Equal(42, await session.EvaluateAsync(new("C.value"))); Assert.Equal(2, count);
    }
    [Theory]
    [InlineData("Import 1")]
    [InlineData("Import \"\"")]
    [InlineData("Import \"missing\"")]
    [InlineData("Dim H = 1\nImport \"helpers\" As H")]
    [InlineData("Import \"helpers\"\nhelpers = Nothing")]
    [InlineData("Import \"helpers\"\nhelpers.missing")]
    public async Task InvalidImportsAndAliasCollisionsFail(string code)
    {
        Write("helpers.bas", "Dim value = 1"); await using var session = Engine().CreateSession();
        await Assert.ThrowsAsync<ScriptRuntimeException>(async () => await session.ExecuteAsync(new(code)));
    }
    [Fact]
    public async Task ResolverCancellationAndCustomResolverEmbedding()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new FileModuleResolver(root).ResolveAsync(new("a", null), cts.Token));
        var resolver = new MemoryResolver(); await using var session = new ScriptEngine(moduleResolver: resolver).CreateSession();
        await session.ExecuteAsync(new("Import \"memory\" As M", "caller.bas")); Assert.Equal(42, await session.EvaluateAsync(new("M.value"))); Assert.Equal("caller.bas", resolver.Importer);
    }
    private sealed class MemoryResolver : IModuleResolver
    {
        public string? Importer;
        public ValueTask<ResolvedModule> ResolveAsync(ModuleRequest request, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); Importer = request.ImportingFile; return ValueTask.FromResult(new ResolvedModule("memory.bas", new("Dim value = 42", "memory.bas"))); }
    }
}
