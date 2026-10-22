# Roadmap

> This is the log of what has been done and the early list of possible steps. Where the project is going now: [ROADMAP.md](../../ROADMAP.md) in the repository root.

## Implemented

- [x] A .NET 10 XY cell canvas.
- [x] BMP characters and the 16-colour Console palette.
- [x] Points, multi-line text, lines, rectangles, fills and clipping.
- [x] A native Windows renderer, separate through an interface.
- [x] Optional DI and logging, a demo, documentation.
- [x] Renderer double buffering: a reused back buffer, a diff against the presented frame, one native write per frame.
- [x] PCCanvas without per-pixel allocations; O(1) palette and page flip.
- [x] Logical double buffering of PCCanvas (ManualPresent/Present) in VGA modes too.
- [x] A truecolor half-block AnsiFrameRenderer with a per-cell diff.
- [x] IBM CP437 fonts (PCFont from a raw ROM or OEM .fon) and text in graphics modes (DrawGlyph, ScrollUp).
- [x] The IBM EGA 8x8/8x14 and VGA 8x16 ROM fonts are included (CC BY-SA 4.0), `PCVideoMode.Font`.

## Possible next steps

- An interactive session with cursor and buffer management and restoration.
- Keyboard/mouse input and resize notifications.
- Layers, sprites and compositing.
- Integration tests on a real Windows Console.
- A cross-platform ANSI backend and RGB colours.

## Out of scope of the first version

Pixel graphics, UI widgets, automatic layout, input/event loop, emoji and full Unicode width handling, Linux/macOS support of the renderer, automatic publishing to NuGet. None of these is needed to use the XY model right away.

## Devices split out of QBSharp (2026-10-22)
Moved into the engine: `PixelWindow` (formerly QBSharp.Cli `WindowsPixelPresenter` + `WindowsBackBuffer`, title in the constructor), `PCTextDisplay` (formerly `QBasicTextDisplay`; `FontDirectories` instead of `FontDirectory` + `QBS_FONT_DIR`), `PCTone`/`PCMusic` (formerly `QBasicTone`/`QBasicMusic`), `PCSpeaker` (formerly `QBasicSpeaker`, now public, with the `Beep`/`Silent` backends formerly `QBasicConsoleDevices`), `PCKeyboard` (formerly `QBasicKeyboard`), `PCSprite` (the `QBasicSpriteCodec` algorithm on `Span<int>`, enum operations). The `samples/csharp/Gorilla/Engine.cs` replica was removed. The device tests moved from QBSharp.Tests to Screen9.Tests (`DeviceTests`). Screen9 214 tests, QBSharp 714 from PowerShell, Gorillas `--selftest` and `--smoke` succeeded. Not verified: real audio (`--sound`, `QBS_QBASIC_AUDIO=1`). Open: `PCTextDisplay` still uses `ConsoleColor` for attributes; the `PixelWindow` key map covers only arrows/Home/End/PgUp/PgDn/Ins/Del (no F1-F12, unlike `PCKeyboard`).

## CGA palette and cell ANSI renderer (2026-10-22)
Added `CgaPalette` (also used by `PixelWindow` for text) and `AnsiCanvasRenderer` (a cell Canvas in CGA truecolor, per-cell diff, synchronized output). 217 tests, including the comparison on an emulated VT terminal. Not verified: terminals without truecolor (colours would be approximated by the terminal).

## Python with PySharp (2026-10-22)
`samples/python/Gorilla`: `gorillas.py` run by PySharp (`PyGorillas.cs`) with the engine types injected. Added to `PCSprite` the `Capture`/`Draw` overloads on `int[]`, needed by scripting hosts that bind by reflection (`Span`s are not reachable). In PySharp the CLR binder now accepts omitted optional parameters (`= default`, `CancellationToken`, enums). The Python self-test is identical to the C# one on 5 seeds; a window smoke test succeeded. 218 tests.

## The screen9 package for PySharp (2026-10-22)
The Python Gorilla no longer has a C# host: `pysharp run samples/python/Gorilla/gorillas.py`, with the `screen9` package installed by PySharp's mini-pip (`pysharp install screen9 --find-links ops/artifacts/python`). The wheel (`ops/build-python-wheel.cs`) contains `src/Screen9.Python/screen9/__init__.py`, `Screen9.dll` with its dependencies and the font licences. In the engine: synchronous `PCSpeaker.Play`/`Drain` for scripts and game loops. In PySharp: the `clr` module (AddReference, importable .NET namespaces, `System` always available), .NET exceptions as Python exceptions (OperationCanceledException -> KeyboardInterrupt), .NET methods as delegates, multi-argument indexers and .NET arrays, a mini-pip with local wheels, `--find-links` and uninstallation of the previous version, script options in `sys.argv`. The Python self-test is identical to the C# one (seeds 1990, 1, 42); a window smoke test succeeded. 218 Screen9 tests.

