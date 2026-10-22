using QBSharp.Core.Syntax;

namespace QBSharp.Core.Diagnostics;

public enum DiagnosticSeverity { Warning, Error }

public sealed record ScriptDiagnostic(string Code, string Message, SourceSpan Span,
    DiagnosticSeverity Severity = DiagnosticSeverity.Error)
{
    public override string ToString() => $"{Span}: {Severity} {Code}: {Message}";
}

/// <summary>Runtime error with original source location and optional host exception.</summary>
public sealed class ScriptRuntimeException(ScriptDiagnostic diagnostic, Exception? innerException = null)
    : Exception(diagnostic.ToString(), innerException)
{
    public ScriptDiagnostic Diagnostic { get; } = diagnostic;
}
