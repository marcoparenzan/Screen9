#!/usr/bin/env dotnet
#:project ../src/Screen9/Screen9.csproj
#:project ../samples/csharp/Gorilla/GorillasCore.csproj
#:project ../samples/csharp/RallyX/RallyXCore.csproj
#:project ../samples/csharp/Demos/Core/Screen9.DemosCore.csproj
#:project ../src/QBSharp.Cli/QBSharp.Cli.csproj
#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable

// Renders screenshots of the games and demos into docs/images, to reuse in documents and slides. No window, no browser:
// every frame comes straight from the engine's indexed framebuffer, so the pictures are exact and repeatable.
// Each picture is saved twice:
//   name.png          1280x960, as a monitor showed it (4:3: EGA 640x350 pixels are not square), nearest-neighbour
//   name-native.png   the framebuffer's own pixels (640x350, 640x480, 320x200, ...)
//
//   dotnet run ops/make-screenshots.cs                 writes docs/images
//   dotnet run ops/make-screenshots.cs -- some/folder  writes somewhere else
using Gorilla;
using QBSharp.Cli.Editor;
using RallyX;
using Screen9;
using Screen9.Demos;

string folder = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine("docs", "images"));
Directory.CreateDirectory(folder);

Gorillas();
RallyXScreens();
Plasma();
Scene();
await EditorScreens();
return 0;

void Save(string name, int width, int height, byte[] indices, PCRgb[] palette)
{
    File.WriteAllBytes(Path.Combine(folder, name + "-native.png"), PngFrame.Encode(width, height, indices, palette));
    const int w = 1280, h = 960; // 4:3
    var scaled = new byte[w * h];
    for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            scaled[y * w + x] = indices[(y * height / h) * width + x * width / w];
    File.WriteAllBytes(Path.Combine(folder, name + ".png"), PngFrame.Encode(w, h, scaled, palette));
    Console.WriteLine($"{name}.png  ({width}x{height} -> {w}x{h})");
}

void SaveCanvas(string name, PCCanvas canvas) =>
    Save(name, canvas.Width, canvas.Height, canvas.PixelSnapshot(canvas.VisualPage), Enumerable.Range(0, canvas.ColorCount).Select(canvas.GetRgb).ToArray());

/// <summary>Gorillas (the C# game), driven by a script of keys until it asks for the first angle: the city, the gorillas, the wind.</summary>
void Gorillas()
{
    const string script = " \r\r2\r\rp"; // intro, default names, 2 points, default gravity, P = play
    int next = 0;
    PCCanvas? graphics = null;
    var host = new GorillasHost
    {
        Present = (pc, _) => { if (pc is not null) graphics = pc; },
        ReadKey = () =>
        {
            if (next < script.Length) return script[next++].ToString();
            if (graphics is not null) { SaveCanvas("gorillas-city", graphics); throw new OperationCanceledException(); }
            return "";
        },
        FlushKeys = () => { },
        Sleep = _ => { },
        Tone = _ => { },
        Seed = () => 1990,
    };
    try { new GorillasGame(host).Run(); } catch (OperationCanceledException) { }
}

/// <summary>Rally-X: the title, a few seconds into a round (the autopilot driving), and a crash.</summary>
void RallyXScreens()
{
    var assets = RallyXAssets.Load();
    var renderer = new RallyXRenderer(assets);
    Save("rallyx-title", RallyXRenderer.Width, RallyXRenderer.Height, (byte[])renderer.RenderTitle(12340, true).Clone(), assets.TitlePalette);

    var sim = new RallyXSim(assets, 3);
    sim.StartRound(1, 0, 3);
    var bot = new RallyXAutopilot();
    for (int i = 0; i < 60 * 14; i++) { bot.Drive(sim); sim.Step(); }
    Save("rallyx-play", RallyXRenderer.Width, RallyXRenderer.Height, (byte[])renderer.RenderGame(sim, 12340, false).Clone(), assets.GamePalette);

    sim.Rocks.Add(new RallyXSpot(sim.Player.TileX, sim.Player.TileY));
    for (int i = 0; i < 10 && sim.Phase != RallyXPhase.Crashed; i++) sim.Step();
    Save("rallyx-crash", RallyXRenderer.Width, RallyXRenderer.Height, (byte[])renderer.RenderGame(sim, 12340, false).Clone(), assets.GamePalette);
}

