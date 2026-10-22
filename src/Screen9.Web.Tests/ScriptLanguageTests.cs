using Screen9.Web;

namespace Screen9.Web.Tests;

public sealed class ScriptLanguageTests
{
    [Theory]
    [InlineData("GORILLA.BAS", ScriptLanguage.Basic)] [InlineData("hello.bas", ScriptLanguage.Basic)]
    [InlineData("gorillas.py", ScriptLanguage.Python)] [InlineData("GORILLAS.PY", ScriptLanguage.Python)]
    [InlineData("gorillas.csx", ScriptLanguage.CSharp)] [InlineData("Game.CSX", ScriptLanguage.CSharp)]
    [InlineData("notes.txt", ScriptLanguage.Basic)] [InlineData("noextension", ScriptLanguage.Basic)]
    public void ExtensionChoosesTheInterpreter(string file, ScriptLanguage expected)
        => Assert.Equal(expected, ScriptLanguages.ForFile(file));

    [Theory]
    [InlineData(ScriptLanguage.Basic, "vb")] [InlineData(ScriptLanguage.Python, "python")] [InlineData(ScriptLanguage.CSharp, "csharp")]
    public void MonacoHighlightsEachLanguage(ScriptLanguage language, string monaco)
        => Assert.Equal(monaco, ScriptLanguages.MonacoLanguage(language));

    [Theory]
    [InlineData(true, null, true)]      // a developer machine: on by default
    [InlineData(false, null, false)]    // anything else: off by default
    [InlineData(false, "true", true)]   // explicitly enabled
    [InlineData(true, "false", false)]  // explicitly disabled even in Development
    [InlineData(false, "nonsense", false)]
    public void ServerSideCodeIsOnlyForDevelopmentOrExplicit(bool development, string? configured, bool expected)
        => Assert.Equal(expected, ScriptLanguages.ServerCodeAllowed(development, configured));

    [Theory]
    [InlineData("a.bas", true)] [InlineData("A.PY", true)] [InlineData("x.csx", true)] [InlineData("notes.txt", false)] [InlineData("noext", false)]
    public void OnlyTheThreeRuntimeExtensionsAreSupportedForNewFiles(string file, bool expected)
        => Assert.Equal(expected, ScriptLanguages.IsSupported(file));
}
