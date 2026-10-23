# Screen9 in the browser: Blazor WebAssembly

Everything that draws a screen in real time on the Screen9 site — the games, the demos, `GORILLA.BAS`, the Python game and the Monaco editor's programs —
runs **in the visitor's browser**, as .NET compiled to WebAssembly. The server only serves the pages. This document explains why, how it is built,
what it costs, how to build, run, debug and verify it, and how to add a new page. It is the detailed companion of the shorter sections in
[README.md](README.md) and of the site's `/architecture` page.

Contents: [1 Why](#1-why-it-moved-to-the-browser) · [2 Overview](#2-overview) · [3 Projects and wiring](#3-projects-and-wiring) · [4 The screen](#4-the-screen-pixelscreen) ·
[5 One thread: async games](#5-one-thread-async-games) · [6 Python: PySharp RunAsync](#6-python-pysharp-runasync) · [7 The Monaco editor](#7-the-monaco-editor) ·
[8 Performance and size](#8-performance-and-size) · [9 Build, run, publish, deploy](#9-build-run-publish-deploy) · [10 Debugging](#10-debugging) ·
[11 Verification](#11-verification) · [12 Adding a page](#12-adding-a-new-browser-page) · [13 Pitfalls](#13-pitfalls-met) · [14 Limits](#14-limits-and-what-is-left) · [15 File map](#15-file-map)

---

## 1. Why it moved to the browser

The site started as Blazor Server. A program ran in the server and every frame travelled to the visitor:

```
Before (Blazor Server)

  game ──► PCCanvas ──► PNG encode ──► SignalR ──► browser canvas      ~5 frames a second
    ▲                                                   │
    └──────────────────── key (SignalR) ◄───────────────┘

Now (Blazor WebAssembly)

  browser:  game ──► PCCanvas ──► RGBA buffer ──► canvas ImageData     30–60 frames a second
              ▲                                       (no network)
              └── key (in the page)

  server:   serves the page, the code listing and the WebAssembly files
```

With one frame in flight at a time, a game cannot draw faster than one round trip, and every key had to make the round trip too. The web cap on frames had been lowered
to 5 a second to keep traffic down, which made the games feel broken. Moving the program next to the screen removes the network from the loop entirely. It also removes a
cost per visitor on the server (a thread and a framebuffer per session) and, for the editor, the need to run visitors' code in the server process.

## 2. Overview

### What runs where

| Page | Runs in | Component / runner |
|---|---|---|
| `/plasma-demo`, `/scene-demo` | browser | `PlasmaWasm`, `SceneWasm` |
| `/rally-x` | browser | `RallyXWasm` → `RallyXGame.RunAsync` |
| `/gorillas-csharp` | browser | `GorillasWasm` → `GorillasGame.RunAsync` |
| `/gorillas-bas` | browser | `BasicWasm` → `BrowserBasic` (`QBasicEngine`) |
| `/gorillas-python` | browser | `PythonWasm` → `BrowserPython` (PySharp `RunAsync`) |
| `/editor-monaco` | browser | `EditorMonacoWasm` (`.bas` QBasic/QBS, `.py`; `.csx` is edited but not run) |
| `/gorillas-csx` | **server** | Roslyn (C# scripting); frames travel as PNG |
| `/editor` | **server** | the character-grid QBasic editor (`EditorController`) |
| `/`, `/history`, `/library`, `/roadmap`, `/architecture` | server | static pages |

The C# script game stays on the server because Roslyn is large and slow to load in WebAssembly (decision taken with the maintainer: the cost is high for a single page). The grid editor
is untouched and still drives QBasic on the server.

### The two halves

- The **server** (`src/Screen9.Web`, Blazor Server) renders every page, including the text and the code listings, and serves the WebAssembly files.
- The **client** (`src/Screen9.Web.Client`, Blazor WebAssembly) holds the components that run programs. A page embeds one as an **island**:
  `<GorillasWasm @rendermode="new InteractiveWebAssemblyRenderMode(prerender: false)" />`. `prerender: false` because a running program has nothing to show before it runs.

## 3. Projects and wiring

### `src/Screen9.Web.Client`

`Microsoft.NET.Sdk.BlazorWebAssembly`, `net10.0`. References: `Screen9` (the engine), `Screen9.DemosCore`, `RallyXCore`, `GorillasCore`, `QBSharp.QBasic`, `QBSharp.StandardLibrary`, and the packages
`Microsoft.AspNetCore.Components.WebAssembly` 10.0.13 and `PySharp.Interpreter` 2.6.0 (from `LocalFeed/`). Notable properties:

| Property | Why |
|---|---|
| `AllowUnsafeBlocks` | `[JSImport]` source generation needs it (`SYSLIB1074`). |
| `NoWarn CA1416` | `JSHost`/`JSImport` are browser-only APIs; the assembly also gets *read* by the server (it shares `BrowserKeyEvent`, `ScriptLanguages`), so marking the whole assembly browser-only would flag the server's own code. |
| `InvariantGlobalization`, `BlazorWebAssemblyLoadAllGlobalizationData=false` | Keeps the download smaller; nothing here needs ICU. |
| `RunAOTCompilation` only when `Configuration == Release` | AOT is what makes it fast (section 8) but only takes effect on `publish`; a Debug build stays the plain, debuggable one. |

The package versions are pinned to **10.0.13** deliberately: 10.0.0 has known high-severity advisories (`NU1903`) and the solution builds with `TreatWarningsAsErrors`.

### `src/Screen9.Web` (server)

- `Program.cs`: `AddInteractiveWebAssemblyComponents()`, `AddInteractiveWebAssemblyRenderMode()`, `AddAdditionalAssemblies(typeof(PlasmaWasm).Assembly)` so the router finds the client components,
  and `UseWebAssemblyDebugging()` in Development.
- `Components/Routes.razor`: `AdditionalAssemblies` with the client assembly.
- `Screen9.Web.csproj`: a `ProjectReference` to the client and the package `Microsoft.AspNetCore.Components.WebAssembly.Server` 10.0.13 (the `AddInteractiveWebAssembly*` extensions live there, even though the server does not host WebAssembly itself).
- `tailwind.css` also scans `../Screen9.Web.Client/**/*.razor`, so the utility classes used in client components are generated into `app.css`.

### What moved between the projects

`BrowserKeyboard.cs`, `BrowserKeyEvent.cs` and `ScriptLanguages.cs` (namespace `Screen9.Web`, unchanged) and the scripts `keyCapture.js`, `toneBeeper.js`, `monacoHost.js` (same URLs, `/js/...`)
moved to the client project: the static web assets of a referenced project are served from the server at the same paths, so the existing pages and the new components load the same files.
Mixing render modes matters here: **an interactive-server page cannot contain a WebAssembly child**, which is why a page that hosts a browser program is a static server page with an island, not an interactive page.

## 4. The screen: `PixelScreen`

`PixelScreen.razor` is the browser counterpart of the server's `PixelDisplay` (which stays for the server pages). It owns a `<canvas>`, the keyboard and the Web Audio tones.

```
game ──► PCCanvas ──► RgbaFrame.Fill ──► byte[] ──► PixelBlit.Draw ([JSImport]) ──► ImageData ──► putImageData
                      (palette indices → RGBA,      pixelBlit.js: one entry per canvas id
                       reused buffer, no allocation)
```

- **Frames.** `Screen9.RgbaFrame` (in the engine) expands the *visible* page of a `PCCanvas` into a caller-owned RGBA buffer (`Length`, `Fill`); with `ManualPresent` it reads the *presented* page, as the other renderers do.
  For text cells (`Canvas`) it rasterizes with the VGA 8x16 font and the 16 CGA colours (`Size`, `Fill`), producing what `PngFrame.Encode(Canvas)` produces on the server (80x25 = 640x400).
- **Blit.** `PixelBlit` (`Components/PixelBlit.cs`) is the `[JSImport]` surface over `wwwroot/pixelBlit.js`: `Attach(id, w, h)`, `Draw(id, byte[])`, `Detach`, `Save`, `NextFrame()` (a promise on `requestAnimationFrame`).
  `JSHost.ImportAsync` is called once (`EnsureLoadedAsync`). Canvases are looked up by element id, so several screens can share a page (the editor makes a new one per run).
- **Passing the bytes.** `byte[]` arrives in JavaScript as a `Uint8Array` (one copy: 256 KB for a 640x350 frame, negligible). `Span<byte>` with `[JSMarshalAs<JSType.MemoryView>]` was tried first and fails in this runtime with `Assert failed: Expected function Uint8Array`.
- **No throttle, no queue.** A frame is drawn when the game presents it. Drawing costs about 0.2 ms.
- **Keyboard.** `keyCapture.js` (unchanged) listens on the document in the capture phase and posts a small `BrowserKeyEvent` to `[JSInvokable] HandleKeyDown`, which encodes it with `BrowserKeyboard.Encode` (INKEY$ convention: `"\0H"` for the up arrow) into a queue.
  Games call `ReadKey()` (`""` when empty) and `FlushKeys()`. Function keys are always taken while a screen exists (so F5 does not reload the page); other keys only while the canvas has focus.
- **Audio.** `RequestTone(frequency, ms)` calls `toneBeeper.js` (a square-wave oscillator). It is fire-and-forget: the browser plays it for its own duration; pacing is the game's job (section 5).
- **Lifecycle.** `Ready` completes when the canvas, keyboard and audio are wired up (a page awaits it before the first frame); the `OnReady` callback tells a parent that creates screens dynamically. `DisposeAsync` detaches everything.

## 5. One thread: async games

A browser runs .NET on **one thread**. A game that pauses with `Thread.Sleep` or loops waiting for a key would freeze painting and input. The games are written for a *host* object (screen, keys, waiting, sound) so the same game runs in a window and in a page; the browser host needed a way to wait without a thread.

### The contract

| Host | Member | Meaning |
|---|---|---|
| `GorillasHost` | `Sleep` (`Action<TimeSpan>`, default `Thread.Sleep`) | blocking wait, for hosts with a thread to spare (desktop window, tests) |
| | `Delay` (`Func<TimeSpan, ValueTask>?`) | non-blocking wait; when set, awaited **instead of** `Sleep` |
| | `Tone` (`Action<PCTone>`) / `ToneAsync` (`Func<PCTone, ValueTask>?`) | a tone that paces the game for its duration; `ToneAsync` wins when set |
| | `Present`, `ReadKey`, `FlushKeys`, `Background`, `Seed`, `Checkpoint` | unchanged |
| `RallyXHost` | `Sleep`, `Delay` | as above, awaited at the end of every frame |
| | `Present`, `ReadKey`, `Sound`, `Seed`, `Assets`, `Demo`, `MaxFrames`, `Checkpoint` | unchanged |

`GorillasGame.RunAsync()` and `RallyXGame.RunAsync()` are `async ValueTask`; every method that used to wait (`Frame`, `Rest`, `Play`, `WaitKey`, `LineInput`, `GetNum`, `PlotShot`, the intros, the explosion…) is async. Two methods lost `out`/`ref` parameters because `async` forbids them
(`GetInputs` returns a tuple, `DoShot` returns `(hit, playerNum)`).

### Why `Run()` still works

`Run()` is `RunAsync().AsTask().GetAwaiter().GetResult()`. With no `Delay`, every wait calls `Sleep` and returns a completed `ValueTask`, so the whole run completes synchronously on the calling thread. The desktop window (`Gorillas.cs`, `RallyX.cs`), the tests and `gorillas.csx` therefore keep working unchanged.
`gorillas.csx` is regenerated from `GorillasCore.cs` (a test checks that the script carries the game text verbatim, and another that both play the same match: same score, frames and keys).

### `GameClock`

`GameClock.Delay(time, token)` awaits `Task.Delay`; for a **zero or negative** wait it awaits `Task.Yield()`. That second case is essential: a game that is behind schedule would otherwise complete every wait synchronously and spin without ever returning to the browser, starving painting and keys.
`GameClock.PlayBackground(tones, play, token)` plays `PLAY "MB…"` tones one after the other without stopping the game. Cancellation (`OperationCanceledException`) is how a page leaving ends the game cleanly.

### What needed no conversion

- **Plasma and the demos** have no loop of their own: the component calls `Step()` from `requestAnimationFrame` (Plasma) or a 33 ms delay (Scene).
- **`QBasicEngine`** was already async: `INKEY$` polls and `TIMER` waits `await options.Delay`, and it presents frames through callbacks (`PresentText`, `PresentGraphics`). `BrowserBasic` only sets the options (`AllowFileIO=false`, `BetweenInputPolls`, `ScreenInput=true`).

### Calibrating QBasic-era timing

GORILLA.BAS measures the speed of an empty loop (`CalcDelay`) and then waits proportionally, so on an interpreter that runs that loop much faster than a 1990 PC every pause is far too long. `TimerWaitScale` brings TIMER waits back to their intended length. The server computes it once at startup (`QBasicRunPolicy.CalibratedTimerWaitScale`, in `QBSharp.Cli`, which the browser does not reference);
`BrowserBasic.CalibrateAsync` repeats the same measurement in the browser, **once per page load**, about half a second of busy loop after the first paint, cached in a static task.

## 6. Python: PySharp RunAsync

`gorillas.py` blocks in `sleep`, and PySharp, like every tree-walking interpreter, keeps the Python call stack on the C# stack — a blocked call cannot be "paused". The options were:

| Option | Verdict |
|---|---|
| .NET WebAssembly threads | Experimental, not supported for Blazor, and they need cross-origin isolation headers (`COOP`/`COEP`) that would break the CDN-loaded editor. Rejected. |
| Rewrite PySharp's evaluator (3,800 lines) as async everywhere | Every node pays for a state machine, every script slows down, and everything its 1,300 tests cover is touched. Rejected. |
| A **second, suspendable evaluator** beside the first | Chosen. |

### Design (in PySharp 2.6.0)

`PyEngine.RunAsync(source)` runs the same AST on the caller's thread with `async`/`await` (`Interpretation/InterpAsync.cs`, a partial of `Interp`); the synchronous `Run`, `Exec` and `Eval` are untouched.

- **Where it waits.** A call to a .NET method that returns `Task`, `Task<T>`, `ValueTask` or `ValueTask<T>` (`host.sleep(0.1)` with `public Task sleep(double s)`): `ClrBinder.InvokeMethodRaw` returns the raw result and the evaluator awaits it before converting it to a Python value.
  And a builtin that has an `AsyncFn` (`time.sleep` has one). A .NET exception thrown by the awaited work becomes the Python exception as it does on the blocking path (`OperationCanceledException` → `KeyboardInterrupt`, which `gorillas.py` catches to end cleanly).
- **What it re-implements.** Only what can contain a call: statements that nest statements (block, `if`, `while`, `for`, `try`/`finally`, `with`, `return`, assignments), the expressions that nest calls (call, attribute, index, slice, unary/binary/bool/compare, if-expression, walrus, tuple/list/set/dict), and the call chain (bound methods, functions, lambdas, static methods, `__call__`).
  Everything else, **and every statement or expression with no call inside** (`NeedsAsync`, memoized by node identity), runs on the synchronous evaluator through the same helpers (`BinaryOp`, `GetAttr`, `AssignTo`, `ApplyAugAssign`…), so the two cannot drift apart on semantics. Control flow keeps using the same signal exceptions (`ReturnSignal`, `BreakSignal`…).
- **State.** The synchronous evaluator keeps its frame stack and recursion count per thread, which a wait may change (the continuation can resume on another thread in a test, or after a timer in a browser). The suspendable one carries frames down as arguments and keeps its depth in an instance field. One asynchronous run at a time per `Interp`.
- **Stack.** There is no `BigStack` (it needs a thread): recursion depth is what the caller's stack allows; the 1,000-call limit still applies.

### What does not wait

A wait inside these runs synchronously and does **not** suspend: class bodies, `__init__` and other methods reached through instantiation or operators, comprehensions and generator expressions, f-strings, `match`, decorators, annotated assignments, callbacks passed to builtins (`sorted(key=f)`), and **generators and coroutines** (`yield`, `async def` use one thread each, so they are not available where threads are not).
A task returned inside one of these is not awaited: the script sees the task object. The rule for host code is: wait in plain functions and methods.

### The Screen9 side

`BrowserPythonHost` is the `web_host` variable of `gorillas.py` — the same contract as the desktop host (`present`, `read_key`, `flush_keys`, `sleep`, `tone`, `background`, `seed`, `checkpoint`), except that `sleep` and `tone` **return a `Task`** (`GameClock.Delay`).
`BrowserPython.RunAsync(source, fileName, host, output)` builds the engine, registers an in-memory `screen9` module (`clr.AddReference("Screen9")` plus the names the installable wheel provides; `Assembly.Load` by name finds the assembly already in the page), sets `web_host` and awaits `engine.RunAsync`. It returns `"Completed"`, `"Canceled"` or `"Error: …"` and writes Python output (one line per call) to a callback.
`PythonWasm` is the page component. The tests drive `gorillas.py` through `BrowserPython` with a fake host (`PythonGamesTests`). Full design notes: PySharp's `ARCHITECTURE.md` §12d and `RELEASE_NOTES.md` v2.6.0; its tests: `M11_Interop/RunAsyncTests.cs` (a wait is a real suspension and parks no thread; same thread as the caller; results, exceptions, cancellation; waits inside loops, methods, `try`/`finally`; a differential set that must print the same through `Run` and `RunAsync`).

## 7. The Monaco editor

`/editor-monaco` is a static server page that embeds `EditorMonacoWasm`. The server reads the bundled samples (`.bas`, `.py`, `.csx` from `samples/`) and passes them as a parameter (and the `?file=` query value as `InitialFile`); from there everything is in the page.

- **Files.** An in-memory sorted list: the samples, overlaid with what this browser saved. **Save** writes them as JSON to `localStorage` (`screen9.editor.files`); **Reset** removes the key and goes back to the samples; **New** adds a file whose extension (`.bas`, `.py`, `.csx`) picks the runtime; switching file keeps the previous text.
  `localStorage` can be missing or full (a private window): every access is wrapped, and the editor works without it.
- **Editing.** `monacoHost.js` (Monaco itself comes from the jsDelivr CDN) through ordinary `IJSObjectReference` calls: `getValue`, `setValue`, `setLanguage`.
- **Running.** Run stores the editor's text, then:

  | File | Runner | Output |
  |---|---|---|
  | `.py` | `BrowserPython` (PySharp `RunAsync`) | program screen below the editor; `print` and the traceback in the log, errors in red |
  | `.bas`, QBasic dialect | `BrowserBasic` (`QBasicEngine`) | program screen; runtime diagnostics in the log |
  | `.bas`, QBS (modern) dialect | `BrowserQbs` (`ScriptEngine` + `StandardLibraryHost`: `Print`, `Json`, `Ui`, `Screen`…) | text only: the log. `Ui` dialogs are the browser's `prompt`/`alert` (`Select` takes a number). No files, no Ralf |
  | `.csx` | none | a message: it needs Roslyn, which runs on `/gorillas-csx` |

  A **new `PixelScreen` is created for every run** (`@key="runId"`, `OnReady` completes a `TaskCompletionSource` the run awaits), **Stop** cancels the run's token, **Close** hides the screen. The keys go to the program: the canvas is focused after it is ready.
- **Compared with the old server version of this page**: no `.csx` run, no editor-owned INPUT dialog (INPUT is on the program screen, as in a window), no per-tab folder on the server. `ScriptLanguages.ServerCodeAllowed` and the `Screen9:AllowServerCode` setting are gone: **nothing a visitor writes runs on the server** any more.

## 8. Performance and size

### Frame rate and per-step cost (Plasma, headless Edge, software rendering, same machine)

| Build | `Step()` | `Fill` (RGBA expand) | Frames/s |
|---|---|---|---|
| Blazor Server (PNG over SignalR) | n/a | n/a | ~5 |
| WebAssembly, **interpreted**, Debug (`dotnet run`) | 28 ms | 10 ms | ~22 |
| WebAssembly, interpreted, Release (`dotnet run -c Release`) | not split | not split | 23–25 |
| WebAssembly, **AOT** (`dotnet publish`) | 1.1 ms | 0.2 ms | **60** |

Drawing on the canvas costs about 0.2 ms in every build. Other AOT results: Rally-X holds its 30 frames a second (29 draws measured over a second); Gorillas (C# and `.bas`) play normally; the Python game draws about 44 frames a second during a banana flight against a ceiling of about 50 (the game's own 20 ms step); `GORILLA.BAS` runs.

**Interpreted vs AOT.** Without AOT the Mono runtime interprets the IL, which is about 25 times slower than the compiled code for this kind of loop; **a Release build does not fix it** (the numbers above are 22 vs 23–25). `RunAOTCompilation` only acts on `publish`, so `dotnet run` and Visual Studio always run interpreted. For anything where speed matters, use the published build (section 9).

### Download size

Measured on a published AOT build (before the editor's QBS support was added, so slightly smaller than today's), `_framework`:

| | Size |
|---|---|
| Uncompressed (77 files) | 29.8 MB |
| Brotli (`.br`) | **6.5 MB** |
| Gzip (`.gz`) | 9.5 MB |
| `dotnet.native.wasm` (the runtime with the AOT-compiled code) | 22.4 MB raw, 4.7 MB brotli |
| `System.Private.CoreLib.wasm` | 2.0 MB raw, 0.45 MB brotli |
| `PySharpLib.wasm` | 1.1 MB raw, 0.2 MB brotli |

The publish produces the `.br` and `.gz` files next to every asset. The first visit to any browser page downloads the runtime (the cost is per origin, not per page); later visits use the cache. **Not measured:** first-load time on a real network.

## 9. Build, run, publish, deploy

### Build and test

```powershell
dotnet build src\Screen9.Web\Screen9.Web.csproj        # server + client (interpreted WebAssembly)
dotnet test  src\Screen9.Web.Tests                      # includes gorillas.csx vs GorillasCore, gorillas.py through BrowserPython
dotnet test  src\RallyX.Tests ; dotnet test src\Screen9.Tests
```

### Run

```powershell
dotnet run --project src\Screen9.Web\Screen9.Web.csproj    # works, but the browser programs are interpreted: slow
```

### Run fast: a published (AOT) build

```powershell
# once per machine
dotnet workload install wasm-tools

# from the repository root
dotnet publish src\Screen9.Web\Screen9.Web.csproj -c Release -o ops\artifacts\site
dotnet ops\artifacts\site\Screen9.Web.dll --urls http://localhost:5280
```

The first publish takes about 3½ minutes (it compiles the whole runtime ahead of time). `ops\artifacts\` is ignored by git. Stop any running copy of the site first — **a running `Screen9.Web` (including the one Visual Studio starts) locks `Screen9.Web.Client.dll`** and the build or publish fails with `MSB3027`.
The published site runs in the `Production` environment by default and serves its files from `wwwroot` under the content root (see `Program.cs`: it fails fast if `wwwroot` is missing, and the working directory must not be changed before the host is built).

### Deploy

The repository has publish profiles for Azure App Service (`Properties\PublishProfiles`: Web Deploy, Zip Deploy, FTP). The published output is self-contained pages plus the `_framework` files; the host must serve `.wasm` with its MIME type and may serve the precompressed `.br`/`.gz`.
**Not verified:** a deployment of the WebAssembly build to App Service, and its caching headers.

## 10. Debugging

- `Program.cs` calls `UseWebAssemblyDebugging()` in Development, as the Blazor Web App template does, so a debugger can attach to the client.
- `RunAOTCompilation` is off in Debug, so a debug build is the plain interpreted one.
- **Open problem.** In Visual Studio Insiders, debugging (F5) produced *"A fatal error has occurred loading the metadata for 'mscorlib.dll' and debugging needs to be terminated. The operation failed because the debugger could not get the metadata."* The two changes above did **not** resolve it for the maintainer, and it could not be reproduced from the command line (running the site without the debugger works).
  Not yet tried: a `global.json` pinning the .NET 10 SDK (the machine has the 11.0 release candidate as the newest SDK while the projects target `net10.0`), switching off *Tools → Options → Debugging → Enable JavaScript debugging for ASP.NET*, and checking whether the commit before the WebAssembly work (`56ef136`) debugs in the same Visual Studio. Until it is understood, debug the server side with `dotnet run` and the browser side with the browser's developer tools.
- Locked files while building: see section 9.

## 11. Verification

### Automated tests

`Screen9.Tests`, `RallyX.Tests` and `Screen9.Web.Tests` pass (the last includes the navigation tests — every menu entry is a page and every page is in the menu — the `.csx` parity tests and `PythonGamesTests`). PySharp has its own suite (`RunAsyncTests`, 14 tests). `QBSharp.Tests` has four failures about ANSI output of the redirected CLI that do not touch this code, and PySharp has seven FastAPI failures (`sys._getframe`) unrelated to `RunAsync`.
There are no automated browser tests.

### Driving a real browser: `ops/browser-drive.mjs`

The browser side was checked with headless Edge driven over the DevTools protocol, no human involved:

```powershell
msedge --headless=new --disable-gpu --user-data-dir=$env:TEMP\edge-profile --remote-debugging-port=9333 --window-size=1100,900 http://localhost:5280/rally-x
node ops\browser-drive.mjs rally-x click "keys: " "wait:500" "keys:dddd" "wait:3000" "shot:rally.png"
```

Count the frames a page draws per second by patching `CanvasRenderingContext2D.prototype.putImageData` (the script's header has the exact expression) and reading the counter twice. Use a **published** build to judge speed, and a profile directory of its own (`--user-data-dir`) so a running browser does not swallow the command.
Headless virtual time (`--virtual-time-budget`) makes in-page seconds meaningless for frame rates: measure in real time.

### What was verified, and what was not

| Verified (headless Edge, AOT unless noted) | Not verified |
|---|---|
| Plasma 60 fps; Rally-X title, a match, steering keys, ~30 draws a second | A real browser with a GPU |
| Gorillas C#: intro, names, a shot | Sound (Web Audio needs a gesture; nothing was audible in headless) |
| `GORILLA.BAS`: intro, names, first prompt, a shot | Touch devices |
| Gorillas Python: intro with its sparkles, a shot, ~44 draws/s in flight | First-load time on a real network |
| Editor (dev build): Monaco loads with the samples; `hello.bas` in QBS prints `Hello, QBSharp`; `gorillas.py` and `GORILLA.BAS` run from it; Stop ends a run | `Save` across a reload, `Reset`, `New`, the QBS `Ui` dialogs, the editor on the AOT build |
| Header and side navigation stay in view while scrolling | Leaving a game page mid-run (tokens are cancelled in `Dispose`; not exercised) |
| | Deployment to App Service; Visual Studio debugging (section 10) |

## 12. Adding a new browser page

1. **Make the program async-friendly.** If it has a loop with waits, give its host a `Delay` (and `ToneAsync` if a tone paces it) and an `async` entry point, keep the synchronous `Run()` as a wrapper (section 5). If it is a Python script, wait only in plain functions and methods (section 6).
2. **Add a component** to `src/Screen9.Web.Client/Components/`. The shape of every one:

   ```razor
   @implements IDisposable
   <PixelScreen @ref="screen" />

   @code {
       private PixelScreen? screen;
       private readonly CancellationTokenSource cts = new();

       protected override async Task OnAfterRenderAsync(bool firstRender)
       {
           if (!firstRender || screen is null) return;
           await screen.Ready;                      // canvas, keys and audio are wired up
           var token = cts.Token;
           var host = new MyHost { Present = canvas => screen.PresentGraphics(canvas), ReadKey = screen.ReadKey,
                                   Delay = d => GameClock.Delay(d, token) };
           try { await new MyGame(host).RunAsync(); }
           catch (OperationCanceledException) { }   // the page was left
       }

       public void Dispose() => cts.Cancel();
   }
   ```
3. **Add a server page** (`src/Screen9.Web/Components/Pages/`), static, with the description and the code listing, embedding the component with `@rendermode="new InteractiveWebAssemblyRenderMode(prerender: false)"`. Pass text parameters from the server (a source file read from `samples/`) rather than fetching them in the client.
4. **Menu and tests.** Add the page to `SiteNavigation.cs`; `SiteNavigationTests` checks that every menu entry is a page and every page is in the menu.
5. **References.** If the game lives in another project, add a `ProjectReference` to `Screen9.Web.Client.csproj`. A file the page shows as a listing is copied by an item in `Screen9.Web.csproj`.
6. **Docs.** Update `docs/web/README.md` (the page list and the table), this file if the design changed, and the site's `/architecture` page.
7. **Check it on a published build** with `ops/browser-drive.mjs` (section 11); do not judge speed on `dotnet run`.

## 13. Pitfalls met

| Symptom | Cause and fix |
|---|---|
| `Assert failed: Expected function Uint8Array` calling `PixelBlit.Draw` | `Span<byte>` as `MemoryView` is not supported by this runtime; pass a `byte[]`. |
| `SYSLIB1074` / `CS0227` building the client | `JSImport` needs `AllowUnsafeBlocks`. |
| `CA1416` "only supported on: 'browser'" | Browser-only API (`JSHost`) in code the server also reads. `NoWarn CA1416` on the client; do **not** put `[assembly: SupportedOSPlatform("browser")]` (it flags the server's use of shared types). |
| `NU1903` high-severity package | The 10.0.0 WebAssembly packages; use 10.0.13 (warnings are errors here). |
| `AddInteractiveWebAssemblyComponents` not found | It is in `Microsoft.AspNetCore.Components.WebAssembly.Server`, not in the client package. |
| `MSB3027` copying `Screen9.Web.Client.dll` | The site (or Visual Studio) is running and locks it. |
| A game freezes the page after a long frame | A zero wait completed synchronously; `GameClock.Delay` must `Task.Yield()` for waits of zero or less. |
| `dotnet run` is "very slow" | Interpreted WebAssembly. Publish (AOT); a Release build alone does not help (section 8). |
| Python `sleep` freezes the page | The script ran through `Run`, not `RunAsync`, or the wait is in a place that does not suspend (section 6). |
| Headless frame counters read 0 or nonsense | `--virtual-time-budget` distorts timers; measure in real time with a remote debugging port. |
| Visual Studio debugger: `mscorlib.dll` metadata | Open, see section 10. |

## 14. Limits and what is left

- **Speed depends on AOT.** `dotnet run` and the Visual Studio debugger run interpreted code, 25 times slower here. A published build is the only fast one.
- **Size.** About 6.5 MB over the wire (brotli) on the first visit, 29.8 MB uncompressed on disk; not trimmed beyond the SDK's default. A smaller runtime (more aggressive trimming, dropping assemblies a page does not need, per-page lazy loading) is possible and unexplored.
- **C# script and the grid editor stay on the server.** Roslyn in WebAssembly would add tens of megabytes and slow compiles. The grid editor was not touched.
- **PySharp `RunAsync`** does not suspend inside class bodies, `__init__`, comprehensions, f-strings, `match`, decorators or builtin callbacks, and has no generators or coroutines without threads. Extend it if a game needs them (`async def` could run on the same machinery).
- **The QBS dialect** in the browser has no files and no Ralf, and its `Ui` uses `prompt`/`alert`.
- **No browser tests in the repository.** `ops/browser-drive.mjs` is a driver, not a test suite.
- **Unverified:** a GPU browser, sound, touch, a real network, deployment, Visual Studio debugging.
- **The server's PNG path** (C# script, grid editor) is capped at about 30 frames a second with one frame in flight (so it follows the round trip); sending raw indices plus palette instead was measured and rejected (encoding costs 2–7 ms per frame on a worst-case frame, raw frames would be 2.5 times larger). The console renderer already reads the raw pixels and the palette directly.

## 15. File map

| Path | Role |
|---|---|
| `src/Screen9.Web.Client/Screen9.Web.Client.csproj` | the WebAssembly project (references, AOT in Release) |
| `src/Screen9.Web.Client/Components/PixelScreen.razor` | canvas, keys, audio, `Ready`/`OnReady` |
| `src/Screen9.Web.Client/Components/PixelBlit.cs`, `wwwroot/pixelBlit.js` | `[JSImport]` surface: attach, draw, detach, save, next frame |
| `src/Screen9.Web.Client/Components/GameClock.cs` | `Delay` (with `Task.Yield` for zero waits), `PlayBackground` |
| `src/Screen9.Web.Client/Components/PlasmaWasm.razor`, `SceneWasm.razor`, `RallyXWasm.razor`, `GorillasWasm.razor` | the C# demos and games |
| `src/Screen9.Web.Client/Components/BasicWasm.razor`, `BrowserBasic.cs` | `GORILLA.BAS` and QBasic programs (`QBasicEngine`, calibration) |
| `src/Screen9.Web.Client/Components/BrowserQbs.cs` | the QBS dialect (`ScriptEngine`, `Ui` over browser dialogs) |
| `src/Screen9.Web.Client/Components/PythonWasm.razor`, `BrowserPythonHost.cs` | Python: `BrowserPythonHost` (`web_host`) and `BrowserPython.RunAsync` |
| `src/Screen9.Web.Client/Components/EditorMonacoWasm.razor` | the Monaco editor |
| `src/Screen9.Web.Client/BrowserKeyboard.cs`, `BrowserKeyEvent.cs`, `ScriptLanguages.cs` | shared with the server (namespace `Screen9.Web`) |
| `src/Screen9.Web.Client/wwwroot/js/keyCapture.js`, `toneBeeper.js`, `monacoHost.js` | keyboard, Web Audio, Monaco |
| `src/Screen9/RgbaFrame.cs` | palette indices / text cells → RGBA bytes |
| `samples/csharp/Gorilla/GorillasCore.cs`, `gorillas.csx`; `samples/csharp/RallyX/RallyXGame.cs` | the games: `Run`/`RunAsync`, `Delay` |
| `src/Screen9.Web/Program.cs`, `Components/Routes.razor` | WebAssembly render mode, additional assemblies, debugging endpoints |
| `src/Screen9.Web/Components/Pages/*.razor` | static server pages embedding the islands |
| `src/Screen9.Web/Components/Pages/Architecture.razor` | the site's summary of this document |
| `src/Screen9.Web.Tests/PythonGamesTests.cs`, `CsxGamesTests.cs`, `SiteNavigationTests.cs` | tests touching this design |
| `ops/browser-drive.mjs` | headless browser driver (DevTools protocol) |
| `LocalFeed/PySharp.Interpreter.2.6.0.nupkg` | PySharp with `RunAsync`; source in the PySharp repository (`Interpretation/InterpAsync.cs`) |
