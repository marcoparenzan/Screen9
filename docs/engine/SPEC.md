# Screen9 SPEC


## PCCanvas — Screen9 1.2.0

**2.0.0: `PCCanvas` is a standalone class, no longer derived from `Canvas`** (it was in 1.2/1.3). It has its own Width/Height and only indexed operations; the 16-color cell view exists only through `ToCellCanvas`, and truecolor output through `AnsiFrameRenderer`. `Canvas.OnCellChanged` was removed. Migration: code that passed a PCCanvas where a Canvas was expected must project it (`pc.ToCellCanvas(...)`) first; cell text (`DrawText`) on graphics must be rasterized by the caller (as QBasic PRINT does).

| Mode | Adapter | Pixels | Colors | Pages |
|---|---|---|---|---|
| 1 | CGA | 320x200 | 4 | 1 |
| 2 | CGA | 640x200 | 2 | 1 |
| 7 | EGA | 320x200 | 16 | 8 |
| 8 | EGA | 640x200 | 16 | 4 |
| 9 | EGA | 640x350 | 16 | 2 |
| 11 | VGA | 640x480 | 2 | 1 |
| 12 | VGA | 640x480 | 16 | 1 |
| 13 | VGA | 320x200 | 256 | 1 |

Modes immutable per instance; mode0/text/10/other BIOS modes rejected. EGA page counts model a 256KiB adapter; VGA modes provided have one page. ActivePage selects drawing/Point/GetImage, VisualPage selects projection, independent and range checked.

APIs: PSet(x,y,index) clipped Boolean; Point(x,y[,page]) index/-1 outside; ClearIndex; Line endpoints/clipped; Box x/y/width/height with optional fill; Ellipse integer radii/fill; Circle double radius/aspect with optional counterclockwise arcs in radians0..2pi (wrapped end<start supported, no QBasic negative-angle radial lines). Paint4-connected region or border fill bounded and cancellable. GetImage strict/copied PCImage, PutImage clipped Copy/Preset/Xor/And/Or and transparent index (equal color depths required). PutPixels(indices,x,y,width,height) writes a block of color indices to the active page in one call, clipped, with the same video memory as PSet (the fast path for games that compose the frame themselves: 5 ms for 640x480 in mode 12 against 45 ms for PutImage in Release). Snapshot copies indexed pixels, never exposes mutable storage. PCImage is logical bytes, not QBasic GET Long-array format. Since 2.1.0 `PCImage` and `CanvasRegion` also have `ReadOnlySpan<T>` constructors (arrays, slices, stackalloc); the `IReadOnlyList<T>` constructors remain, with identical validation, and both always copy. Coordinates and indices stay `int`: negative coordinates are valid input and are clipped.

Palette: CGA family0 green/red/brown or1 cyan/magenta/gray, bright flag and background16-color index; direct palette writes mode1 only index0, mode2 black background/programmable foreground. EGA SetPalette(index,hardwareCode0..63) maps RGB 2-bit/channel, standard16 initial colors. VGA SetDac(index,r,g,b) six-bit channels0..63, GetRgb returns RGB8; SetPalette hardware code not accepted in VGA. VGA default palette deterministic: 16 PC colors +16 grayscale +216 six-bit cube +8 black, **not BIOS ROM-exact**. ResetPalette preserves pixel indices; palette/active page refresh cell projection.

ReadByte/WriteByte(address[,plane]): CGA16KiB even/odd scanline banks at0/8192 (2bpp mode1/1bpp mode2), EGA/VGA planar64KiB per plane MSB-first bits (4 planes16 colors, one plane mono), mode13 linear64KiB (64000 visible bytes). Physical page offsets use width/8*height per plane. Drawing and memory writes synchronized, inactive page writes supported, unused padding bytes preserved. **2.2.0 CPU view and ports:** `VideoSegment` (A000 EGA/VGA, B800 CGA), `VideoWindowBytes` (64 KiB, CGA 32 KiB mirroring 16 KiB), `ReadVideoMemory`/`WriteVideoMemory(offset)` as seen through DEF SEG/PEEK/POKE. Planar writes copy the byte to every plane enabled by the map mask (port 3C5 index 2, default all), reads return the plane chosen by read map select (3CF index 4, default 0). `WritePort`/`ReadPort` support only 3C4/3C5, 3CE/3CF (EGA/VGA; other register indices are stored without effect) and the VGA DAC 3C6-3C9 (write index, read index, auto-increment, 6-bit components, state on 3C7; PEL mask stored but not applied). Any other port throws ArgumentOutOfRangeException. VGA DAC has 256 six-bit registers; modes 11/12 map logical colors through the default attribute controller (16 colors: 0-5, 20, 7, 56-63; mode 11: 0, 63), so `SetDac(color)` and DAC ports stay consistent. **Still not emulated: latches, set/reset, bit mask, rotate, write modes 1-3, chain-4 plane interleave, attribute controller writes (3C0), CRTC, input status (3DA), BIOS/CPU/timing/scanline.**