void Plasma()
{
    var demo = new PlasmaDemo();
    for (int i = 0; i < 90; i++) demo.Step();
    SaveCanvas("plasma", demo.Canvas);
}

/// <summary>The cell canvas, drawn the way the VGA showed text: each cell with the ROM 8x16 font.</summary>
void Scene()
{
    var demo = new SceneDemo(80, 25, "Screen9 - cell canvas demo");
    for (int i = 0; i < 70; i++) demo.Step();
    SaveCells("scene", demo.Canvas);
}

/// <summary>A cell canvas (80x25 text) as a 640x400 picture, the way the VGA showed text mode.</summary>
void SaveCells(string name, Canvas cells)
{
    var (width, height, bgra) = PixelWindow.Capture(cells);
    var palette = new List<PCRgb>();
    var indices = new byte[width * height];
    for (int i = 0; i < indices.Length; i++)
    {
        var color = new PCRgb(bgra[i * 4 + 2], bgra[i * 4 + 1], bgra[i * 4]);
        int index = palette.IndexOf(color);
        if (index < 0) { palette.Add(color); index = palette.Count - 1; }
        indices[i] = (byte)index;
    }
    Save(name, width, height, indices, palette.ToArray());
}

/// <summary>The QBasic editor (the one of `qbs edit` and the /editor page), driven with keys and drawn headless at 80x25:
/// code with the lexer's colours, the Run menu open, and a program run with its output in the lower pane.</summary>
async Task EditorScreens()
{
    string samples = Path.GetFullPath(Path.Combine("samples", "qbsharp"));
    // The editor shows the file's full path in its title bar: work on copies in a short neutral folder, so the pictures do not
    // carry the path of the machine they were made on (C:\screen9\GORILLA.BAS, not the user's profile or project folder).
    string neutral = Path.Combine(Path.GetPathRoot(samples)!, "screen9");
    Directory.CreateDirectory(neutral);
    try
    {
        await EditorScreensIn(samples, neutral);
    }
    finally { try { Directory.Delete(neutral, true); } catch (IOException) { } }
}

async Task EditorScreensIn(string samples, string neutral)
{
    string Copy(string source) { string target = Path.Combine(neutral, Path.GetFileName(source)); File.Copy(source, target, true); return target; }

    // 1. GORILLA.BAS (QBasic dialect), scrolled to the PlayGame procedure.
    var document = new EditorDocument();
    await document.OpenAsync(Copy(Path.Combine(samples, "Gorilla", "GORILLA.BAS")));
    await using var runner = new EditorRunner();
    var editor = new EditorController(document, runner, null, "qbasic");
    editor.Resize(80, 25);
    for (int i = 0; i < 28; i++) await editor.HandleAsync(new EditorInput(EditorKey.PageDown));
    SaveCells("editor-code", EditorFrame.Build(editor).ToCanvas());

    // 2. The Run menu: F10, then right to Edit, right to Run.
    await editor.HandleAsync(new EditorInput(EditorKey.F10));
    await editor.HandleAsync(new EditorInput(EditorKey.Right));
    await editor.HandleAsync(new EditorInput(EditorKey.Right));
    SaveCells("editor-menu", EditorFrame.Build(editor).ToCanvas());

    // 3. A small program run with F5, output in the lower pane.
    var hello = new EditorDocument();
    await hello.OpenAsync(Copy(Path.Combine(samples, "hello.bas")));
    await using var helloRunner = new EditorRunner();
    var running = new EditorController(hello, helloRunner, null, "qbs");
    running.Resize(80, 25);
    await running.HandleAsync(new EditorInput(EditorKey.F5));
    for (int i = 0; i < 200 && (helloRunner.Running || running.LastResult is null); i++) { await Task.Delay(25); running.Tick(); }
    running.Tick();
    SaveCells("editor-run", EditorFrame.Build(running).ToCanvas());
}
