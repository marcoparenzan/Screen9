# Using the canvas

## Coordinates and cells

`new Canvas(width, height)` requires positive dimensions. Sizes, coordinates and lengths are expressed in **cells**. `canvas[x,y]` reads or writes a cell and throws `ArgumentOutOfRangeException` outside the edges.

```csharp
var canvas = new Canvas(40, 15);
var ink = new Cell('*', ConsoleColor.Yellow, ConsoleColor.DarkBlue);
canvas[5, 3] = ink;
bool visible = canvas.SetPixel(-1, 3, ink); // false, no change
canvas.Clear();                          // grey spaces on black
canvas.Clear(new Cell(' ', ConsoleColor.White, ConsoleColor.DarkBlue));
```

`default(Cell)` is shown as a black space; prefer `Cell.Empty` for a grey space on black. Control characters and UTF-16 surrogates are rejected. Use single-width BMP characters: the library does not measure visual width, and does not support emoji, combining characters or wide CJK. The look of the glyphs depends on the terminal font.

## Drawing

```csharp
canvas.DrawText(2, 1, "First line\r\nSecond line", ConsoleColor.Cyan);
canvas.DrawLine(-10, 5, 25, 10, ink);
canvas.DrawRectangle(1, 1, 20, 8, ink);
canvas.FillRectangle(4, 4, 6, 3, ink);
Cell[] copy = canvas.Snapshot();
```

- The primitives ignore the parts outside the canvas.
- Rectangles occupy `[x, x+width)` and `[y, y+height)`: zero sizes draw nothing; negative ones are errors.
- Clipping an edge does not create a new edge along the canvas limit.
- Lines include their endpoints; outside segments are geometrically cut before rasterising.
- Text supports LF and CRLF, restarting from the initial X on every line. It does not wrap; tabs and lone CRs are errors. The text is validated before any cell is modified.
- There is no transparency: every write replaces the character and colours of the cell.
- The snapshot is a copy in row order, index `y * Width + x`.

## Windows rendering

```csharp
if (!OperatingSystem.IsWindows()) return;
ICanvasRenderer renderer = new WindowsConsoleRenderer();
renderer.Render(canvas);       // origin of the visible window
renderer.Render(canvas, 2, 1); // offset relative to the visible window
```

The canvas must fit entirely in the visible window. `GetViewportSize()` returns the current size. After a resize, recreate the canvas and redraw. The renderer does not resize the Console, does not move/hide the cursor and does not restore the previous contents. It does not close the standard handle it receives from Windows.

A redirected stdout throws `InvalidOperationException`; native API errors throw `Win32Exception`; a resize that clips the frame during the write throws `IOException`. A resize immediately afterwards remains the application's responsibility.

Double buffering: `WindowsConsoleRenderer` remembers the last presented frame and writes, with a single native call, only the changed rectangle; an identical frame writes nothing. To animate, just modify the same canvas and call `Render` at every frame. Resize, scroll and a change of the canvas position or size force a full frame. After external writes (`Console.Write`, `Console.Clear`) call `renderer.Invalidate()`, otherwise cells that did not change in the canvas are not rewritten.

Do not write at the same time with `Console.WriteLine` or with loggers on stdout: the output could overlap the canvas. The renderer serialises its own calls; the canvas is not thread-safe, so do not modify it during `Render`.

Native verification: `dotnet run --project samples/csharp/Demos -- --smoke` animates 180 frames, reads the window back with `ReadConsoleOutputW` and compares it with the canvas (exit 0 ok, 1 rendering error, 2 different contents).

## Dependency injection

