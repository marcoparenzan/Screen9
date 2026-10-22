using System.Text;
using Microsoft.Extensions.FileSystemGlobbing;

namespace QBSharp.StandardLibrary.Files;

public sealed record FileSystemOptions
{
    public string BaseDirectory { get; init; } = Directory.GetCurrentDirectory();
    public int MaximumFileBytes { get; init; } = 1_048_576;
    public int MaximumEntries { get; init; } = 10_000;
    public bool RestrictToBaseDirectory { get; init; }
}

public sealed class FileServices
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly FileSystemOptions options;
    public string BaseDirectory { get; }
    public FileServices(FileSystemOptions? options = null)
    {
        this.options = options ?? new(); BaseDirectory = Path.GetFullPath(this.options.BaseDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(this.options.MaximumFileBytes, 1); ArgumentOutOfRangeException.ThrowIfLessThan(this.options.MaximumEntries, 1);
    }
    public string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path); string full = Path.GetFullPath(path, BaseDirectory);
        if (options.RestrictToBaseDirectory)
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!full.Equals(BaseDirectory, comparison) && !full.StartsWith(Path.TrimEndingDirectorySeparator(BaseDirectory) + Path.DirectorySeparatorChar, comparison)) throw new UnauthorizedAccessException("Path outside configured base directory.");
            string? current = full;
            while (current is not null)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) throw new UnauthorizedAccessException("Symlink/reparse paths not allowed by restricted file policy.");
                if (current.Equals(BaseDirectory, comparison)) break;
                current = Path.GetDirectoryName(current);
            }
        }
        return full;
    }
    public async ValueTask<string> ReadTextAsync(string path, CancellationToken token = default)
    {
        await using var stream = new FileStream(Resolve(path), FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
        if (stream.Length > options.MaximumFileBytes) throw new InvalidDataException("File byte limit exceeded.");
        using var memory = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        { if (memory.Length + read > options.MaximumFileBytes) throw new InvalidDataException("File byte limit exceeded."); memory.Write(buffer, 0, read); }
        var bytes = memory.ToArray(); int start = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
        return Utf8.GetString(bytes, start, bytes.Length - start);
    }
    public async ValueTask WriteTextAsync(string path, string text, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); string full = Resolve(path); if (Utf8.GetByteCount(text) > options.MaximumFileBytes) throw new InvalidDataException("File byte limit exceeded."); byte[] bytes = Utf8.GetBytes(text);
        if (bytes.Length > options.MaximumFileBytes) throw new InvalidDataException("File byte limit exceeded.");
        string temporary = Path.Combine(Path.GetDirectoryName(full)!, ".qbs-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
            { await stream.WriteAsync(bytes, token).ConfigureAwait(false); await stream.FlushAsync(token).ConfigureAwait(false); }
            token.ThrowIfCancellationRequested(); Resolve(path); File.Move(temporary, full, true);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } }
    }
    public bool FileExists(string path) => File.Exists(Resolve(path));
    public bool DirectoryExists(string path) => Directory.Exists(Resolve(path));
    public void CreateDirectory(string path) => Directory.CreateDirectory(Resolve(path));
    public void DeleteFile(string path) => File.Delete(Resolve(path));
    public void DeleteDirectory(string path, bool recursive)
    {
        string full = Resolve(path);
        if (Path.TrimEndingDirectorySeparator(full).Equals(Path.TrimEndingDirectorySeparator(BaseDirectory), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Deleting the base directory is forbidden.");
        Directory.Delete(full, recursive);
    }
    public async ValueTask CopyAsync(string source, string destination, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested(); string target = Resolve(destination);
        await using var input = new FileStream(Resolve(source), FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
        if (input.Length > options.MaximumFileBytes) throw new InvalidDataException("File byte limit exceeded.");
        string temporary = Path.Combine(Path.GetDirectoryName(target)!, ".qbs-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
            {
                var buffer = new byte[8192]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    total += count; if (total > options.MaximumFileBytes) throw new InvalidDataException("File byte limit exceeded.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                }
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested(); Resolve(destination); File.Move(temporary, target, true);
        }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } }
    }
    public string[] Files(string path, CancellationToken token = default) => Entries(path, false, token).Where(File.Exists).ToArray();
    public string[] Directories(string path, CancellationToken token = default) => Entries(path, false, token).Where(Directory.Exists).ToArray();
    private string[] Entries(string path, bool recursive, CancellationToken token)
    {
        var results = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(Resolve(path), "*", new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = false, AttributesToSkip = FileAttributes.ReparsePoint }))
        { token.ThrowIfCancellationRequested(); if (results.Count >= options.MaximumEntries) throw new InvalidDataException("Directory entry limit exceeded."); results.Add(entry); }
        results.Sort(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal); return results.ToArray();
    }
    public string[] Glob(string pattern, string? directory = null, CancellationToken token = default)
    {
        string root = Resolve(directory ?? BaseDirectory); string normalized = pattern.Replace('\\', '/');
        if (Path.IsPathRooted(pattern) || normalized.Split('/').Contains("..")) throw new ArgumentException("Glob pattern must be relative without parent traversal.");
        var files = Entries(root, true, token).Where(File.Exists).Select(p => Path.GetRelativePath(root, p).Replace('\\', '/')).ToArray();
        var matcher = new Matcher(OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal).AddInclude(normalized);
        string[] results = matcher.Match(files).Files.Select(f => Path.Combine(root, f.Path)).Order(StringComparer.Ordinal).ToArray(); token.ThrowIfCancellationRequested(); return results;
    }
}
