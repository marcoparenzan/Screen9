# Screen9 on the web

`src/Screen9.Web`: Blazor Server (interactive server render mode, .NET 10). The same Screen9 engine and the
same QBSharp interpreter/editor used on the desktop, served to a browser — each tab is an isolated Blazor
circuit, that is an independent session.

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
- **`/gorillas-python`** — `samples/python/Gorilla/gorillas.py`, the same file as `pysharp run`, run by
  PySharp embedded in the server (`PySharp.Interpreter` from `LocalFeed/`): the site passes the screen, the keyboard and
  the speaker as the `web_host` variable (`PythonGameHost`), and an in-memory `screen9` module on top of the engine.
- **`/history`**, **`/library`**, **`/roadmap`** — the Mode 9 history, the Coding Guide (runnable mode 9 snippets), and the roadmap.
- **`/plasma-demo`**, **`/scene-demo`** — the demos of `samples/csharp/Demos` (`Screen9.DemosCore`): a VGA mode 13 plasma with palette cycling, and the
  cell canvas. Each page shows the code.
- **`/rally-x`** — `samples/csharp/RallyX`: Rally-X in C# (`RallyXGame`, the same as the desktop one), mode 12 at 640×480 and 16 colours.
  Arrows or WASD, Space for smoke, P to pause. See [samples/csharp/RallyX/README.md](../../samples/csharp/RallyX/README.md).
- **`/gorillas-csx`** — `samples/csharp/Gorilla/gorillas.csx`, the game as a C# script, compiled and run by
  C# scripting (Roslyn) in the server with the same devices (`Screen9.Scripting.GameGlobals`).
- **`/editor`** — the full-screen editor of `qbs edit` (`EditorController`/`EditorApplication` unchanged),
  with a new `IEditorScreen`/`IEditorInputSource` for the browser. Keyboard only (no mouse for now).
- **`/editor-monaco`** — a more "browser-like" editor, based on Monaco (CDN) instead of the character-grid UI:
  it drives `EditorRunner` directly, bypassing `EditorController`. The interpreter is chosen from the file
  extension: `.bas` → QBSharp (QBasic or QBS with the selector), `.py` → PySharp, `.csx` → C#
  scripting (`?file=gorillas.py`, `?file=gorillas.csx`). Run executes the editor's text (saved or not) and opens a window in front of the page;
  program output and errors (QBasic diagnostics, the Python traceback, C# compiler errors, in red) appear in a log under the editor. **New**
  creates an in-memory file in this tab's temporary folder: its extension (`.bas`, `.py`, `.csx`) picks the runtime.

## How it works (reuse, not duplication)

- **Where a program runs**: `Screen9.IPixelDisplay` (a screen to fill and a keyboard to read). On the desktop
  it is implemented by `PixelWindow`; on the web by `BrowserDisplay`, inside `ProgramDialog` (a modal in front of the page). A
  `PixelWindow` opened by the server would end up in a window on the server, behind the browser: that is why
  `EditorApplication` has configurable `ProgramWindow`/`Audio`/`EnableAi`.
- **Frames**: `Screen9.PngFrame` encodes a `PCCanvas`/`Canvas` as a palette-indexed PNG (about 6 KB and under
  1 ms for an EGA frame), sent as a single `byte[]` and drawn on a `<canvas>` with `createImageBitmap`;
  only one draw is in flight at a time, the latest frame wins, and a frame skipped by the 30 Hz limit is still
  shown (`Flush`). The same throttling discipline (~30 Hz, unchanged frames dropped) as `PixelWindow` and
  `AnsiFrameRenderer`/`AnsiCanvasRenderer`, without sharing their code (they are independent renderers, as they
  already are among themselves).
- **Keyboard**: `BrowserKeyboard` (towards INKEY$, like `PCKeyboard.Encode`) and `BlazorEditorInput` (towards
  `EditorInput`, like `EditorInputDecoder.DecodeVt`) read the browser's `keydown` events.
- **Audio**: PLAY/BEEP/SOUND tones are played with real Web Audio (`AudioContext`/`OscillatorNode`), not
  silenced.
- **Style**: Tailwind CSS (standalone CLI, no Node dependency in production), no default Blazor Bootstrap
  layout. JavaScript organised in ES6 classes (`PixelCanvas`, `KeyCapture`, `ToneBeeper`,
  `MonacoHost`), not loose functions.

## Screens and comments

Every screen has a **Save PNG** button under it: it downloads the frame as it is on the screen (native pixels, for example 640×350 in mode 9) so it can
be reused. Ready-made screenshots, in two versions (native, and 1280×960 at 4:3), are in `docs/images`, rendered by `ops/make-screenshots.cs`.
The footer of every page links to the repository's GitHub Issues, which is where comments and bug reports go.

## Security: code run by the server

Python and C# written by the user in Monaco run in the server process with its permissions (files, processes, network):
fine on the development machine, never on a public host. They are therefore enabled only in the Development environment or with
`Screen9:AllowServerCode=true` (`ScriptLanguages.ServerCodeAllowed`); otherwise Run says they are disabled.
QBasic is confined by the interpreter (no file access in the game pages) and is always allowed. The Gorillas pages (Python, C# script)
run fixed code included in the site and are not affected by the check. Note: PySharp's Python has `os`, `subprocess`, `socket`:
before exposing the site a sandbox must be chosen, or only the fixed code left.

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
- **Not verified**: the whole interactive cycle (frame drawing, keyboard, audio) needs a real browser;
  what was verified here is the build, the automated tests (mappers, `PngFrame`, `GorillasCore` parity via
  `--selftest`/`--smoke`) and that every page answers HTTP 200 without exceptions in the initial render — not
  a real interactive session.

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
