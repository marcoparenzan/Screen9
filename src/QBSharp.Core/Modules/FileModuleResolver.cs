using System.Text;
using QBSharp.Core.Hosting;

namespace QBSharp.Core.Modules;

/// <summary>Local importer, user lib, then stdlib; name.bas before name/__init__.bas.</summary>
public sealed class FileModuleResolver : IModuleResolver
{
    private readonly string baseDirectory;
    private readonly string? userDirectory, standardDirectory;
    public FileModuleResolver(string? baseDirectory = null, string? userLibraryDirectory = null, string? standardLibraryDirectory = null)
    {
        this.baseDirectory = Path.GetFullPath(baseDirectory ?? Directory.GetCurrentDirectory());
        userDirectory = userLibraryDirectory is null ? null : Path.GetFullPath(userLibraryDirectory);
        standardDirectory = standardLibraryDirectory is null ? null : Path.GetFullPath(standardLibraryDirectory);
    }
    public async ValueTask<ResolvedModule> ResolveAsync(ModuleRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        string local = request.ImportingFile is { } file && !file.StartsWith('<') ? Path.GetDirectoryName(Path.GetFullPath(file, baseDirectory))! : baseDirectory;
        var roots = new[] { local, userDirectory, standardDirectory }.Where(x => x is not null).Distinct();
        foreach (string? root in roots)
        {
            string path = Path.GetFullPath(request.Name, root!);
            string[] candidates = Path.GetExtension(path).Equals(".bas", StringComparison.OrdinalIgnoreCase) ? [path] : [path + ".bas", Path.Combine(path, "__init__.bas")];
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(candidate)) continue;
                string text = await File.ReadAllTextAsync(candidate, new UTF8Encoding(false, true), cancellationToken).ConfigureAwait(false);
                return new(candidate, new(text, candidate));
            }
        }
        throw new FileNotFoundException($"QBS module '{request.Name}' not found (local/user/stdlib).", request.Name);
    }
}
