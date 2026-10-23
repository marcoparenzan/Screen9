# Screen9 on the web

> The programs that draw a screen run in the browser as WebAssembly: **[WASM.md](WASM.md)** is the detailed document (design, async games, PySharp `RunAsync`, AOT numbers, build/run/debug, how to add a page); the site's `/wasm` page is the short, reader-facing version and `/architecture` the overview.

`src/Screen9.Web`: Blazor Server (interactive server render mode, .NET 10). The same Screen9 engine and the
same QBSharp interpreter/editor used on the desktop, served to a browser — each tab is an isolated Blazor
circuit, that is an independent session.

**Hybrid: the screens run in the browser.** Everything that draws a screen in real time (the C# games, the demos
and `GORILLA.BAS`) is a WebAssembly component in `src/Screen9.Web.Client` (`InteractiveWebAssembly`, no
prerender), embedded in an otherwise server-rendered page: the program, its frames and its keys never leave the
browser, so there is no SignalR round trip per frame. The server keeps what needs it: the C# script
game (Roslyn is large and slow to load in WebAssembly), the character-grid editor (`/editor`), and the pages
themselves. Measured on the plasma in headless Edge: ~22 fps with the interpreted .NET runtime and 60 fps (1.1 ms
per step) with AOT, which `dotnet publish` turns on (`RunAOTCompilation`, needs `dotnet workload install
wasm-tools`); `dotnet run`/`build` stay interpreted and slower. Rally-X holds its 30 fps and Gorillas plays
normally.

## Starting it

```powershell
# one-off: download Tailwind's standalone CLI (no Node/npm)
pwsh src/Screen9.Web/tools/fetch-tailwind.ps1
dotnet run --project src/Screen9.Web
```

The site is in English. The header has three drop-down menus, all read from `SiteNavigation.cs`: **Screen9** (hangs from the logo: Home, Mode 9 history, Library), **Gorilla Scripted** (Gorillas as QBasic, Python and C# script, plus the QBasic and Monaco editors) and **C# Games** (Gorillas, Rally-X, marked work in progress) and **C# Demos** (the plasma and the cell-canvas scene). The Screen9 menu also has the Roadmap. On every page a navigation panel beside the content (above it on narrow screens) lists the pages of the same section with the current one highlighted. The header also links to the GitHub repository. A test checks that every menu entry is a real page and every page is in the menu.

Pages, reachable from the home page (`/` is the introduction, `/history` tells the story of the project, `/library` is the Coding Guide with snippets for mode 9, the old `/libreria` still works):

- **`/gorillas-csharp`** — `GorillasCore` (extracted from `samples/csharp/Gorilla/Gorillas.cs`), unchanged: the same
  `GorillasGame`/`GorillasHost` as the desktop pixel window, only with a different host (no GDI/Win32).
- **`/gorillas-bas`** — the original GORILLA.BAS (1990) run by `QBasicEngine`, wired like
  `qbs run --dialect qbasic` with `QBS_GRAPHICS=pixel`. No file system access (`AllowFileIO=false`).
- **`/gorillas-python`** — `samples/python/Gorilla/gorillas.py`, the same file as `pysharp run`, run **in the browser** by
  PySharp (`PySharp.Interpreter` 2.6.0 from `LocalFeed/`, `PyEngine.RunAsync`): the page passes the screen, the keyboard and
  the speaker as the `web_host` variable (`BrowserPythonHost`), and an in-memory `screen9` module on top of the engine.
- **`/architecture`** — how the site is built: the server, the WebAssembly islands, the async games, the AOT numbers (the page version of the section below).
- **`/history`**, **`/library`**, **`/roadmap`** — the Mode 9 history, the Coding Guide (runnable mode 9 snippets), and the roadmap.
- **`/plasma-demo`**, **`/scene-demo`** — the demos of `samples/csharp/Demos` (`Screen9.DemosCore`): a VGA mode 13 plasma with palette cycling, and the
  cell canvas. Each page shows the code.
- **`/rally-x`** — `samples/csharp/RallyX`: Rally-X in C# (`RallyXGame`, the same as the desktop one), mode 12 at 640×480 and 16 colours.
  Arrows or WASD, Space for smoke, P to pause. See [samples/csharp/RallyX/README.md](../../samples/csharp/RallyX/README.md).
- **`/gorillas-csx`** — `samples/csharp/Gorilla/gorillas.csx`, the game as a C# script, compiled and run by
  C# scripting (Roslyn) in the server with the same devices (`Screen9.Scripting.GameGlobals`).
- **`/editor`** — the full-screen editor of `qbs edit` (`EditorController`/`EditorApplication` unchanged),
  with a new `IEditorScreen`/`IEditorInputSource` for the browser. Keyboard only (no mouse for now).
- **`/editor-monaco`** — a more "browser-like" editor, based on Monaco (CDN) instead of the character-grid UI, **entirely in the browser**: the file list, Run and the program screen are a
  WebAssembly component (see "The Monaco editor in the browser" below). The interpreter is chosen from the file extension: `.bas` → QBSharp (QBasic or QBS with the selector), `.py` → PySharp; `.csx` can be
  edited but runs only on the server page (`?file=gorillas.py`, `?file=hello.bas`). Run executes the editor's text (saved or not) on a screen below the editor; output and errors (QBasic diagnostics, the Python
  traceback, in red) appear in a log. **New** adds a file; its extension (`.bas`, `.py`, `.csx`) picks the runtime.