## Screen9/QBSharp on the web, Blazor Server (2026-10-22)
New `src/Screen9.Web`: the same C# Gorillas (`GorillasCore`, extracted from `samples/csharp/Gorilla/Gorillas.cs`, previously file-based only), GORILLA.BAS through `QBasicEngine` and the QBSharp editor (`EditorController`/`EditorApplication` unchanged, plus a second Monaco editor directly on `EditorRunner`/`EditorAiWorkflow`) in four Blazor Server pages. In the engine: `PngFrame` (PNG from `PCCanvas`/`Canvas`, for hosts without a native window). Everything else is reuse: `PixelWindow.Capture` for the pixels, `CgaPalette`, the same `IEditorScreen`/`IEditorInputSource` interfaces already designed for this. Tailwind CSS (standalone CLI) without Blazor's default Bootstrap layout; JavaScript in ES6 classes. A temporary per-session folder for Save/Open of the TUI editor (known limit: the Open dialog starts empty, a name without a path resolves against the process working directory, not the session folder — mitigated by pointing that directory at a dedicated empty scratch folder). Real audio through Web Audio. 263 tests in total (221 Screen9 including 3 new `PngFrameTests`, 42 new `Screen9.Web.Tests` on the keyboard mappers, 714/715 QBSharp, the usual unstable test). `GorillasCore` verified unchanged: the same score/frames/keys of `--selftest`/`--smoke` before and after the split. Not verified: the real interactive cycle in a browser (frame drawing, keyboard, audio) — only the build, automated tests and the HTTP 200 answer of every page without exceptions in the initial render. See [docs/web](../web/README.md).

Follow-up (same day): `IPixelDisplay` in the engine (implemented by `PixelWindow` and by the browser), F5 opens a window in front in the browser instead of the native `PixelWindow` on the server (that was the "dialog behind"), a palette `PngFrame` with a single `byte[]` per frame and one draw in flight at a time, Ralf removed from the site, a Home page with the story, a Library page, a `Gorillas (Python)` page with embedded PySharp and `.py` files runnable from Monaco. `LocalFeed/` in the repository with `PySharp.Interpreter` 2.5.0, registered in `src/Directory.Build.props`. 983 tests (45 in `Screen9.Web.Tests`, including 3 that run `gorillas.py` on PySharp with a fake screen).

## C# scripting as an embeddable language (2026-10-22)
A third embeddable language for Gorillas, next to QBSharp and PySharp: `samples/csharp/Gorilla/gorillas.csx` (the text of `GorillasCore.cs` plus a launch line; a test checks that they match), run by Roslyn scripting. New `src/Screen9.Scripting`: `GameGlobals` (screen, keyboard, waits, speaker as global variables of the script, on `IPixelDisplay`) and `CsxRunner`. A single command-line host (`ops/csx.cs`, on `PixelWindow`) and web (the `/gorillas-csx` page, the browser canvas). The script plays the same match as the C# one (same seed: 252 frames, 18 keys). Monaco chooses the interpreter from the extension (`.bas`, `.py`, `.csx`); user Python and C# are run by the server only in Development or with `Screen9:AllowServerCode=true`. Note: test projects in parallel (`dotnet test Screen9.slnx`) can make the QBSharp tests that spawn processes time out (ConPTY, redirected CLI): they pass on their own.

## Rally-X, a second C# game on the engine (2026-10-22)
`samples/csharp/RallyX` brings the RallyX project (Phaser) to Screen9: mode 12 (640×480, 16 colours), true pixel art doubled, the rules of `solo.js`/`Car.js` in a device-free simulation (`RallyXSim`) that is tested at a fixed step, an autopilot for demo and tests, an abstract host like Gorillas (desktop `RallyX.cs` and the `/rally-x` page). Without the "Naonian Retrogaming Society" logo. The art goes through `ops/build-rallyx-assets.py` (a 16-colour palette, the title with its own palette). For frames `PCCanvas.PutPixels` was born (a block write with the same VRAM as `PSet`); `src/RallyX.Tests` (23 tests: art without the logo, rules, physics, an autopilot that completes a round, rendering, 30 fps in Release). Differences from the original project in `samples/csharp/RallyX/README.md`.

## Integer scaling and DPI awareness of PixelWindow (2026-10-22)
`PixelWindow.IntegerScale` and `PixelWindow.FitInteger` enlarge the image only by whole multiples (all pixels the same size, black bands for the rest; below 1x it falls back to the 4:3 fit); the constructor takes an initial outer window size. The window declares itself DPI aware (`SetProcessDPIAware`), so on scaled displays Windows no longer bitmap-stretches it. Off by default: the EGA modes have non-square pixels and keep the 4:3 fit (Gorillas is unchanged). Rally-X uses it at 2x (1280×960 client). 4 new `FitInteger` tests, 233 Screen9 tests.
