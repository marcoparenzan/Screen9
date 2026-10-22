using Gorilla;
using Screen9.Scripting;

namespace Screen9.Web.Tests;

/// <summary>gorillas.csx on Roslyn scripting, against GorillasCore: the script is the same game, and with the same
/// seed and keys it must play the same match (score, frames presented, keys read).</summary>
public sealed class CsxGamesTests
{
    private static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Screen9.slnx"))) dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("Screen9.slnx not found above " + AppContext.BaseDirectory);
        }
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();

    // The same scripted match as GorillasDemo: intro, default names, 2 points, default gravity, P; then angle 45 / velocity 1.
    private static IEnumerator<char> Keys()
    {
        foreach (char c in " \r\r2\r\rp") yield return c;
        for (int shot = 0; shot < 60; shot++) foreach (char c in "45\r1\r") yield return c;
    }

    [Fact]
    public void ScriptCarriesTheGameTextOfGorillasCoreVerbatim()
    {
        string core = Normalize(File.ReadAllText(Path.Combine(Root, "samples", "csharp", "Gorilla", "GorillasCore.cs")));
        string script = Normalize(File.ReadAllText(Path.Combine(Root, "samples", "csharp", "Gorilla", "gorillas.csx")));
        string game = core[core.IndexOf("/// <summary>Dispositivi del gioco", StringComparison.Ordinal)..];
        Assert.Contains(game, script); // regenerate gorillas.csx from GorillasCore.cs if this fails
    }

    [Fact]
    public async Task ScriptPlaysTheSameMatchAsGorillasCore()
    {
        int seed = 1990, frames = 0, pressed = 0; var keys = Keys();
        var globals = new GameGlobals
        {
            Present = (_, _) => frames++,
            ReadKey = () => { pressed++; return keys.MoveNext() ? keys.Current.ToString() : throw new InvalidOperationException("keys exhausted"); },
            FlushKeys = () => { }, Sleep = _ => { }, Tone = _ => { }, Seed = () => seed++,
        };
        string result = await CsxRunner.RunAsync(File.ReadAllText(Path.Combine(Root, "samples", "csharp", "Gorilla", "gorillas.csx")), "gorillas.csx", globals);

        int coreSeed = 1990, coreFrames = 0, corePressed = 0; var coreKeys = Keys();
        var core = new GorillasGame(new GorillasHost
        {
            Present = (_, _) => coreFrames++,
            ReadKey = () => { corePressed++; return coreKeys.MoveNext() ? coreKeys.Current.ToString() : throw new InvalidOperationException("keys exhausted"); },
            FlushKeys = () => { }, Sleep = _ => { }, Tone = _ => { }, Seed = () => coreSeed++,
        });
        core.Run();

        Assert.Equal("Completed", result);
        Assert.Equal((coreFrames, corePressed), (frames, pressed));
        Assert.Equal(252, frames); Assert.Equal(18, pressed); // the same figures as the desktop selftest
    }

    [Fact]
    public async Task CompileErrorsComeBackAsText()
    {
        string result = await CsxRunner.RunAsync("int x = \"not an int\";", "bad.csx", new GameGlobals());
        Assert.StartsWith("Error:", result);
        Assert.Contains("bad.csx", result);
    }

    [Fact]
    public async Task CancellingStopsTheScript()
    {
        using var cts = new CancellationTokenSource();
        var globals = new GameGlobals { ReadKey = () => { cts.Cancel(); cts.Token.ThrowIfCancellationRequested(); return ""; } };
        Assert.Equal("Canceled", await CsxRunner.RunAsync("ReadKey();", "stop.csx", globals, cts.Token));
    }
}
