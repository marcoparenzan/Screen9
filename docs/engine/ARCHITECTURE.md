# Architecture

## Structure

- `Screen9.slnx` (root): the single solution, with the folders `engine`, `qbsharp` and `samples`.
- `src/Screen9`: a net10.0 library, with nullable and implicit usings enabled.
- `samples/csharp/Demos`: a demonstration application with a Generic Host (cell and VGA scenes, a smoke test on a real Console).
- `samples/csharp/Gorilla`: a port of GORILLA.BAS as a .NET 10 file-based app (`Gorillas.cs`, a library with `#:project`), on the engine devices.
- `src/Screen9.Tests`: xUnit tests of the model and of the DI registration.
- `ops/artifacts`: the optional destination of the local packages produced with `dotnet pack`.

## Model and rendering

`Cell` is an immutable record struct. `Canvas` holds an array of rows: O(width × height) memory. The indexer enforces valid coordinates; the primitives apply clipping. Fills and rectangles use widened arithmetic to avoid overflow; lines use Liang–Barsky and then Bresenham. This avoids walking billions of points for segments with very distant endpoints. Clipped geometric endpoints are rounded to the nearest cell before rasterisation: the result may differ from a line rasterised outside and then clipped.

`ICanvasRenderer` decouples the model from the terminal. `WindowsConsoleRenderer` queries the visible window of the stdout buffer, translates the cells into UTF-16 `CHAR_INFO` and 16-colour attributes, then calls `WriteConsoleOutputW`. The drawing origin is relative to the visible window, not to the whole scrollback. The P/Invoke structures keep the Win32 layout and the native coordinates are signed shorts.

The renderer double-buffers with `ConsoleFrameBuffer`: cells are converted into a reused `CHAR_INFO` back buffer (no copy of the canvas, no steady-state allocation), compared row by row with the front buffer (the last presented frame) and written with a single `WriteConsoleOutputW` limited to the rectangle that contains all the changes; an identical frame writes nothing. After a successful write the buffers swap. The window origin or size and the canvas offset and size are part of the frame target: if they change, the next frame is complete; a native error or a resize during the write invalidates the front buffer. Writes to the Console from outside the renderer cannot be detected: call `ICanvasRenderer.Invalidate()` (an interface method with an empty default implementation, so it does not break existing implementers). The renderer serialises Render/Invalidate with a lock. It does not use `SetConsoleActiveScreenBuffer`: it does not change global terminal state and does not require a disposable session. Alternating different canvases at different positions with the same renderer stays correct but rewrites full frames.

The library's dependencies are limited to the Microsoft.Extensions DI and logging abstractions; the host and the configuration providers are in the demo. The library does not impose a host on the caller.

## Tests and limits of verification

The 27 xUnit cases cover the in-memory model, including extreme coordinates, atomic text validation, lines in several directions, clipped rectangles and DI. They do not invoke a native write on a real terminal: visual layout, fonts, Windows Terminal and concurrent resizes require manual verification with the demo.

## 1.2.0 extensions

TextConsole is independent of Spectre and handles an output stream + numeric dialogs/cancellable line input, with control-character sanitisation. TextCanvasRenderer serialises monochrome snapshots without emulating persistent positioning. The ellipse is rasterised only in the viewport (O(visible area) cost, even with extreme radii), the inner contour through neighbours; regions are immutable with a defensive copy and Blit with clipping/char transparency, no bitmap XOR. UI/Script binding and JSON mapping live in the QBSharp adapter, not in Screen9.


## PCCanvas storage architecture
From 2.0.0 PCCanvas does not derive from Canvas: cells (Canvas) and indexed pixels (PCCanvas) are distinct models, connected only by explicit projections. PCCanvas stores byte indices per page plus CGA-interleaved/planar/linear 64 KiB memory backing; each indexed write updates index and VRAM, nothing else. Active/visual pages separate, descriptors get-only. Palette changes update a per-index Cell cache in O(1); indices remain stable. ToCellCanvas samples the visible frame and maps RGB to 16 terminal colours. PCImage immutable indexed data is separate from CanvasRegion. No registers/latches/CPU emulation, no true-colour terminal renderer. The API contract is in SPEC.md; deterministic tests are in PCCanvasTests.


## Shared rasterisation
The rasterisers (Liang–Barsky/Bresenham line, rectangles, ellipse) live in `Raster`, generic over a plotter struct: Canvas and PCCanvas share the same clipping without closures or per-pixel allocations, while being independent classes. SetDac and page flip are O(1); Paint is a 4-connected scanline fill without a visited map. Contracts are in PCCanvasContractTests, budgets in PerformanceTests; `[GapFact]` marks specifications of known gaps, run only with `CONSOLELIB_GAPS=1`.


## AnsiFrameRenderer
It reads the visible frame from PCCanvas (the presented indices and palette, internal to the assembly) and samples it at columns x 2*rows pixels. It keeps, per cell, the 24-bit RGB of the upper and lower pixel of the last emitted frame (uint.MaxValue = unknown) and emits only the changed cells: CUP only if the cell is not contiguous with the previous one, SGR only if the colour differs from the current state in the frame. The output is built in a reused StringBuilder and written with a single Write: no steady-state allocation. Frame envelope: CSI ?2026h, DECSC, ..., SGR 0, DECRC, CSI ?2026l. The tests apply the output to a minimal VT emulator and compare the resulting screen with the expected projection; the `--smoke --vga` demo reads back a real Console.

## Devices
The engine contains the PC devices that QBSharp used to emulate on its own; the language stays in QBSharp. `PixelWindow` (with an internal `PixelWindowBackBuffer`) has a UI thread with its own message queue: `Present` records the latest PCCanvas or text Canvas, `Refresh` captures an immutable snapshot of it at most every 33 ms, `WM_PAINT` composes it off-screen (black bands, StretchDIBits) and copies it with a single BitBlt; keys arrive in a bounded queue already encoded as INKEY$. `PCTextDisplay` keeps the text cells and, if it receives a PCCanvas, draws every character with `DrawGlyph` (IBM ROM when the cell is 8 pixels wide; `PCFont.Find` only if `FontDirectories` is not empty, so as not to pick up Windows .fon files) or with the compact 5x7. `PCSpeaker` is a Channel bounded to 32 tones consumed by a task: background tones return as soon as they are queued, foreground ones wait for their own playback. `PCSprite` reads and writes 32-bit words (`Span<int>`), so QBSharp copies its LONG array and C# passes an `int[]`. The engine does not read `QBS_*` environment variables: configuration stays with the host.

## Block writes and integer scaling
`PCCanvas.PutPixels` copies a block of colour indices into the active page in one call and updates the video memory exactly as `PSet` does (a row copy for the index array, then the per-pixel VRAM encoding); it is the fast path for games that compose a whole frame themselves (5 ms for 640x480 in mode 12 against 45 ms for `PutImage`, which goes through `PSet`). `PixelWindow.IntegerScale` makes the presenter enlarge the image only by whole multiples (`FitInteger`, falling back to the 4:3 fit when the window is smaller than the source); the window also declares itself DPI aware so that Windows does not bitmap-stretch it on scaled displays.
