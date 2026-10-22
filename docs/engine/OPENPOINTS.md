## PCCanvas gate
Release build exit 0 / 0 warnings-errors and 60 tests pass. Indexed modes/palettes/pages/memory/shape/sprite/projection implemented; pack and QBSharp integration next. Not full hardware/BIOS/register emulation; a true-colour renderer, mode 10/text, viewport transforms and the QBasic binary sprite format remain open.


## PCCanvas distribution verified
ConsoleLib 1.2.0 packaged in the authorised local feed; the QBSharp PC wrapper integrated in the global tool 0.7.0-alpha.2. ConsoleLib gate: 60 tests / 0 warnings-errors earlier; QBSharp gate: 426 tests and a smoke test of 12 PC assertions + 2 ConPTY on the installed DLL. Full hardware/register/BIOS emulation and a truecolor/pixel renderer remain open and are not promised by the logical framebuffer.


## PCCanvas performance (2026-10-21)
Closed: PSet without allocations (~0.3 ms per 320x200 frame, before ~20 ms and 6.6 MB), O(1) SetDac (a 256-entry cycle ~0.3 ms, before 450–750 ms), O(1) page flip (before 8–13 ms), scanline Paint (~0.65 ms and <100 B, before ~7 ms and 6.7 MB). Public API unchanged. 97 tests. Open: a renderer with front/back buffers and a diff (CreateConsoleScreenBuffer/SetConsoleActiveScreenBuffer), a logical back buffer for VGA modes, an interface for indexed frames + a 24-bit ANSI renderer with half blocks, mode 10/text. The inherited cells project ActivePage, not VisualPage: rendering a PCCanvas as a Canvas shows the drawing page; use ToCellCanvas.


## Renderer double buffering (2026-10-21)
Closed: WindowsConsoleRenderer with front/back buffers (ConsoleFrameBuffer), a single dirty rectangle, no write for identical frames, no steady-state allocation (a 300x100 frame under 2 ms), automatic invalidation on resize/scroll/move and manual with ICanvasRenderer.Invalidate(). 105 tests. The P/Invoke path with a sub-rectangle is not verified automatically on a real Console: manual verification with the demo. Open: a logical back buffer in VGA modes, a 24-bit ANSI renderer for indexed frames, mode 10/text.


## VGA back buffer and native verification (2026-10-21)
Closed: PCCanvas.ManualPresent/Present (logical double buffering for all modes, palette included, without changing PageCount or VRAM) and ToCellCanvas(destination) without allocations; a mode 13 pipeline of draw+present+projection+diff without allocations, under 4 ms. 112 tests. Animated demo; `--smoke` verified on a real Console (Windows 11): 180 frames through the diff, read back with ReadConsoleOutputW identical to the canvas. Not verified: Windows Terminal as the default host vs conhost, resize during the animation. Open: a 24-bit ANSI renderer for indexed frames (VisiblePixels/VisiblePalette are already available internally), mode 10/text.


## Truecolor renderer (2026-10-21)
Closed: AnsiFrameRenderer (256 exact VGA colours, half block, per-cell diff, synchronized output, no steady-state allocation, a 320x100-cell frame under 4 ms). 119 tests, including a VT emulator. `--smoke --vga` smoke test on a real Console: glyph read-back identical to the expected one. Not verified: colour fidelity on screen (ReadConsoleOutputW returns only 16-colour attributes), Linux/macOS terminals, resize during the animation. Open: mode 10 and text mode 0; DI registration of AnsiFrameRenderer; possibly a common interface for indexed-frame renderers.


## 2.0.0: PCCanvas separated from Canvas (2026-10-21)
PCCanvas no longer inherits from Canvas; OnCellChanged and the lazy cell projection were removed. Screen9 118 tests. QBSharp (a copy, not the repository) compiles against 2.0 with TreatWarningsAsErrors; 699/704 tests: 4 pre-existing failures identical with 1.2.0/1.3.0 (Spectre ANSI in redirected output in the test environment) + PCCanvasBindingTests.WrapperUsesPackagedDerivedCanvasAndAllBasicGraphics, which asserts inheritance (line 39) and must be updated when moving to 2.0. Open in QBSharp: update that test and the reference; a single window (QBS_GRAPHICS=ansi with AnsiFrameRenderer, or SCREEN 0 text in the pixel window).


