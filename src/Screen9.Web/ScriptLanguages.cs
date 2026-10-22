namespace Screen9.Web;

/// <summary>The interpreters the Monaco editor can run a file on, chosen by its extension.</summary>
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
        ScriptLanguage.CSharp => "C# script (Roslyn)",
        _ => "QBasic / QBS",
    };

    /// <summary>The user's own Python and C# run in this server process with its permissions (files, processes,
    /// network): fine on a developer machine, never on a public host. Allowed in the Development environment, or
    /// explicitly with <c>Screen9:AllowServerCode=true</c>. QBasic is sandboxed by the interpreter and always allowed;
    /// the bundled Gorillas pages run fixed code and are not affected.</summary>
    public static bool ServerCodeAllowed(bool isDevelopment, string? configured)
        => bool.TryParse(configured, out bool explicitly) ? explicitly : isDevelopment;
}
