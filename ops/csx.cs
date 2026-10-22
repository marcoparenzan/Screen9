#!/usr/bin/env dotnet
#:project ../src/Screen9.Scripting/Screen9.Scripting.csproj
#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable

// Runs a C# script (.csx) on Roslyn scripting with the Screen9 engine: a pixel window for the screen, the
// keyboard from the window, the PC speaker (--sound). The script gets Screen9.Scripting.GameGlobals as its globals
// (Present, ReadKey, FlushKeys, Sleep, Tone, Background, Seed, Args).
//   dotnet run ops/csx.cs -- samples/csharp/Gorilla/gorillas.csx           Gorillas, in a pixel window (Windows)
//   dotnet run ops/csx.cs -- samples/csharp/Gorilla/gorillas.csx --sound   with the PC speaker (Console.Beep)
using Screen9;
using Screen9.Scripting;

if (args.Length == 0 || !File.Exists(args[0])) { Console.Error.WriteLine("usage: dotnet run ops/csx.cs -- <script.csx> [--sound] [script args...]"); return 2; }
if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("The pixel window requires Windows (GDI)."); return 1; }
string file = args[0]; bool sound = args.Contains("--sound");
using var window = new PixelWindow(Path.GetFileName(file) + " — Screen9 (close to stop)");
var globals = GameGlobals.ForDisplay(window, tone => { if (sound) _ = PCSpeaker.Beep(tone, default); }, CancellationToken.None, args.Skip(1).Where(a => a != "--sound").ToArray());
string result = await CsxRunner.RunAsync(File.ReadAllText(file), file, globals);
if (result.StartsWith("Error:")) { Console.Error.WriteLine(result); return 1; }
return 0;
