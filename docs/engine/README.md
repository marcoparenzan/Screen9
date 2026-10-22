# Screen9

A **.NET 10** library for using the Windows Console as an **XY canvas of cells**, not as a pixel graphics surface.

- Origin `(0,0)` at the top left; X to the right, Y downwards.
- Each cell holds a character and foreground/background colours (`ConsoleColor`, 16 colours).
- Points, multi-line text, lines, empty and filled rectangles, clearing and snapshots.
- Clipping of the primitives at the canvas edges.
- A native Windows renderer through `WriteConsoleOutputW` with double buffering: it writes only the changed rectangle; the interface is separate from the canvas.
- Optional integration with `Microsoft.Extensions` dependency injection and logging.

## Requirements

.NET 10 SDK or compatible. For rendering: Windows and stdout attached to a real Console (classic Console or Windows Terminal). Redirected output and IDE Output panes are not supported. The in-memory canvas is independent of the operating system.

## Build, verify, run

From the main folder:

```powershell
dotnet build Screen9.slnx
dotnet test Screen9.slnx
dotnet run --project samples/csharp/Demos/Screen9.Demo.csproj
```

The demo shows text, rectangles and a line, animates a ball until a key is pressed and cleans up the Console. With `-- --smoke` it runs 180 frames, checks the Console contents and exits. With `-- --vga` it shows a PCCanvas mode 13 scene (a 240-colour plasma with palette cycling) through `AnsiFrameRenderer`; it can be combined with `--smoke`. Start it in an interactive terminal; use a window of at least 40×20 cells to see all the shapes.

## Using the library

Add the project reference:

```powershell
dotnet add path/MyApp.csproj reference src/Screen9/Screen9.csproj
```

```csharp
using Screen9;

if (!OperatingSystem.IsWindows())
    throw new PlatformNotSupportedException();

var renderer = new WindowsConsoleRenderer();
var size = renderer.GetViewportSize();
var canvas = new Canvas(size.Width, size.Height);

canvas.DrawRectangle(0, 0, canvas.Width, canvas.Height,
    new Cell('#', ConsoleColor.Cyan));
canvas.DrawText(2, 2, "Hello canvas!", ConsoleColor.Yellow);
canvas.SetPixel(10, 5, new Cell('*', ConsoleColor.Green));
renderer.Render(canvas);
Console.ReadKey(intercept: true);
```

To generate a local (unpublished) NuGet package:

```powershell
dotnet pack src/Screen9/Screen9.csproj -c Release -o ops/artifacts
```

## Verification

Build succeeded with 0 warnings and 0 errors; **180 xUnit tests passed**: coordinates, colours, text, clipping, lines, rectangles, snapshots, overflow, DI registration, PCCanvas contracts, double buffering of the renderer and of PCCanvas, the ANSI truecolor renderer (verified with an emulated VT terminal) and performance budgets. Native visual rendering was not verified automatically in an interactive Console: the demo also serves as a manual check.

See [USAGE.md](USAGE.md) for the APIs and [ARCHITECTURE.md](ARCHITECTURE.md) for the implementation choices.

## Version 1.2.0 — QBSharp runtime

Added cell ellipses (filled/outline), CanvasRegion Capture/Blit with clipping/transparency, TextConsole for literal output, window/menu/input/table/progress without Spectre, and TextCanvasRenderer for snapshots in pipelines. WindowsConsoleRenderer remains the native renderer for interactive canvases. This is not an emulation of CGA/EGA or DOS/QBasic.

Local package: `dotnet pack src/Screen9/Screen9.csproj -c Release -o <local NuGet feed folder>`. Model/runtime tests are automated; the native renderer integration was verified by the QBSharp ConPTY host, not as the UX of every terminal.


## PCCanvas
PCCanvas (standalone from 2.0.0; derived from Canvas in 1.2/1.3) provides indexed CGA/EGA/VGA modes 1, 2, 7, 8, 9, 11, 12, 13, palettes, pages, graphics primitives, sprites with bitwise raster operations and mapped video memory. ToCellCanvas provides a reduced 16-colour terminal preview; not complete PC/BIOS/register emulation. See SPEC.md for the exact API/limits and USAGE.md for an example.


## AnsiFrameRenderer
A truecolor renderer for PCCanvas on VT terminals: all 256 VGA colours (exact palette RGB), two pixels per cell with the half block ▀, only the changed cells per frame in a single write with synchronized output. Portable (it writes to a `TextWriter`); on Windows `AnsiFrameRenderer.TryEnableVirtualTerminal()` enables VT and the output must be UTF-8. See USAGE.md.


