namespace Screen9.Web;

/// <summary>A per-circuit scratch folder for the TUI editor's Save/Open (F2/F3), so those work exactly as in
/// <c>qbs edit</c> — <see cref="QBSharp.Cli.Editor.EditorDocument"/> is untouched. Deleted when the circuit
/// disconnects. Known sharp edge (accepted for now): <c>EditorController.OpenDialog</c>'s text box starts
/// empty, so a bare filename typed there resolves against the process working directory
/// (<c>EditorDocument.ValidatePath</c> → <c>Path.GetFullPath</c>), not this folder — <see cref="Program"/>
/// points that shared working directory at its own empty scratch root (see <c>CwdScratchRoot</c>) so the
/// worst case is an orphaned file there, never a collision with another session's real content.</summary>
public static class SessionWorkspace
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "screen9-web", "sessions");
    public static readonly string CwdScratchRoot = Path.Combine(Path.GetTempPath(), "screen9-web", "cwd-scratch");

    /// <summary>A fresh, empty folder for one circuit.</summary>
    public static string Create()
    {
        string dir = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Copies the bundled read-only samples into the session folder and returns the absolute path
    /// the editor should open first.</summary>
    public static string Seed(string sessionDirectory)
    {
        string samples = Path.Combine(AppContext.BaseDirectory, "samples");
        foreach (string file in Directory.EnumerateFiles(samples).Where(f => f.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".py", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".csx", StringComparison.OrdinalIgnoreCase)))
            File.Copy(file, Path.Combine(sessionDirectory, Path.GetFileName(file)), overwrite: true);
        return Path.Combine(sessionDirectory, "GORILLA.BAS");
    }

    public static void Delete(string sessionDirectory)
    {
        try { Directory.Delete(sessionDirectory, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
