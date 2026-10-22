using System.Globalization;

namespace QBSharp.Cli;

public sealed record CliCommand(string Name, string? File, string? Prefix, bool Help, long? MaximumSteps, string[] ScriptArguments) { public string Dialect { get; init; } = "qbs"; }
public sealed class CliUsageException(string message) : Exception(message);

public static class CommandLine
{
    public static CliCommand Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0) return new("help", null, null, true, null, []);
        if (args is ["--version"]) return new("version", null, null, false, null, []);
        if (args is ["--help"] or ["-h"]) return new("help", null, null, true, null, []);
        if (args[0] == "help")
        {
            if (args.Length > 2) throw new CliUsageException("Uso: qbs help [comando]");
            var subject = args.Length == 2 ? args[1] : "help"; CheckName(subject);
            return new(subject, null, null, true, null, []);
        }
        var name = args[0]; CheckName(name);
        if (name == "complete")
        {
            if (args.Length > 2) throw new CliUsageException("Uso: qbs complete [prefisso]");
            bool requestedHelp = args.Length == 2 && args[1] is "--help" or "-h";
            return new(name, null, args.Length == 2 ? args[1] : "", requestedHelp, null, []);
        }
        string? file = null; string? dialect = null; long? steps = null; bool help = false; var scriptArgs = Array.Empty<string>();
        for (int i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                if (name is not ("run" or "test")) throw new CliUsageException("-- consentito solo con run/test.");
                scriptArgs = args[(i + 1)..]; break;
            }
            if (arg is "--help" or "-h") { help = true; continue; }
            if (arg == "--dialect")
            {
                if (name is not ("run" or "test" or "edit") || dialect is not null || ++i >= args.Length || args[i] is not ("qbs" or "qbasic")) throw new CliUsageException("--dialect requires qbs or qbasic (run/test/edit).");
                dialect = args[i]; continue;
            }
            if (arg == "--max-steps")
            {
                if (name is not ("run" or "test" or "repl") || steps is not null || ++i >= args.Length ||
                    !long.TryParse(args[i], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 1)
                    throw new CliUsageException("--max-steps richiede un intero positivo (run/test/repl).");
                steps = value; continue;
            }
            if (arg.StartsWith('-') && arg != "-") throw new CliUsageException($"Opzione sconosciuta: {arg}");
            if (name is "run" or "test" or "new" or "edit")
            {
                if (file is not null) throw new CliUsageException("Un solo file; argomenti script dopo --.");
                file = arg; continue;
            }
            throw new CliUsageException($"Argomento non previsto: {arg}");
        }
        if (!help && (name is "run" or "test") && file is null) throw new CliUsageException($"Uso: qbs {name} file.bas [--max-steps N] [-- args]");
        return new(name, file, null, help, steps, scriptArgs) { Dialect = dialect ?? "qbs" };
    }
    private static void CheckName(string name)
    {
        if (name is not ("help" or "run" or "repl" or "new" or "test" or "complete" or "edit"))
            throw new CliUsageException($"Comando sconosciuto: {name}. Usa qbs --help.");
    }
}
