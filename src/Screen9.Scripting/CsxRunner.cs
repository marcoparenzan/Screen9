using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Screen9.Scripting;

/// <summary>Runs a C# script (.csx) on Roslyn scripting with the Screen9 engine referenced and imported, and a
/// <see cref="GameGlobals"/> as its globals. Same role as PySharp for <c>gorillas.py</c>: the language is
/// embedded, the host supplies the devices.</summary>
public static class CsxRunner
{
    private static readonly string[] Imports =
    [
        "System", "System.Collections.Generic", "System.Globalization", "System.IO", "System.Linq",
        "System.Text", "System.Threading", "System.Threading.Tasks", "Screen9",
    ];

    public static ScriptOptions Options(string fileName) => ScriptOptions.Default
        .WithFilePath(fileName)
        .WithReferences(typeof(object).Assembly, typeof(Enumerable).Assembly, typeof(PCCanvas).Assembly, typeof(GameGlobals).Assembly)
        .WithImports(Imports);

    /// <summary>Compiles, then runs on a worker thread (scripts block: a game loop, a sleep). Returns "Completed",
    /// "Canceled", or "Error: ..." with the compiler diagnostics or the exception's message.</summary>
    public static Task<string> RunAsync(string source, string fileName, GameGlobals globals, CancellationToken token = default) => Task.Run(async () =>
    {
        try
        {
            var script = CSharpScript.Create(source, Options(fileName), typeof(GameGlobals));
            var diagnostics = script.Compile(token);
            var errors = diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
            if (errors.Count > 0) return "Error: " + string.Join(" | ", errors.Take(5).Select(d => d.ToString()));
            await script.RunAsync(globals, token);
            return "Completed";
        }
        catch (OperationCanceledException) { return "Canceled"; }
        catch (CompilationErrorException e) { return "Error: " + string.Join(" | ", e.Diagnostics.Take(5).Select(d => d.ToString())); }
        catch (Exception e) { return "Error: " + e.Message; }
    }, token);
}
