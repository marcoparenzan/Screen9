#!/usr/bin/env dotnet
#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable

// Builds the `screen9` wheel for PySharp: src/Screen9.Python/screen9 (Python) + Screen9.dll and its dependencies
// (dotnet publish, Release) + the IBM font licenses. Pure wheel (py3-none-any): .NET IL runs on every platform.
//   dotnet run ops/build-python-wheel.cs            -> ops/artifacts/python/screen9-<Version>-py3-none-any.whl
//   pysharp install screen9 --find-links ops/artifacts/python
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

string root = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFilePath") is string entry ? Path.GetDirectoryName(entry)! : ".", ".."));
string project = Path.Combine(root, "src", "Screen9", "Screen9.csproj");
string version = Regex.Match(File.ReadAllText(project), "<Version>([^<]+)</Version>").Groups[1].Value;
string staging = Path.Combine(root, "ops", "artifacts", "python", "staging");
string output = Path.Combine(root, "ops", "artifacts", "python", $"screen9-{version}-py3-none-any.whl");
string package = Path.Combine(staging, "screen9");

if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
string publish = Path.Combine(staging, "publish");
var build = Process.Start(new ProcessStartInfo("dotnet", ["publish", project, "-c", "Release", "-o", publish, "-v", "q", "-nologo"]) { UseShellExecute = false })!;
build.WaitForExit();
if (build.ExitCode != 0) { Console.Error.WriteLine("dotnet publish failed."); return 1; }

Directory.CreateDirectory(Path.Combine(package, "fonts"));
foreach (var dll in Directory.GetFiles(publish, "*.dll")) File.Copy(dll, Path.Combine(package, Path.GetFileName(dll)));
string init = File.ReadAllText(Path.Combine(root, "src", "Screen9.Python", "screen9", "__init__.py"))
    .Replace("__version__ = \"0.0.0\"  # replaced when the wheel is built", $"__version__ = \"{version}\"");
File.WriteAllText(Path.Combine(package, "__init__.py"), init);
foreach (var name in new[] { "LICENSE.TXT", "SOURCE.TXT" })
    File.Copy(Path.Combine(root, "src", "Screen9", "Fonts", name), Path.Combine(package, "fonts", name));

string distInfo = $"screen9-{version}.dist-info";
var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
foreach (var file in Directory.GetFiles(package, "*", SearchOption.AllDirectories))
    files["screen9/" + Path.GetRelativePath(package, file).Replace('\\', '/')] = File.ReadAllBytes(file);
files[$"{distInfo}/METADATA"] = Encoding.UTF8.GetBytes($"""
    Metadata-Version: 2.1
    Name: screen9
    Version: {version}
    Summary: Screen9 retro PC machine (CGA/EGA/VGA framebuffer, IBM fonts, PLAY, sprites, pixel window) for PySharp
    Requires-Python: >=3.12
    Classifier: Programming Language :: Python :: Implementation :: PySharp
    Classifier: Topic :: Games/Entertainment

    The Screen9 engine as a PySharp package: the .NET types of Screen9.dll, loaded with PySharp's clr module.
    Requires PySharp (a Python interpreter in .NET); CPython cannot load it. IBM fonts: CC BY-SA 4.0 (screen9/fonts).

    """.Replace("\r\n", "\n"));
files[$"{distInfo}/WHEEL"] = Encoding.UTF8.GetBytes("Wheel-Version: 1.0\nGenerator: Screen9 ops/build-python-wheel.cs\nRoot-Is-Purelib: true\nTag: py3-none-any\n");
files[$"{distInfo}/top_level.txt"] = Encoding.UTF8.GetBytes("screen9\n");
var manifest = new StringBuilder();
foreach (var (path, data) in files)
    manifest.Append(path).Append(",sha256=").Append(Convert.ToBase64String(SHA256.HashData(data)).TrimEnd('=').Replace('+', '-').Replace('/', '_')).Append(',').Append(data.Length).Append('\n');
manifest.Append($"{distInfo}/RECORD,,\n");
files[$"{distInfo}/RECORD"] = Encoding.UTF8.GetBytes(manifest.ToString());

if (File.Exists(output)) File.Delete(output);
using (var zip = ZipFile.Open(output, ZipArchiveMode.Create))
    foreach (var (path, data) in files)
    {
        using var stream = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
        stream.Write(data);
    }
Directory.Delete(staging, recursive: true);
Console.WriteLine($"{output} ({files.Count} files)");
return 0;
