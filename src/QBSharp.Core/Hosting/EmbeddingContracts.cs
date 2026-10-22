using System.Collections.Immutable;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Syntax;

namespace QBSharp.Core.Hosting;

public sealed record ScriptSource
{
    public string Text { get; }
    public string FileName { get; }
    public ScriptSource(string text, string fileName = "<host>")
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        Text = text; FileName = fileName;
    }
}
public sealed record ParseResult<T>(T? Root, ImmutableArray<ScriptDiagnostic> Diagnostics) where T : SyntaxNode
{
    public bool Success => Root is not null && !Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
/// <summary>Stateless parsing contract, implemented by ScriptParser.</summary>
public interface IScriptParser
{
    ParseResult<Script> ParseScript(ScriptSource source, CancellationToken cancellationToken = default);
    ParseResult<Expression> ParseExpression(ScriptSource source, CancellationToken cancellationToken = default);
}
/// <summary>Creates isolated sessions; no process-global variable state.</summary>
public interface IScriptEngine
{
    IScriptSession CreateSession(HostBindings? bindings = null);
}
/// <summary>Single execution at a time; snapshots and host objects are shallow, not sandboxed.</summary>
public interface IScriptSession : IAsyncDisposable
{
    ValueTask ExecuteAsync(ScriptSource source, CancellationToken cancellationToken = default);
    ValueTask<object?> EvaluateAsync(ScriptSource expression, CancellationToken cancellationToken = default);
    IReadOnlyDictionary<string, object?> Variables { get; }
    void SetVariable(string name, object? value);
    void RegisterFunction(string name, HostFunction function);
}