## How it works (reuse, not duplication)

- **Where a program runs**: `Screen9.IPixelDisplay` (a screen to fill and a keyboard to read). On the desktop
  it is implemented by `PixelWindow`; on the web by `BrowserDisplay`, inside `ProgramDialog` (a modal in front of the page). A
  `PixelWindow` opened by the server would end up in a window on the server, behind the browser: that is why
  `EditorApplication` has configurable `ProgramWindow`/`Audio`/`EnableAi`.
- **Frames in the browser (WebAssembly)**: `Screen9.RgbaFrame` expands the visible `PCCanvas`/`Canvas` into a
  reusable RGBA buffer, `PixelScreen` (`Screen9.Web.Client`) copies it into the canvas `ImageData` through a
  `[JSImport]` (`pixelBlit.js`) — no PNG, no throttle. There is no thread to sleep on in the browser, so the games
  are async: `GorillasGame.RunAsync` and `RallyXGame.RunAsync` await `Delay` on their host (`GameClock.Delay`,
  which yields to the browser even for a zero wait), while the old synchronous `Run()` over `Sleep` is kept for
  the desktop window and the tests (with no `Delay`, every await completes at once). `QBasicEngine` was already
  async: TIMER waits and INKEY$ polls await a delay.
- **Frames from the server** (the grid editor, C# script): `Screen9.PngFrame` encodes a `PCCanvas`/`Canvas` as a palette-indexed PNG (about 6 KB and under
  1 ms for an EGA frame), sent as a single `byte[]` and drawn on a `<canvas>` with `createImageBitmap`;
  only one draw is in flight at a time, the latest frame wins, and a frame skipped by the 30 Hz limit is still
  shown (`Flush`). The same throttling discipline (~30 Hz, unchanged frames dropped) as `PixelWindow` and
  `AnsiFrameRenderer`/`AnsiCanvasRenderer`, without sharing their code. With one draw in flight the rate also
  follows the round trip, so a slow link lowers it by itself (the C# script game reaches ~20 fps on localhost).
- **Keyboard**: `BrowserKeyboard` (towards INKEY$, like `PCKeyboard.Encode`) and `BlazorEditorInput` (towards
  `EditorInput`, like `EditorInputDecoder.DecodeVt`) read the browser's `keydown` events.
- **Audio**: PLAY/BEEP/SOUND tones are played with real Web Audio (`AudioContext`/`OscillatorNode`), not
  silenced.
- **Style**: Tailwind CSS (standalone CLI, no Node dependency in production), no default Blazor Bootstrap
  layout. JavaScript organised in ES6 classes (`PixelCanvas`, `KeyCapture`, `ToneBeeper`,
  `MonacoHost`), not loose functions.

## Running in the browser (WebAssembly)

Why: with Blazor Server every frame went program → server → PNG → SignalR → browser, and every key the opposite way, so a game
could not run faster than the round trip allowed (about 5 frames a second once the web cap was lowered). The programs that draw
a screen in real time now run **in the browser**; the server only serves the page. The same story, with a diagram, is on the site's
**Architecture** page (`/architecture`).

### What runs where

| Page | Runs in | How |
|---|---|---|
| `/plasma-demo`, `/scene-demo` | browser | `PlasmaWasm`, `SceneWasm` |
| `/rally-x` | browser | `RallyXWasm` (`RallyXGame.RunAsync`) |
| `/gorillas-csharp` | browser | `GorillasWasm` (`GorillasGame.RunAsync`) |
| `/gorillas-bas` | browser | `BasicWasm` (`QBasicEngine`, the page passes the source as a parameter) |
| `/gorillas-python` | browser | `PythonWasm` (PySharp `RunAsync`, see below) |
| `/gorillas-csx` | server | Roslyn, PNG frames |
| `/editor-monaco` | browser | `EditorMonacoWasm`: `.bas` and `.py` run in the page, `.csx` does not |
| `/editor` | server | the character-grid editor (`EditorController`), which drives QBasic on the server |

C# script stays on the server because Roslyn is large and slow to load in WebAssembly. Moving it is possible later, not needed now. (Python used to stay on the server too, for want of a
way to wait without a thread: see "Python in the browser" below.)

