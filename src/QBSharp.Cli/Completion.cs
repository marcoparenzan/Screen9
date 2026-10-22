namespace QBSharp.Cli;

public static class Completion
{
    public static readonly string[] Commands = ["run", "repl", "new", "test", "help", "complete", "edit"];
    public static readonly string[] ReplCommands = [":help", ":quit", ":vars", ":reset", ":cancel", ":complete"];
    public static readonly string[] Keywords = ["Json", "Yaml", "File", "Directory", "Path", "Config", "Ui", "Class", "Namespace", "Property", "Public", "Me", "New", "Import", "Of", "Sub", "Dim", "As", "If", "Then", "ElseIf", "Select", "Case", "Each", "In", "Is", "Else", "End", "While", "For", "Next", "To", "Step", "Do", "Loop", "Until", "Exit", "Function", "Return", "True", "False", "Nothing", "Not", "And", "Or", "AndAlso", "OrElse", "Mod", "Integer", "Long", "Double", "Decimal", "Boolean", "String", "Object", "Date", "Print", "Args"];
    public static string[] Repl(string prefix, IEnumerable<string> variables) => Keywords.Concat(ReplCommands).Concat(variables)
        .Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    public static string[] Cli(string prefix, string directory)
    {
        var candidates = Commands.Concat(["--help", "--version", "--max-steps", "--dialect"]).Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        string? parent = Path.GetDirectoryName(prefix); string leaf = Path.GetFileName(prefix);
        string search = string.IsNullOrEmpty(parent) ? directory : Path.GetFullPath(parent, directory);
        if (Directory.Exists(search))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(search).Take(1000))
            {
                bool folder = Directory.Exists(path);
                if (!folder && !Path.GetExtension(path).Equals(".bas", StringComparison.OrdinalIgnoreCase)) continue;
                string name = Path.GetFileName(path);
                if (!name.StartsWith(leaf, StringComparison.OrdinalIgnoreCase)) continue;
                candidates.Add((string.IsNullOrEmpty(parent) ? name : Path.Combine(parent, name)) + (folder ? Path.DirectorySeparatorChar.ToString() : ""));
            }
        }
        return candidates.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
