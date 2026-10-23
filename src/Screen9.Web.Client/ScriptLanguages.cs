namespace Screen9.Web;

/// <summary>The interpreters the Monaco editor can run a file on, chosen by its extension. <c>.bas</c> and <c>.py</c> run in the
/// browser; <c>.csx</c> is recognised (highlighting, the file list) but only the server page can run it.</summary>
public enum ScriptLanguage
{
    /// <summary>QBSharp: classic QBasic or the modern dialect (<c>.bas</c>).</summary>
    Basic,
    /// <summary>PySharp (<c>.py</c>).</summary>
    Python,
    /// <summary>C# scripting on Roslyn (<c>.csx</c>).</summary>
    CSharp,
}

public static class ScriptLanguages
{
    public static ScriptLanguage ForFile(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".py" => ScriptLanguage.Python,
        ".csx" => ScriptLanguage.CSharp,
        _ => ScriptLanguage.Basic,
    };

    /// <summary>The extensions that pick a runtime: <c>.bas</c>, <c>.py</c>, <c>.csx</c>. Anything else would silently run as QBasic.</summary>
    public static bool IsSupported(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() is ".bas" or ".py" or ".csx";

    /// <summary>Monaco's syntax-highlighting language id ('vb' is the closest built-in one to QBasic).</summary>
    public static string MonacoLanguage(ScriptLanguage language) => language switch
    {
        ScriptLanguage.Python => "python",
        ScriptLanguage.CSharp => "csharp",
        _ => "vb",
    };

    public static string Display(ScriptLanguage language) => language switch
    {
        ScriptLanguage.Python => "Python (PySharp)",
        ScriptLanguage.CSharp => "C# script (server only)",
        _ => "QBasic",
    };
}