### Project layout

- `src/Screen9.Web.Client` — `Microsoft.NET.Sdk.BlazorWebAssembly`, references `Screen9`, `Screen9.DemosCore`, `RallyXCore`,
  `GorillasCore`, `QBSharp.QBasic`, `QBSharp.StandardLibrary` and `PySharp.Interpreter` (the package, from `LocalFeed/`). It holds the components above, `BrowserKeyboard`/`BrowserKeyEvent` (namespace `Screen9.Web`, moved from the
  server project) and the shared scripts `keyCapture.js`, `toneBeeper.js` (moved from the server's `wwwroot/js`, same URLs) and `pixelBlit.js`.
- `src/Screen9.Web` references it, calls `AddInteractiveWebAssemblyComponents()` / `AddInteractiveWebAssemblyRenderMode()` and adds the client
  assembly to the router (`AdditionalAssemblies` in `Routes.razor`, `AddAdditionalAssemblies` in `Program.cs`). The packages
  `Microsoft.AspNetCore.Components.WebAssembly(.Server)` are pinned to 10.0.13: 10.0.0 has known high-severity advisories, and the solution treats warnings as errors.
- A server page embeds a component as an island: `<GorillasWasm @rendermode="new InteractiveWebAssemblyRenderMode(prerender: false)" />`. The text of the page
  (description, listing of the code) is static server-rendered; `prerender: false` because a game has nothing to show before it runs.
- `tailwind.css` also scans `../Screen9.Web.Client/**/*.razor`, so the classes used in the client components are generated.

### The screen: `PixelScreen`

`PixelScreen` is the browser counterpart of `PixelDisplay` (which stays, for the server pages). It owns a `<canvas>`, the keyboard
(`ReadKey()`/`FlushKeys()`, INKEY$ strings from `BrowserKeyboard`) and the Web Audio tones (`RequestTone`). A page awaits `screen.Ready`, then calls
`PresentGraphics(PCCanvas)` or `PresentText(Canvas)`:

1. `Screen9.RgbaFrame` expands the visible page (the presented one with `ManualPresent`) into a reusable RGBA buffer, with no allocation per frame;
   for text cells it rasterizes with the VGA 8x16 font and the 16 CGA colours, like `PngFrame.Encode(Canvas)`.
2. `PixelBlit` (`[JSImport]` over `wwwroot/pixelBlit.js`) copies the buffer into the canvas `ImageData` and calls `putImageData`. One canvas per id, so several screens can share a page.

There is no cap and no queue: a frame is drawn when the game presents it. A `byte[]` is passed to JavaScript (one 256 KB copy for a 640×350 frame, negligible);
`Span<byte>` as `MemoryView` was tried first and fails in this runtime with `Assert failed: Expected function Uint8Array`.

### No thread to sleep on: async games

The browser runs .NET on a single thread: a `Thread.Sleep` or a blocking game loop would freeze painting and keys. The games therefore have an async entry point:

- `GorillasGame.RunAsync()` and `RallyXGame.RunAsync()` await `host.Delay` (a `Func<TimeSpan, ValueTask>`); Gorillas also awaits `host.ToneAsync` for a tone that paces the game.
  `host.Sleep` and `host.Tone` are now optional (default `Thread.Sleep` / no sound).
- The old `Run()` is `RunAsync().AsTask().GetAwaiter().GetResult()`. With no `Delay` every wait completes synchronously, so the desktop window (`Gorillas.cs`, `RallyX.cs`), the
  tests and `gorillas.csx` keep working unchanged. `gorillas.csx` is regenerated from `GorillasCore.cs` (a test checks that it carries the game text verbatim).
- In the browser `GameClock.Delay` awaits `Task.Delay`, and for a zero or negative wait it awaits `Task.Yield()`: a game that is behind schedule must still let the browser paint and read keys.
  `GameClock.PlayBackground` plays `PLAY "MB…"` tones one after the other without stopping the game.
- `QBasicEngine` was already async: INKEY$ polls and TIMER waits `await options.Delay`. `BasicWasm` measures the empty-loop speed once on the user's machine (about half a second, after the first paint)
  and sets `TimerWaitScale`, the same calibration the server does (`QBasicRunPolicy.CalibratedTimerWaitScale`, which lives in `QBSharp.Cli` and is not referenced from the browser).
- Python has no game loop to convert at all: PySharp itself learned to wait (below).
- Plasma and the demos have no loop of their own to convert: the component drives `Step()` from `requestAnimationFrame` (`PixelBlit.NextFrame`) or a 33 ms delay.

### Python in the browser: `PyEngine.RunAsync`

`gorillas.py` blocks in `sleep`, and the browser has one thread, so Python needed a way to wait without a thread. .NET WebAssembly threads are experimental
and not supported for Blazor (they also need cross-origin isolation headers), and rewriting PySharp's 3,800-line evaluator as async everywhere would have slowed
every script and touched everything its 1,300 tests cover. PySharp 2.6.0 (`D:\Dev\2026\repos\PySharp`, packed into `LocalFeed/`) instead has a **second,
suspendable evaluator** beside the synchronous one: `PyEngine.RunAsync` runs the same AST on the caller's thread and **awaits** a call to a .NET method that returns
a `Task` (and `time.sleep`). It only re-implements what can contain a call (statements that nest statements, calls, attributes, operators…) and delegates the rest to the synchronous
evaluator through the same helpers, so semantics cannot drift. Details and the list of what does *not* suspend (class bodies, `__init__`, comprehensions, f-strings,
`match`, callbacks of builtins, generators and coroutines) are in PySharp's `ARCHITECTURE.md` §12d.

On the Screen9 side: `BrowserPythonHost` is the `web_host` of `gorillas.py`, the same contract the desktop and the old server host had (`present`, `read_key`, `flush_keys`, `sleep`, `tone`, `background`, `seed`, `checkpoint`), but `sleep` and `tone` **return a `Task`**
(`GameClock.Delay`) instead of blocking; `BrowserPython.RunAsync` builds the engine with the in-memory `screen9` module and calls `engine.RunAsync`; `PythonWasm` is the component
(`PixelScreen` + status line + Python output). `gorillas.py` is the same file as on the desktop and on the server. Checked on the published (AOT) site in headless Edge: the intro (with its
sparkles, which are `sleep`s), names, a shot with the banana in flight — about 44 canvas draws a second during the flight, against a ceiling of about 50 (the game's own 20 ms step).
Interpreted (`dotnet run`) it also plays, only slower. `PySharp.Interpreter` is a package reference of the client project too (`2.6.0`, from `LocalFeed/`).

### The Monaco editor in the browser

`/editor-monaco` is a static server page that embeds one WebAssembly component, `EditorMonacoWasm` (`Screen9.Web.Client`). The server only reads the bundled
samples (`.bas`, `.py`, `.csx` from `samples/`) and hands them over as a parameter; from there everything is in the page:

- **Files**: an in-memory list (the samples, then whatever this browser saved). **Save** writes them as JSON to `localStorage` (`screen9.editor.files`); **Reset** goes back to the samples;
  **New** adds a file whose extension picks the runtime. localStorage can be missing or full (a private window): the editor works without it. Switching file keeps the text of the previous one.
- **Editing**: `monacoHost.js` (moved to the client project, same URL; Monaco itself still comes from the jsDelivr CDN).
- **Run** (the editor's text, saved or not) opens a **program screen below the editor** (a new `PixelScreen` for every run, created through `OnReady`; **Close** hides it, **Stop** cancels):
  `.py` → `BrowserPython` (PySharp `RunAsync`, output and the traceback in the log, errors in red); `.bas` in the QBasic dialect → `BrowserBasic` (`QBasicEngine`, the same runner as the
  GORILLA.BAS page, calibrated once); `.bas` in the **QBS (modern)** dialect → `BrowserQbs` (`ScriptEngine` + `StandardLibraryHost`: `Print`, `Json`, `Ui`, `Screen`…): text in and out, so no
  program screen, the output goes to the log, and `Ui` dialogs are the browser's own (`prompt`/`alert`; `Select` takes a number). No files (`AllowFileIO=false`) and no Ralf.
- **`.csx`** can be edited (it is in the list, and highlighted as C#), but Run says it needs the server: Roslyn is too large to load in the browser. It runs on `/gorillas-csx`.
- **What was dropped** compared with the server version of this page: running `.csx` from the editor, the editor-owned INPUT dialog of `EditorRunner` (INPUT is now on the program screen, as in a window), and the
  per-tab folder on the server. `ScriptLanguages.ServerCodeAllowed` and the `Screen9:AllowServerCode` setting are gone: no user-written code runs on the server any more through this page.

Checked in headless Edge on the dev build (interpreted, so slow): the editor loads with Monaco and the samples; `hello.bas` in QBS prints `Hello, QBSharp` to the log; `gorillas.py` runs from the editor and
takes keys on the program screen; `GORILLA.BAS` runs and shows its first prompt; Stop ends the run. Not checked: `Save` across a reload, `Reset`, `New`, the QBS `Ui` dialogs, the AOT build of the editor.

### AOT: required for speed

.NET in WebAssembly interprets IL unless it is ahead-of-time compiled. Plasma, headless Edge, same machine:

| Build | `Step()` | fps |
|---|---|---|
| Blazor Server (PNG over SignalR) | n/a | ~5 |
| WebAssembly, interpreted (`dotnet run`/`build`) | 28 ms (+10 ms to expand the frame) | ~22 |
| WebAssembly, AOT (`dotnet publish`) | 1.1 ms (+0.2 ms) | 60 |

Drawing on the canvas costs about 0.2 ms either way. Rally-X holds its 30 frames a second with AOT; Gorillas (C# and `.bas`) play normally.
`RunAOTCompilation` is set in the client project and takes effect only on `publish` (from about 3½ to 9 minutes depending on the machine; it compiles the whole runtime; `pwsh ops/run-site.ps1` does a local-use publish in about 3 minutes and runs it, see [WASM.md](WASM.md)); it needs the workload:

```powershell
dotnet workload install wasm-tools
dotnet publish src/Screen9.Web -c Release -o out
dotnet out/Screen9.Web.dll --urls http://localhost:5000     # or deploy the folder
```

In development (`dotnet run`) the games work but run interpreted: use the publish to judge speed. The published `wwwroot` is about 28 MB on disk
(the runtime and the assemblies; brotli-compressed on the wire, not measured on a real network): the first visit to a game page downloads it, later visits use the cache.

### Server pages: frame rate

The pages that stay on the server send an indexed PNG per frame with a ~30 Hz cap (`PixelDisplay.MinFrameIntervalMs`, 33 ms; it had been lowered to 200 ms and was put back).
With one draw in flight and the latest frame winning, the rate follows the round trip by itself. The C# script game reached about 20 fps on localhost.
Sending raw indices plus palette instead of the PNG was measured and rejected: encoding costs 2–7 ms per frame on a worst-case frame of random lines (mode 9: 5.4 ms, 89 KB;
mode 12: 6.6 ms, 118 KB; mode 13: 2.3 ms, 39 KB; real game frames compress far better), while raw frames would be 224 KB, 307 KB and 64 KB: more bytes on the wire
for a small CPU saving. The limit there is the round trip, not the encoder.

The console has nothing to gain from the same change: `AnsiFrameRenderer` already reads the raw indices and the palette of the `PCCanvas` (`VisiblePixels`, `VisiblePalette`)
and writes only the cells that changed, with no encoding step in between. The raw model is the engine's own; PNG exists only for the one transport that needs a file format.

### How it was checked, and what is not

Verified with Edge headless (software rendering) driven over the DevTools protocol on the published (AOT) site: Plasma, Rally-X (title, a match, steering keys, ~30 canvas draws a second),
Gorillas C# and Gorillas `.bas` (intro, names, a shot), with screenshots and frame counters. Automated tests: `Screen9.Tests`, `RallyX.Tests`, `Screen9.Web.Tests`
(including `gorillas.csx` against `GorillasCore`) pass; `QBSharp.Tests` has four failures about ANSI output of the redirected CLI that do not touch this code.
**Not verified**: a real browser with a GPU, sound (Web Audio needs a gesture and was not audible in headless), touch devices, a real network (download size and first-load time),
and navigating away from a game page mid-run (the components cancel their token in `Dispose`, but this was not exercised).

### Pitfalls met while doing it

- `JSImport`/`JSHost` need `AllowUnsafeBlocks`, and raise CA1416 (browser-only API) in a project that is also read by the server: the client project sets `NoWarn CA1416` rather than marking the whole assembly,
  which would flag the server's own use of `BrowserKeyEvent`.
- The Blazor Web App extensions (`AddInteractiveWebAssemblyComponents`…) live in the `…WebAssembly.Server` package, even though the server does not host WebAssembly itself.
- A running `Screen9.Web` locks `Screen9.Web.Client.dll`: stop it before building.

## Screens and comments

Every screen has a **Save PNG** button under it: it downloads the frame as it is on the screen (native pixels, for example 640×350 in mode 9) so it can
be reused. Ready-made screenshots, in two versions (native, and 1280×960 at 4:3), are in `docs/images`, rendered by `ops/make-screenshots.cs`.
The footer of every page links to the repository's GitHub Issues, which is where comments and bug reports go.

## Security: code run by the server

Nothing a visitor writes runs in the server process any more. The Monaco editor runs `.bas` and `.py` in the visitor's browser (WebAssembly, the browser's own sandbox), and `.csx` is not run by it.
What still runs on the server is fixed code included in the site (the C# script Gorillas page, `gorillas.csx`) and the character-grid editor (`/editor`), whose programs are QBasic on the
interpreter (confined: no file access in the game pages). PySharp's Python has `os`, `subprocess`, `socket`: that is harmless in the browser (WebAssembly has no process or sockets to give) and is why it
could not be run server-side on a public host; the old `Screen9:AllowServerCode` switch for that is gone.

## Known limits

- **Saving/opening in the editor**: each circuit has a temporary server folder
  (`SessionWorkspace`), deleted on disconnection — F2/F3 of `qbs edit` are unchanged. The "Open" dialog
  starts empty: a file name typed without a path resolves against the process working
  directory (shared), not against the session folder — mitigated by pointing that working
  directory at a dedicated, empty scratch folder in `Program.cs`, so the worst case is an
  orphan file there, never a collision with another session. Accepted for now (local use); per-session storage
  through blobs or a Git repository are later choices.
- **Ralf**: removed from the site for now (no panel in Monaco, F7 in the TUI editor says it is not configured);
  it will be put back later.
- **No mouse** in the web TUI editor: both stay fully usable from the keyboard
  (F10 opens the menu in the TUI, the arrows navigate it).
- **Fixed 80×25 size** for the TUI screen (no resizing when the browser is resized).
- **Not verified**: a real browser with a GPU, audio, touch and a real network; see "How it was checked, and what is not" above for what was exercised in headless Edge.

## Publishing: static files arrived empty (fixed)

Symptom on the published site (Azure App Service): every static file — `reconnectModal.js`, `css/app.css`, the logo, even `_framework/blazor.web.js` — answered
a `GET` with `200 OK`, `Content-Length: 0` and no other header, while pages (HTML) arrived fine. The browser reported
`Failed to find a valid digest in the 'integrity' attribute ... reconnectModal.*.js`: `47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=` is simply the SHA-256 of an empty file.
The site was unstyled and not interactive.

Cause: `Program.cs` changed the process working directory (to a scratch folder, for the editor's Open dialog) **before** `WebApplication.CreateBuilder`. The content
root — and with it `wwwroot` and `appsettings.json` — comes from the working directory at that moment, so a published site looked for its files in the scratch
folder. It went unnoticed because in Development (`dotnet run`, Visual Studio) the static files come from absolute paths in the build manifest. Neither Azure,
IIS, the hosting model nor the way the site is deployed (FTP, Web Deploy, Zip Deploy) had anything to do with it.

Fix: the builder is created first, the working directory is changed after it. And outside Development the site now refuses to start, with a message
naming the content root, if `wwwroot` is not there, instead of serving empty files.

How it was found, in case it comes back: the same published output (`dotnet publish`, run with `ASPNETCORE_ENVIRONMENT=Production`) reproduced it on plain Kestrel
and under IIS Express in-process (both gave 0 bytes), so it was not Azure; the log showed `appsettings.json` was not being read either. After the fix both serve the
files with the right bytes and the right `ETag`. When testing static files with curl, download to a file (`-o file`) and look at its size.

The `/favicon.ico` 404 in the console is fixed too (a favicon from the logo is part of the site).

## Future ideas, not built here

- **Cross-platform desktop (Linux/Mac)**: `PixelWindow` stays Windows-only (GDI); the
  `PngFrame`/`BrowserKeyboard`/`pixelCanvas.js` pipeline built here is already operating-system independent and
  could also back a desktop shell (Blazor Hybrid or an embedded browser), not just a cloud tab.
- **VS Code extension**: `EditorRunner`/`EditorAiWorkflow` are already UI-independent C# — the same
  reason the Monaco page was cheap to build would hold for a VS Code extension backend reusing them directly.
- **An editor independent of QBasic** (for PySharp or plain C#): an idea noted, not started.

## GORILLA.BAS performance

The bottleneck was not drawing but the game's pauses: `Rest` waits `MachSpeed * t / 500` seconds and `MachSpeed` counts an empty loop, ~1000 times faster on this interpreter than on a 1990 PC. The three QBasic pages use `QBasicRunPolicy.CalibratedTimerWaitScale`, measured once when the server starts (`Program.cs`), to bring the pauses back to their intended length. Start the site in Release: `dotnet run -c Release --project src/Screen9.Web`.