## 2.1.0: span constructors (2026-10-21)
Added PCImage/CanvasRegion(…, ReadOnlySpan<T>) next to the IReadOnlyList<T> overloads; vectorised colour-depth validation (IndexOfAnyInRange) instead of LINQ; GetImage/Capture without a second copy. Not adopted: unsigned parameters (they break the clipping of negative coordinates and .NET's int convention) and arrays only for TextConsole (async methods, QBSharp passes lists). 127 tests. QBSharp does not call these constructors. QBSharp: verified 704/704 Debug and Release from PowerShell; from Git Bash 4 environment failures (TERM turns on ANSI in Spectre even with redirected output: a real defect in Program.cs:32/40; Ctrl+C not delivered in the ConPTY test).


## 2.2.0: CPU video memory and VGA ports (2026-10-21)
The SPEC boundary changed in a limited way: map mask, read map select and the VGA DAC through ports; a CPU view of A000/B800. Still out: latches, set/reset, bit mask, write modes 1-3, attribute controller (3C0), CRTC, 3DA, BIOS. Wired into QBSharp (DEF SEG/PEEK/POKE, OUT/INP; QBasicVideoPortTests). 139 tests.


## 2.3.0: IBM fonts and text in graphics modes (2026-10-21)
The font is a theme of the device (the video card's character generator), not of the language: moved here from QBSharp. `PCFont` (256 CP437 glyphs 8 pixels wide): `FromRom` for raw dumps, `TryParseFon`/`TryParseFnt` for Windows OEM .fon files (NE + FNT 2.00/3.00), `Padded` for taller cells, `Find(cellHeight, folders...)` searching `ibm8xN.rom` then cga80woa/ega80woa/vgaoem, cached per folder. `PCVideoMode.CharacterHeight/TextColumns/TextRows` (8/14/16 rows). `PCCanvas.DrawGlyph` (cell, active page, synchronised video memory) and `ScrollUp` (a band of rows, for VIEW PRINT). No font included in the package. 155 tests. Windows provides only 8x8 and 8x12 OEM: exact EGA 8x14 and VGA 8x16 only with .rom dumps; without them, a centred 8x12 (extended blocks/lines 0xB0–0xDF). Cursor, wrapping, control codes and the no-font fallback stay with the caller. QBSharp updated to 2.3.0: 720/720 from PowerShell.


## 2.3.1: text and font fixes (2026-10-21)
Review of 2.3.0: DrawGlyph overflowed with huge columns/rows (e.g. column 2^29 drew at (0,0)), now 64-bit coordinates; ScrollUp rewrote every pixel (1.5–4.7 ms per text row in modes 1/9/12), now block copies equivalent to the per-pixel model (tested on all modes, non-visible EGA pages, CGA padding); PCFont.Find without folders outside Windows no longer searches ./Fonts; TryLoad handles ArgumentException/NotSupportedException. 180 tests Debug/Release. Not verified: real behaviour on Linux/macOS.


## 2.4.0: IBM font ROMs included (2026-10-21)
Included as resources the IBM EGA 8x8, EGA 8x14 and VGA 8x16 ROMs (`src/Screen9/Fonts/ibm8xN.rom`), converted from the Bm437 .FON files of The Ultimate Oldschool PC Font Pack v2.2 by VileR (int10h.org), archive SHA-256 ec0aad90…2933. CC BY-SA 4.0 licence for the font files only: `Fonts/LICENSE.TXT` and the attribution `Fonts/SOURCE.TXT`, both in the package under `fonts/`. API: `PCFont.Ega8x8/Ega8x14/Vga8x16`, `PCFont.ForHeight(8|14|16)`, `PCVideoMode.Font`. `Find` unchanged for external fonts. 181 tests Debug/Release. QBSharp uses `PCFont.ForHeight` and no longer contains fonts.


## 2.4.1: font licence in consumers' output (2026-10-22)
`Fonts/LICENSE.TXT` and `SOURCE.TXT` also as contentFiles (`Screen9.Fonts/`, copyToOutput): whoever redistributes the DLL with the fonts (e.g. the qbs tool) receives them alongside. For indirect references `PrivateAssets="none"` is needed on the intermediate reference, as in QBSharp. Code unchanged, 181 tests.

## Devices split out of QBSharp (2026-10-22)
Moved into the engine: `PixelWindow` (formerly QBSharp.Cli `WindowsPixelPresenter` + `WindowsBackBuffer`, title in the constructor), `PCTextDisplay` (formerly `QBasicTextDisplay`; `FontDirectories` instead of `FontDirectory` + `QBS_FONT_DIR`), `PCTone`/`PCMusic` (formerly `QBasicTone`/`QBasicMusic`), `PCSpeaker` (formerly `QBasicSpeaker`, now public, with the `Beep`/`Silent` backends formerly `QBasicConsoleDevices`), `PCKeyboard` (formerly `QBasicKeyboard`), `PCSprite` (the `QBasicSpriteCodec` algorithm on `Span<int>`, enum operations). The `samples/csharp/Gorilla/Engine.cs` replica was removed. The device tests moved from QBSharp.Tests to Screen9.Tests (`DeviceTests`). Screen9 214 tests, QBSharp 714 from PowerShell, Gorillas `--selftest` and `--smoke` succeeded. Not verified: real audio (`--sound`, `QBS_QBASIC_AUDIO=1`). Open: `PCTextDisplay` still uses `ConsoleColor` for attributes; the `PixelWindow` key map covers only arrows/Home/End/PgUp/PgDn/Ins/Del (no F1-F12, unlike `PCKeyboard`).

## PutPixels, integer scaling, DPI (2026-10-22)
Added `PCCanvas.PutPixels` (a block write with the same video memory as `PSet`), `PixelWindow.IntegerScale`/`FitInteger` and DPI awareness. Not verified by hand: the integer-scaled window on a scaled (125–200%) display was exercised only through the unit tests of `FitInteger` and a smoke run. Open: `PixelWindow` still opens at a fixed outer size unless the caller passes one, and does not react to a DPI change while running (per-monitor v2 awareness is not requested).
