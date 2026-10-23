# Screen9 — Usage

Quick start for the whole repository. Per-part details live in [docs/engine](docs/engine/USAGE.md), [docs/qbsharp](docs/qbsharp/USAGE.md),
[docs/web](docs/web/README.md) and [samples/csharp/RallyX](samples/csharp/RallyX/README.md) (most of those pages are in Italian).

## Requirements

| For | You need |
|---|---|
| Everything | the .NET SDK (the projects target `net10.0`) |
| Pixel windows (`PixelWindow`: Gorillas, Rally-X, QBSharp graphics) | Windows (GDI); elsewhere use the web site or the ANSI renderer |
| Python version of Gorillas | [PySharp](https://github.com/marcoparenzan/PySharp) and the `screen9` wheel (see below) |
| Regenerating the Rally-X art | Python 3 with Pillow, and the original RallyX project folder |

NuGet packages that are not on nuget.org (PySharp) are taken from the `LocalFeed/` folder in this repository: nothing to configure.

## Build and test

```powershell
dotnet build Screen9.slnx
dotnet test Screen9.slnx
```

Run the test projects one at a time if you see timeouts in the QBSharp tests that spawn processes (they pass alone):
`dotnet test src/Screen9.Tests`, `src/RallyX.Tests`, `src/Screen9.Web.Tests`, `src/QBSharp.Tests`.

## Play the games

Use `-c Release` for anything graphical: the engine is about nine times slower unoptimised.

```powershell
# Gorillas
dotnet run -c Release samples/csharp/Gorilla/Gorillas.cs                       # C#, native pixel window
dotnet run ops/csx.cs -- samples/csharp/Gorilla/gorillas.csx                   # C# script (Roslyn)
dotnet run --project src/QBSharp.Cli -c Release -- run samples/qbsharp/Gorilla/GORILLA.BAS --dialect qbasic   # the original QBasic game
dotnet run ops/build-python-wheel.cs; pysharp install screen9 --find-links ops/artifacts/python
pysharp run samples/python/Gorilla/gorillas.py                                 # Python on PySharp

# Rally-X
dotnet run -c Release samples/csharp/RallyX/RallyX.cs                          # arrows/WASD, Space = smoke, P = pause, Esc = quit
dotnet run -c Release samples/csharp/RallyX/RallyX.cs -- --demo                # the autopilot plays by itself
```

## The web site

```powershell
dotnet run --project src/Screen9.Web -c Release
```

The games and demos run in the browser (WebAssembly), but only an AOT build is fast: `dotnet run` interprets the .NET code (Plasma ~22 fps), `dotnet publish` compiles it (60 fps).
One-off: `dotnet workload install wasm-tools`, then `dotnet publish src/Screen9.Web -c Release -o out` and run `dotnet out/Screen9.Web.dll` (details in [docs/web](docs/web/README.md)).

Open the printed address. Pages: Gorillas (C#, `.bas`, Python and C# script), Rally-X (`/rally-x`), the QBSharp editor (TUI) and a Monaco editor
that picks the interpreter from the file extension (`.bas` QBSharp, `.py` PySharp; both run in your browser, `.csx` only on its server page). Click the screen before typing.
Nothing you write in the editor runs on the server, so there is no setting to enable for it.

## Use the engine from your own C# code

```csharp
using Screen9;

var canvas = new PCCanvas(12);                         // 640x480, 16 colours (modes 1, 2, 7, 8, 9, 11, 12, 13 exist)
canvas.SetDac(1, 63, 0, 0);                            // VGA palette entry 1 = red (6-bit components)
canvas.Box(100, 100, 200, 120, 1, filled: true);
canvas.PutPixels(frameBytes, 0, 0, 640, 480);          // a whole frame of colour indices in one call (fast path)

using var window = new PixelWindow("My game") { IntegerScale = true };   // pixels enlarged by whole multiples only
window.Present(canvas);
string key = window.ReadKey();                         // INKEY$-style: "" when no key is waiting, "\0H" for the up arrow
```

Games talk to the outside world through a small host object (screen, keys, sleep, sound) so the same game runs in a window and in the
browser: see `GorillasHost` and `RallyXHost`. In a window the game calls `Run()` and the host sleeps (`Sleep`); in the browser there is no thread to block, so
the game calls `RunAsync()` and the host provides an awaitable `Delay`. `RgbaFrame.Fill(canvas, buffer)` expands a frame into RGBA bytes for a host that draws pixels itself.

## Troubleshooting

- **The game stutters**: you ran without `-c Release`.
- **Blurry or uneven pixels**: use `IntegerScale = true` (only for square-pixel modes) and keep the window at a whole multiple of the screen size.
- **`dotnet build` says a file is locked**: a previous game or the web site is still running; close it first.
- **A test project fails to build with a locked `QBSharp.QBasic.dll`**: same cause, a running `Screen9.Web`.