With an application that uses `Microsoft.Extensions.Hosting`:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders(); // or configure logging to a file
builder.Services.AddWindowsConsoleCanvas();
using var host = builder.Build();
var renderer = host.Services.GetRequiredService<ICanvasRenderer>();
```

The registration is Windows-only and adds a singleton renderer. The size and lifetime of the canvas are managed by the application. Without DI the renderer uses a null logger; an `ILogger<WindowsConsoleRenderer>` can be passed to the constructor. No mandatory configuration.

## TextConsole, ellipses and sprites

`new TextConsole(writer, reader, interactive)` for output and dialogs; InputBox/Select reject non-interactive input before reading, a non-interactive MessageBox returns OK without a prompt. Numeric selection (several numbers separated by commas). `Canvas.DrawEllipse(cx,cy,rx,ry,ink,filled)`; strict `Capture(x,y,width,height)` and clipped `Blit(region,x,y,transparent)`. `TextCanvasRenderer(writer, viewport)` for plain-stream snapshots; colours are not serialised. No pixel canvas, runtime mouse events or DOS emulation.


## PC graphics example
```csharp
var pc = new PCCanvas(9);
pc.Line(0,0,639,349,14);
pc.Box(10,10,50,30,6,true);
var image = pc.GetImage(10,10,20,20);
pc.PutImage(image,100,100,PCPixelOperation.Xor);
var frame = pc.ToCellCanvas(80,25);
// Supply ICanvasRenderer, then renderer.Render(frame).
```
Double buffering in any mode, including single-page VGA modes 12/13: set `ManualPresent = true`, draw the whole frame, then call `Present()`. Projection shows VisualPage and palette as of the last `Present()`, so partial drawing and palette changes never leak. `ToCellCanvas(destination)` reuses a canvas, so the loop does not allocate:
```csharp
var pc = new PCCanvas(13) { ManualPresent = true };
var screen = new Canvas(160, 50);
while (running)
{
    pc.ClearIndex(1); pc.Ellipse(x, 100, 20, 20, 14, true); pc.SetDac(14, phase, 63, 0);
    pc.Present(); pc.ToCellCanvas(screen); renderer.Render(screen);
}
```
### Truecolor (256-colour VGA)
`AnsiFrameRenderer` shows the visible frame of a PCCanvas with the exact RGB colours of the palette, two vertical pixels per cell (the half block ▀). It writes only the cells that changed compared with the previous frame, in a single `Write` wrapped in synchronized output; it saves and restores the cursor and resets the colours at the end of the frame. It requires a VT terminal with truecolor (Windows Terminal, a recent conhost with VT enabled, Linux/macOS terminals) and UTF-8 output.
```csharp
AnsiFrameRenderer.TryEnableVirtualTerminal();          // Windows: enables VT on stdout; elsewhere true
Console.OutputEncoding = System.Text.Encoding.UTF8;     // for the ▀ character
var ansi = new AnsiFrameRenderer(Console.Out);
var pc = new PCCanvas(13) { ManualPresent = true };
while (running)
{
    DrawFrame(pc); pc.Present();
    ansi.Render(pc, columns: 160, rows: 50);            // 160x100 pixels sampled from 320x200; 320x100 cells = full resolution
}
```
`Render` returns the cells written (0 if the frame is unchanged). Position and size are part of the frame: changing them forces a full frame. After external writes to the terminal call `Invalidate()`. Limits: 1..1000 columns, 1..500 rows. Not thread-safe.

Low-level access as QBasic programs do it (2.2.0): `pc.WriteVideoMemory(offset, value)` is `DEF SEG = &HA000: POKE offset, value` (B800 for CGA); `pc.WritePort(0x3C8, index)` then three `pc.WritePort(0x3C9, component)` (0..63) is the classic DAC palette write; `pc.WritePort(0x3C4, 2); pc.WritePort(0x3C5, planes)` selects the planes written in modes 7-12. Unsupported ports throw.

Use Point/PixelSnapshot/GetRgb for exact indexed data and GetImage/PutImage for indexed sprites. Sprites can also be built from data without intermediate copies: `new PCImage(16, 16, 256, buffer.AsSpan(offset, 256))` (also `CanvasRegion(width, height, ReadOnlySpan<Cell>)`). PCCanvas is not a Canvas (2.0.0): project it with ToCellCanvas before passing it to an ICanvasRenderer. ReadByte/WriteByte expose the supported VRAM layouts. See SPEC.md for all modes, palette codes and operations.

### Devices: window, text, music, keyboard, sprites
The devices that QBSharp uses for QBasic programs, usable directly from C# (Windows for the window and for `Beep`).
```csharp
using var window = new PixelWindow("My game");               // a pixel window on its own UI thread
var pc = new PCCanvas(9);
var text = new PCTextDisplay { UseIbmFonts = true };          // FontDirectories: folders searched before the included ROMs
text.Locate(1, 1); text.Write("HELLO", 15, 0, pc);             // raster text in the framebuffer (EGA 8x14 font in SCREEN 9)

var sprite = new int[64];                                     // the LONG array of GET/PUT
pc.Box(0, 0, 8, 8, 14, fill: true);
PCSprite.Capture(pc, sprite, 0, 0, 7, 7);                     // GET (0,0)-(7,7), sprite&
PCSprite.Draw(pc, sprite, 100, 50, PCSpriteOperation.Xor);    // PUT (100,50), sprite&, XOR

var speaker = new PCSpeaker(PCSpeaker.Beep);                  // or PCSpeaker.Silent
foreach (var tone in new PCMusic().Parse("MBT180O3L8CDEFG"))  // PLAY: MB in the background
    await speaker.PlayAsync(tone, default);

while (!window.Closed)
{
    window.Present(pc);                                       // at most one capture every 33 ms
    string key = window.ReadKey();                            // "" if no key; NUL + scan code for extended keys
    if (key == "\x1b") break;
    Thread.Sleep(15);
}
await speaker.DrainAsync(default); speaker.Stop();
```
`PCKeyboard.Encode(Console.ReadKey(true))` gives the same codes from the Console. `PCSprite.Capture` returns the words written and does not touch the others.

### Composing frames yourself: PutPixels and IntegerScale

`PutImage` goes through `PSet` pixel by pixel. A game that composes its own frame in a `byte[]` of colour indices should write it with one call:

```csharp
var pc = new PCCanvas(12);                       // 640x480, 16 colours
pc.PutPixels(frame, 0, 0, 640, 480);             // clipped; same video memory as PSet; 5 ms per frame in Release
```

`PixelWindow` fills the window at 4:3 by default (right for the EGA modes, whose pixels are not square). For square-pixel modes ask for whole
multiples only, so every pixel has the same size, and open the window large enough for the multiple you want (the size is the outer size):

```csharp
using var window = new PixelWindow("My game", windowWidth: 1296, windowHeight: 1000) { IntegerScale = true };   // 2x of 640x480
```

`PixelWindow.FitInteger(width, height, sourceWidth, sourceHeight)` gives the target rectangle; a window smaller than the source falls back to
the 4:3 fit. The window declares itself DPI aware, so on scaled displays its client area is in physical pixels instead of being stretched by Windows.
