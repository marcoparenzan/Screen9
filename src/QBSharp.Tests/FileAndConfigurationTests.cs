using QBSharp.Core.Hosting;
using QBSharp.StandardLibrary;
using QBSharp.StandardLibrary.Configuration;
using QBSharp.StandardLibrary.Files;
using QBSharp.StandardLibrary.Serialization;

namespace QBSharp.Tests;

public sealed class FileAndConfigurationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "qbs-stdlib-" + Guid.NewGuid());
    public FileAndConfigurationTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private FileServices Files(int limit = 1024 * 1024, int entries = 10000, bool restrict = false) => new(new() { BaseDirectory = root, MaximumFileBytes = limit, MaximumEntries = entries, RestrictToBaseDirectory = restrict });
    [Fact]
    public async Task TextBinaryCopyAtomicOverwriteBomAndDeletes()
    {
        var files = Files(); files.CreateDirectory("data");
        await files.WriteTextAsync("data/a.txt", "città"); Assert.Equal("città", await files.ReadTextAsync("data/a.txt"));
        await files.WriteTextAsync("data/a.txt", "changed"); Assert.Equal("changed", await files.ReadTextAsync("data/a.txt"));
        byte[] bytes = [0, 0xFF, 42, 0xEF, 0xBB, 0xBF]; await File.WriteAllBytesAsync(Path.Combine(root, "data/a.bin"), bytes);
        await files.CopyAsync("data/a.bin", "data/b.bin"); Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(root, "data/b.bin")));
        await File.WriteAllBytesAsync(Path.Combine(root, "bom.txt"), [0xEF, 0xBB, 0xBF, 0x41]); Assert.Equal("A", await files.ReadTextAsync("bom.txt"));
        files.DeleteFile("bom.txt"); Assert.False(files.FileExists("bom.txt"));
        Assert.Empty(Directory.GetFiles(Path.Combine(root, "data"), ".qbs-*.tmp"));
        files.DeleteDirectory("data", true); Assert.False(files.DirectoryExists("data"));
        Assert.Throws<InvalidOperationException>(() => files.DeleteDirectory(root, true));
    }
    [Fact]
    public async Task GlobAndPathResolutionAreBoundedAndRelative()
    {
        var files = Files(); files.CreateDirectory("nested"); await files.WriteTextAsync("a.bas", ""); await files.WriteTextAsync("nested/b.bas", ""); await files.WriteTextAsync("nested/b.txt", "");
        Assert.Equal(2, files.Glob("**/*.bas").Length); Assert.Single(files.Glob("nested/*.txt")); Assert.Single(files.Files("nested"), p => p.EndsWith(".txt"));
        Assert.Single(files.Directories(root)); Assert.Equal(Path.Combine(root, "a.bas"), files.Resolve("./a.bas"));
        Assert.Throws<ArgumentException>(() => files.Glob("../*.bas")); Assert.Throws<InvalidDataException>(() => Files(entries: 1).Glob("**/*"));
    }
    [Fact]
    public async Task FileLimitsInvalidUtf8CancellationAndRestrictedPaths()
    {
        var small = Files(limit: 3); await small.WriteTextAsync("tiny.txt", "abc");
        await Assert.ThrowsAsync<InvalidDataException>(async () => await small.WriteTextAsync("tiny.txt", "abcd")); Assert.Equal("abc", await small.ReadTextAsync("tiny.txt"));
        await File.WriteAllTextAsync(Path.Combine(root, "large.txt"), "abcd"); await Assert.ThrowsAsync<InvalidDataException>(async () => await small.ReadTextAsync("large.txt"));
        await File.WriteAllBytesAsync(Path.Combine(root, "bad.txt"), [0xFF]); await Assert.ThrowsAsync<System.Text.DecoderFallbackException>(async () => await small.ReadTextAsync("bad.txt"));
        Assert.Throws<UnauthorizedAccessException>(() => Files(restrict: true).Resolve("../outside.txt"));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await small.WriteTextAsync("cancel.txt", "a", cts.Token)); Assert.False(small.FileExists("cancel.txt"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Files(limit: 0));
    }
    [Theory]
    [InlineData("qbs.yaml")]
    [InlineData("qbs.yml")]
    public async Task ConfigurationAutoLoadNestedGetDefaultAndSource(string name)
    {
        await File.WriteAllTextAsync(Path.Combine(root, name), "ops:\n  name: deploy\n  count: 42");
        var config = await ConfigurationData.LoadAsync(Files(), new YamlCodec());
        Assert.Equal("deploy", config.Get("ops.name")); Assert.Equal(42, config.Get("OPS.COUNT")); Assert.Equal("fallback", config.Get("missing", "fallback")); Assert.Equal(Path.Combine(root, name), config.Source);
        var library = new StandardLibraryHost(new() { Files = new() { BaseDirectory = root } });
        await using var session = new ScriptEngine().CreateSession(await library.CreateBindingsAsync());
        Assert.Equal(42, await session.EvaluateAsync(new("Config.Data.ops.count"))); Assert.Equal("fallback", await session.EvaluateAsync(new("Config.Get(\"missing\", \"fallback\")")));
    }
    [Fact]
    public async Task ConfigMissingAmbiguousMalformedAndNonMapping()
    {
        var files = Files(); var config = await ConfigurationData.LoadAsync(files, new YamlCodec()); Assert.Null(config.Source); Assert.Empty(config.Data.Properties);
        await File.WriteAllTextAsync(Path.Combine(root, "qbs.yaml"), "[1, 2]"); await Assert.ThrowsAsync<InvalidDataException>(async () => await ConfigurationData.LoadAsync(files, new YamlCodec()));
        await File.WriteAllTextAsync(Path.Combine(root, "qbs.yaml"), "x: !Tag value"); await Assert.ThrowsAsync<InvalidDataException>(async () => await ConfigurationData.LoadAsync(files, new YamlCodec()));
        await File.WriteAllTextAsync(Path.Combine(root, "qbs.yml"), "{}"); await Assert.ThrowsAsync<InvalidDataException>(async () => await ConfigurationData.LoadAsync(files, new YamlCodec()));
        await new StandardLibraryHost(new() { LoadConfiguration = false, Files = new() { BaseDirectory = root } }).CreateBindingsAsync();
    }
    [Fact]
    public async Task ScriptFilesystemJsonYamlAndPathFunctions()
    {
        var library = new StandardLibraryHost(new() { Files = new() { BaseDirectory = root }, LoadConfiguration = false });
        await using var session = new ScriptEngine().CreateSession(await library.CreateBindingsAsync());
        await session.ExecuteAsync(new("""
            Directory.Create("data")
            Dim pathValue = Path.Combine("data", "value.json")
            Dim data = Json.Deserialize("{""name"":""Ada"",""count"":42}")
            File.WriteJson(pathValue, data)
            File.WriteYaml("data/value.yaml", data)
            File.WriteText("data/value.txt", "hello")
            File.Copy("data/value.txt", "data/copy.txt")
            Dim jsonValue = File.ReadJson(pathValue)
            Dim yamlValue = File.ReadYaml("data/value.yaml")
            """));
        Assert.Equal(42, await session.EvaluateAsync(new("jsonValue.count"))); Assert.Equal("Ada", await session.EvaluateAsync(new("yamlValue.name")));
        Assert.Equal("hello", await session.EvaluateAsync(new("File.ReadText(\"data/copy.txt\")")));
        Assert.Equal(4, await session.EvaluateAsync(new("Directory.Files(\"data\").Length")));
        Assert.Equal(true, await session.EvaluateAsync(new("File.Exists(pathValue)"))); Assert.Equal(".json", await session.EvaluateAsync(new("Path.GetExtension(pathValue)")));
        Assert.Equal("value.json", await session.EvaluateAsync(new("Path.GetFileName(pathValue)"))); Assert.Equal("data", await session.EvaluateAsync(new("Path.GetDirectoryName(pathValue)")));
        Assert.Equal(1, await session.EvaluateAsync(new("Directory.Glob(\"**/*.json\").Length")));
        await session.ExecuteAsync(new("File.Delete(pathValue)\nDirectory.Delete(\"data\", True)"));
    }
}