## Version 2.0.0 — breaking change
`PCCanvas` no longer derives from `Canvas`: `Canvas` is the terminal cell model (character + colours), `PCCanvas` the indexed-pixel framebuffer. They connect only through `ToCellCanvas` (16-colour preview) or `AnsiFrameRenderer` (truecolor). `Canvas.OnCellChanged` was removed. See SPEC.md for migration.

## Version 2.1.0
`ReadOnlySpan<T>` constructors for `PCImage` and `CanvasRegion` (arrays, slices, stackalloc), compatible with the previous `IReadOnlyList<T>` ones; `GetImage` and `Capture` copy the data only once. Parameters stay `int`: negative coordinates are valid input and are clipped.

## Version 2.2.0
A CPU view of video memory (`VideoSegment`, `ReadVideoMemory`/`WriteVideoMemory`) for `DEF SEG`/`PEEK`/`POKE`, and `WritePort`/`ReadPort` ports for the map mask, read map select and the VGA DAC (`OUT &H3C8`/`&H3C9`). The subset is declared in SPEC.md: no latches, write modes 1-3 or other registers.

## Version 2.3.0 / 2.3.1
`PCFont` (CP437 fonts from ROM dumps or Windows OEM `.fon` files, no font included), the BIOS text grid per mode (`CharacterHeight`, `TextColumns`, `TextRows`), `PCCanvas.DrawGlyph` and `ScrollUp` for text in graphics modes. 2.3.1: `DrawGlyph` overflow with huge coordinates fixed, block `ScrollUp` (about 0.01 ms instead of 1.5–4.7 ms per row in planar modes), `PCFont.Find` safe outside Windows.

## Included fonts

Screen9 includes the IBM EGA 8x8, EGA 8x14 and VGA 8x16 ROM fonts (`PCFont.Ega8x8`, `PCFont.Ega8x14`, `PCFont.Vga8x16`, `PCVideoMode.Font`) from **The Ultimate Oldschool PC Font Pack** by VileR (https://int10h.org/oldschool-pc-fonts/), licensed [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/). The licence text and provenance are in `src/Screen9/Fonts/LICENSE.TXT` and `SOURCE.TXT` (in the package: `fonts/`).

## Gorillas demo
`dotnet run samples/csharp/Gorilla/Gorillas.cs` plays a C# port of GORILLA.BAS (Copyright (C) Microsoft Corporation 1990; a derivative work for demonstration purposes, in the samples only, not in the package) in a pixel window (Windows), identical to the QBSharp one. A single-file .NET 10 file-based app, `Gorillas.cs`, that uses the engine devices (see below): a double-buffered GDI window, text with IBM raster glyphs, `PLAY` and the speaker, key encoding, `GET`/`PUT` sprites on LONG arrays. `-- --sound` uses the speaker; `-- --selftest` plays a scripted match without a window (seed from `GORILLAS_SEED`); `-- --smoke` plays it in the window and checks the final text, the frames captured and composed.

## Devices (2026-10-22)
Split out of QBSharp into the engine, with the same behaviour: `PixelWindow` (a Windows pixel window with a GDI back buffer, 4:3 letterbox, keys as INKEY$ codes; it shows a PCCanvas or a text Canvas with the VGA 8x16 font), `PCTextDisplay` (cursor, `LOCATE`, `VIEW PRINT`, wrapping and scrolling, IBM ROM glyphs or the compact 5x7 in graphics modes), `PCTone`/`PCMusic` (the `PLAY` language), `PCSpeaker` (a background buffer of 32 tones, a `Beep` backend on Windows and a `Silent` one), `PCKeyboard.Encode` (ConsoleKeyInfo to INKEY$), `PCSprite` (the QBasic `GET`/`PUT` format on 32-bit words, `PCSpriteOperation` operations). The C# Gorilla no longer has its own `Engine.cs` copy; QBSharp keeps only the `QBasicSpriteCodec` adapter (LONG array) and the `QBS_*` configuration. See USAGE.md.

## Exact CGA colours in the terminal (2026-10-22)
`CgaPalette.Rgb(ConsoleColor)` gives the exact RGB of the 16 PC text colours (blue 0x0000AA, brown 0xAA5500...). `AnsiCanvasRenderer` is an `ICanvasRenderer` for VT terminals that shows a cell `Canvas` with those colours in truecolor, regardless of the terminal theme; it writes only the changed cells, in a single Write with synchronized output, like `AnsiFrameRenderer`. The QBSharp editor uses it.