`ToCellCanvas(cols,rows,token)` projects VisualPage with nearest-neighbor sampling and nearest of16 ConsoleColor RGB values; max300x100, cells have space+background color. Render the returned Canvas with ICanvasRenderer. Native Windows text rendering cannot reproduce all256 VGA colors or pixel-resolution graphics. Exact indices and RGB are available independently for future renderers. `ManualPresent`/`Present()` add logical double buffering for every mode: projection reads a copy of VisualPage plus palette taken at the last Present (enabling presents immediately; Present without it throws InvalidOperationException). `ToCellCanvas(Canvas destination)` fills an existing canvas with the same 1..300 x 1..100 limits. PageCount, VRAM layout and inherited cells (active page) are unchanged. `AnsiFrameRenderer(TextWriter)` renders the visible frame (VisualPage or last Present) with exact palette RGB via SGR 38;2/48;2, upper half block U+2580 (foreground = upper pixel, background = lower pixel; space when equal), nearest-neighbor sampling to columns x 2*rows pixels. Per-cell diff against the last emitted frame; one Write per frame wrapped in CSI ?2026h/l, DECSC/DECRC and SGR 0. Target (columns, rows, x, y) change or Invalidate forces a full frame. Class not thread-safe, no framebuffer-wide rollback if canceled mid-fill/blit, but validation occurs before mutation. No DOS memory/audio/input emulation.

```csharp
var pc = new PCCanvas(9);
pc.Line(0,0,639,349,14);
pc.Box(10,10,50,30,6,true);
var sprite = pc.GetImage(10,10,20,20);
pc.PutImage(sprite,100,100,PCPixelOperation.Xor);
var frame = pc.ToCellCanvas(80,25);
// renderer.Render(frame); renderer is supplied by the host.
```

Build/test: solution src/Screen9.slnx, net10.0/C#14. PCCanvasTests plus prior Canvas/runtime suites: 60 tests passed at initial implementation. No live hardware/desktop validation claimed; pure framebuffer tests deterministic. Package 1.2.0 intended for a local NuGet feed and the QBSharp PC wrapper. Gorillas syntax compatibility is separate.

## Text in graphics modes — 2.3.0

`PCVideoMode.CharacterHeight` is the height of the BIOS text cell: 14 for 350 rows (mode 9), 16 for 480 (modes 11/12), 8 otherwise; `TextColumns = Width/8`, `TextRows = Height/CharacterHeight` (25 or 30).

`PCFont`: 256 glyphs 8 pixels wide, index = CP437 byte, row = a byte with bit 7 on the left. `FromRom(data, height)` requires exactly 256×height bytes (height 1..32), otherwise ArgumentException. `TryParseFon` reads the first RT_FONT (0x8008) of an NE executable; `TryParseFnt` accepts FNT 2.00/3.00 only when all glyphs are 8 wide; both return null on unrecognised data, with no exceptions and no reads outside the buffer. Codes outside the file's first..last stay empty. `Padded(height)` centres the glyphs; 0xB0–0xDF repeat the first/last row in the added rows, so boxes and blocks stay joined. `TryLoad(path)`: .fon, .rom (height = length/256) or FNT. `Find(cellHeight, folders...)` only for 8/14/16: for each non-empty folder `ibm8xN.rom`, then `cga80woa.fon` (8), `ega80woa.fon` (14) or `vgaoem.fon` (16), reduced with Padded; without folders it uses Windows\Fonts. Results (null too) are cached per folder and height for the whole process. Windows provides only 8x8 and 8x12 OEM: 8x14/8x16 are approximated without a .rom dump. No font is included or redistributed by this method.

`PCCanvas.DrawGlyph(column, row, code, foreground, background, font)`: the 0-based cell of 8×font.Height pixels on the active page, lit pixels foreground and unlit ones background, video memory synchronised as in PSet, clipping at the edges (2.3.1: cell coordinates are computed in 64 bits, off-screen cells draw nothing even for huge values); code 0..255 and colours validated. `ScrollUp(top, height, lines, fill)`: moves the band [top, top+height) of the active page up by `lines` pixels and fills the bottom with `fill`; lines beyond height clears the band; a band off screen throws ArgumentOutOfRangeException. Not handled: cursor, wrapping, interpretation of control codes, blinking attributes, text mode 0 (they stay with the caller, e.g. QBasic PRINT).

2.3.1: `ScrollUp` block-copies indices and VRAM rows in the layout of the mode (linear, planar per page, CGA banks with padding preserved): the result is identical to the per-pixel model, about 0.01 ms for a full-screen text row even in planar modes. `PCFont.Find` without folders outside Windows returns null instead of searching `Fonts` in the working directory; `TryLoad` returns null for invalid paths too.

**2.4.0 — included fonts.** `PCFont.Ega8x8`, `PCFont.Ega8x14`, `PCFont.Vga8x16`: IBM ROMs loaded lazily from embedded resources (`Screen9.Fonts.ibm8xN.rom`), shared instances. `PCFont.ForHeight(h)` for h = 8, 14, 16, otherwise ArgumentOutOfRangeException. `PCVideoMode.Font` = `ForHeight(CharacterHeight)`. Source: The Ultimate Oldschool PC Font Pack v2.2 by VileR, files Bm437_IBM_EGA_8x8, Bm437_IBM_EGA_8x14, Bm437_IBM_VGA_8x16; CC BY-SA 4.0 licence for the font files (`fonts/LICENSE.TXT`, `fonts/SOURCE.TXT` in the package), the Screen9 code does not change licence. `Find` remains for external fonts and does not use the included fonts.
