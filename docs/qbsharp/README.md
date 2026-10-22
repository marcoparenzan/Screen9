# QBSharp (QBS)

An embeddable .NET 10 interpreter with a VB.NET reference syntax and a console editor in the spirit of QuickBASIC. AST execution, without Roslyn, C#/VB generation or IL emission.

## Status: phase 7 — RalfAI

- Lexer/parser/AST runtime, CLR types, Dim/operators, If/While, Function/Sub/Return and closures.
- Interpreted classes with auto-properties/methods/Sub New/Me; namespaces, New and CLR generics.
- Runtime .bas Import with aliases, isolated modules, cache/cycles/cancellation/limits.
- Spectre CLI run/repl/edit/new/test, help/completion/exit codes and embedding.
- Standard library filesystem/glob, dynamic/CLR/script-class JSON/YAML, YAML config and UI.
- A full-screen editor with menu/keyboard/mouse, buffer/selection/internal clipboard/undo/redo, save/open/dirty confirmations, run/output/dialog/diagnostics/Stop.
- A separate **RalfAI**: the `Ralf(prompt [, format])` binding, text/JSON, timeout/cancel/protected secrets and F7 generation/modification with consent and preview. A real **RalfAI.Providers 1.0.4** adapter, not an autonomous agent.

Database/templates (phase 8) and dynamic assemblies/packages (phase 9) are not implemented. Not full VB.NET/VB6 compatibility and not a sandbox. Do not run untrusted scripts or AI code.

## Starting and the global tool

.NET 10 SDK/runtime (a later compatible SDK is allowed). The RalfAI package comes from its own trusted feed/cache; the originally indicated feed is not present in this environment:

```powershell
dotnet run --file ops/seed-ralf-feed.cs
# or pass a trusted RalfAI.Providers.1.0.4.nupkg as an argument after --
dotnet restore src/QBSharp.slnx --configfile ops/nuget.config
dotnet build src/QBSharp.slnx
dotnet test src/QBSharp.slnx
```

The QBSharp.Cli tool, command qbs, **0.7.0-alpha.4** (installed on 2026-10-22), per user, not published on nuget.org:

```powershell
dotnet pack src/QBSharp.Cli -c Release -o ops/artifacts
dotnet tool update --global QBSharp.Cli --version 0.7.0-alpha.4 --add-source ./ops/artifacts
qbs --version
qbs edit samples/qbsharp/hello.bas
qbs run samples/qbsharp/phase5.bas
qbs repl
```

For a first installation use install instead of update. If a QBSharp program is open, `bin/Release` is locked: add `--artifacts-path <temporary folder>` to the pack. PATH $HOME/.dotnet/tools. The tool includes the adapter/dependencies and `Screen9.Fonts/LICENSE.TXT` (the CC BY-SA 4.0 licence of the IBM fonts), and does **not** include credentials/TerminalHarness.

## External Ralf credentials — opt-in

```powershell
$env:QBS_AI_CONFIG_FILE = 'C:\path\to\RalfAI\config.json'
qbs run samples/qbsharp/ralf.bas
qbs edit samples/qbsharp/hello.bas
```

The path explicitly enables the use of the external file; no keys are copied into the repository/scripts/YAML config and there is no autodiscovery of that folder. The Ralf JSON has the root AIProvider and the OpenAI/Anthropic/AzureAIFoundry groups with ApiKey/ModelId; Foundry Endpoint/Api and model aliases. Explicit QBS_AI_* take precedence; **an empty QBS_AI_PROVIDER also disables reading the file**. A model is required, the Foundry endpoint must be HTTPS, defaults are a 60 s timeout / 2048 output tokens. Calls may be billed: merely starting the editor does not call the AI.

```vb
Dim answer = Ralf("Explain a blue-green deploy")
Print(answer.Text)
Dim result = Ralf("Return JSON with status = ready", "json")
Print(result.Data.status)
```

The result is a DynamicRecord Text/Provider/Model/Data. Formats: text (default)/strict json. No tool calls/autonomous agent/auto execution/history. Provider/file errors are sanitised, host cancellation is supported; do not include secrets in prompts or in the buffer. Config.Data remains the script's YAML config, not AI credentials.

## The QuickBASIC editor and F7

`qbs edit [file.bas]` requires an interactive ANSI terminal of at least 40x12 (80x25 recommended), not redirected. A non-existent file is only a buffer until Save.

| Key | Action |
|---|---|
| F1 / F10 | Help / menu |
| F2 / F3 | Save / Open |
| F4 / F5 / F6 | Go to error / Run buffer / Stop |
| **F7** | Ralf: consent -> instruction -> preview -> Apply/Discard |
| Ctrl Q | Exit with confirmation of changes |
| Ctrl Z,Y / A / C,X,V | Undo/redo / select all / internal clipboard |

F7 sends only the instruction and the current buffer (max 64Ki chars), not other files/paths/config. The proposal is syntactically validated, not guaranteed safe; the replacement diff scrolls with arrows/PgUp/PgDn/wheel, horizontally too. Enter applies in memory only, Esc discards; Save/Run are always separate. A changed buffer/path -> rejection. New file: Ctrl N -> F7 -> review/apply -> Save As with a name chosen by the user. No arbitrary file is written by the agent.

Mouse on menu/dialog/cursor/wheel/output, Shift movement/click selects. Ctrl+C copies in the editor, F6 cancels. UI example `qbs edit samples/qbsharp/editor.bas`, F5. Single document / no find-replace / OS clipboard / drag / multi-tab; display beyond Latin-1 shows '?' without altering the file, Unicode cell widths are incomplete. The Windows workflow is verified with ConPTY; the Unix backend and the real font/resize UX are still manual smoke tests.

## Standard library, classes and modules

```vb
Import "lib/models" As Models
Dim c = New Models.Ops.Counter(20)
c.Add(1)
Print(c.Twice())
Dim data = Json.Deserialize("{""Name"":""deploy"",""Count"":42}")
Print(Yaml.Serialize(data))
```

Import is relative to the importing file, then $HOME/.qbs/lib/stdlib; File/Directory/Config are standard cwd. Config qbs.yaml or qbs.yml data only, data-only YAML with no tag/anchor/alias. Limits/differences in SPEC.

## Embedding and verification

A separate net10.0 Core, without implicit AI/UI:

```csharp
using QBSharp.Core.Hosting;
await using var session = new ScriptEngine().CreateSession();
await session.ExecuteAsync(new ScriptSource("Dim answer As Integer = 6 * 7", "job.bas"));
var next = await session.EvaluateAsync(new ScriptSource("answer + 1")); // 43
```

StandardLibrary is opt-in through CreateBindingsAsync; AI is opt-in by referencing QBSharp.AI + RalfHost.AddBindings or DI AddQBSharpAI/IRalfClient fake. Do not expose settings/key objects to the script.

The xUnit suite is offline/fake with parser/runtime/library/CLI/editor/AI/secrets and .bas fixtures; native Windows ConPTY includes F7 disabled. `dotnet msbuild ops/verify-tool.proj` verifies the installed tool/DLL without the cloud. An authorised live smoke test, a small JSON+assertion, does not print the response/key:

```powershell
dotnet run --file ops/smoke-ralf-live.cs -- 'C:\path\to\RalfAI\config.json'
# optional against the installed DLL:
dotnet msbuild ops/verify-tool.proj -p:LiveAiConfig='C:\path\to\RalfAI\config.json'
```

SPEC.md is the contract; USAGE.md examples; ARCHITECTURE.md the design; OPENPOINTS.md gates/limits; PROMPTS.md goals. The external PySharp README/ARCHITECTURE were read during phase 7: the AST/library/tool/adapter pipelines are separate and consistent; full code-style alignment remains to be verified.


## Samples and historical Gorillas

The examples are in **samples/qbsharp/**, next to src/ and ops/ (moved from src/examples, bytes preserved). Tests and documentation point to the new path; the fixture copied into the tests is samples/qbsharp/.

`gorillas.bas` is **QBasic Gorillas Deluxe 2.2**, based on the Microsoft/IBM original with modifications by Daniel Beardsmore, not a QBS port. Verified with the modern qbs dialect: **exit 3, QBS1003 at (10,24), suffix '#' not supported**. It does not start or run partially; it also requires DEFINT/DECLARE/TYPE/DIM SHARED/classic arrays/CONST/For/Do/SelectCase/GoSub/OnError, PRINT without parentheses, SCREEN/PALETTE/LINE/CIRCLE/GET-PUT/PSET/POINT graphics, PLAY/SOUND audio and DOS memory DEF SEG/PEEK/POKE. Screen9 PCCanvas now provides an indexed PC framebuffer, but it does not automatically convert the QBasic syntax or the game's DOS environment.

Opening it in the editor does not mean being able to run it. To play the classic file you need a compatible QBasic environment (for example QBasic in DOSBox, not verified here), or a separate port with a dedicated graphics/compatibility backend. The original file remains unchanged, no silent conversion.

## PCCanvas: logical CGA/EGA/VGA graphics

ConsoleLib 1.2.0 adds `PCCanvas : Canvas`, exposed as the standard-library `PC`. Supported modes 1, 2, 7, 8, 9, 11, 12, 13, indexed palettes/pages, mapped VRAM, primitives/paint and sprites Copy/Preset/Xor/And/Or. The native terminal preview is reduced to cells/16 colours, not register-accurate hardware or a full VGA display. The original Gorillas syntax is unsupported in modern qbs; opt-in QBasic runtime services are described below.

```vb
Dim c = PC.Create(9)
PC.Box(c, 20, 20, 100, 60, 6, True)
PC.Circle(c, 320, 50, 28, 14)
Dim sprite = PC.Get(c, 20, 20, 20, 20)
PC.Put(c, sprite, 200, 130, "xor")
PC.Render(c, 80, 25)
```

```powershell
qbs run samples/qbsharp/pc-canvas.bas
qbs test samples/qbsharp/pc-canvas-test.bas
# Native reduced colour preview (requires a terminal >= 80x25):
qbs run samples/qbsharp/pc-canvas.bas -- render
```

The library package is `Screen9.1.2.0.nupkg` in the local NuGet feed and in ops/artifacts/nuget-feed. Detailed modes/memory/API/limitations in SPEC. No runtime Spectre dependency.

### Global release confirmed
qbs 0.7.0-alpha.2 was installed globally and verified, and includes RalfAI and Screen9 PC. The ops/artifacts/tools path is no longer needed to use PC: qbs run samples/qbsharp/pc-canvas.bas and qbs test samples/qbsharp/pc-canvas-test.bas. The retry did not terminate user processes.


## QBasic foundations in the source (opt-in)

The new QBSharp.QBasic project keeps the classic dialect separate from modern qbs. Arrays/ByRef/loops/Gosub/error handlers/DATA/numbered files verified, including regressions on REDIM, aliases, CONST, single evaluation and overflow. It is not yet the working Gorillas game nor full DOS compatibility.

```powershell
dotnet run --project src/QBSharp.Cli -- test samples/qbsharp/qbasic-core.bas --dialect qbasic
dotnet test src/QBSharp.slnx -c Release
```

The global tool 0.7.0-alpha.2 was not updated for this gate: the dialect and the new services are tried from the source.

### Raster text and QBasic services: source gate closed

Virtual memory DEF SEG/PEEK/POKE, host-injected INKEY$/SLEEP, BEEP/SOUND/PLAY audio with an explicit backend, PCCanvas statements and LONG-array sprites are in the source. Raster PRINT, LOCATE/COLOR/WIDTH, VIEW PRINT and CLS 0/1/2 are now recompiled and verified; wrap/TAB, state after invalid input, background reset and cancellation after a host call were fixed.

**Debug/Release builds with 0 warnings/errors; the full suite 528/528 passed in both, 0 skipped.** It includes 35 new text/raster/process tests and the fixture `samples/qbsharp/qbasic-raster-test.bas` (10 assertions).

```powershell
dotnet run --project src/QBSharp.Cli -c Release -- test samples/qbsharp/qbasic-raster-test.bas --dialect qbasic
```

Gorillas passes the virtual initialisation and the original drawing routines, but is not yet declared playable: a complete match must be verified. The compact font is approximate, the console preview is cell-based, not IBM ROM / a faithful pixel renderer. The Windows CLI audio is opt-in QBS_QBASIC_AUDIO=1, background playback is not faithful; the normal suite runs without sound/network. SPEC §17 details the contracts and limits; OPENPOINTS distinguishes verified source from the installed tool.

### Gorillas: interactive wait fixed
In the source, the interactive QBasic run without an explicit budget uses limited steps **between INKEY$ polls** and 10 ms pacing; batch/test/embedding and --max-steps keep a total budget. The terminal uses the alternate screen with restoration before diagnostics, Screen9 frames are limited to about 30 fps. Use only trusted scripts for the interactive policy; it is not a sandbox.

```powershell
dotnet run --file ops/launch-gorillas.cs
```

A Windows launcher from the repository, the already compiled Release executable, a separate window and a temporary workspace. It does not use the global qbs, does not modify gorillas.bas, does not terminate user windows; it removes the budget env only in the child. Ctrl+C interrupts. Gate **551/551 Debug/Release**: ConPTY verifies the introduction/key/arrival at player creation and cancellation, **not** a complete match. Source and precise API in SPEC §18; the global tool 0.7.0-alpha.2 is still to be updated.

### F5 in the editor now supports QBasic (source)

```powershell
dotnet run --project src/QBSharp.Cli -- edit samples/qbsharp/gorillas.bas --dialect qbasic
# An isolated Release build, without closing previous editors:
dotnet build src/QBSharp.slnx -c Release --artifacts-path ops/artifacts/editor-dialect
dotnet run --file ops/launch-gorillas-editor.cs
```

Run -> Language / dialect selects QBS/QBasic; the default is modern, with no automatic change to the .bas. **F5 runs the buffer, F8 code/program view, F6 Stop, Ctrl+Q Exit.** Keys in the program view go to INKEY$, not to the buffer. 80x30 recommended; an indexed Screen9 frame with a cell preview. INPUT uses the editor dialog, output/errors/F4 stay integrated. Windows audio opt-in QBS_QBASIC_AUDIO=1; the launcher enables it in the child. F7 AI code is currently blocked in QBasic with an explanation, so it does not generate wrong syntax.

**564/564 tests Debug/Release**, ConPTY F5 on the original Gorillas up to instructions/input/player creation/F6, not a complete match. The interactive QBasic quota is 10M steps between polls, needed for CalcDelay too; explicit total/batch/embedding budgets unchanged. The global tool 0.7.0-alpha.2 still lacks this integration; use the source/new launcher, not the already open window.

### Editor optimisation and the test version of Gorillas

Optimised source: graphics projections only when due, no lexer/hidden code in the program view, line/style cache and grouped segments, input queued without an artificial delay. Gate **579/579 Debug/Release**. Offline benchmark: preparation of 200 frames 83.14 -> 18.35 ms and allocations 114.9 -> 22.7 MB; the overall terminal renderer improves little, no FPS promise for the game.

The `samples/qbsharp/gorillas.bas` found during this check is an IBM 1991 fragment that ends with DI; **not modified**. The complete Deluxe 2.2 version tested earlier is kept separately, byte-identical, in **samples/qbsharp/gorillas-deluxe.bas**; the historical end-to-end tests point to that one.

```powershell
dotnet build src/QBSharp.slnx -c Release --artifacts-path ops/artifacts/editor-perf-after
dotnet run --file ops/launch-gorillas-editor.cs -- samples/qbsharp/gorillas-deluxe.bas
# Local benchmark (offline, not part of the ordinary tests):
dotnet run --project src/QBSharp.Benchmarks -c Release --artifacts-path ops/artifacts/editor-perf-after -- ops/artifacts/perf-latest.json
```

Windows that are already open do not receive updates. No update of the global tool; use the source/launcher and the full path you want. The game's pauses, the AST and sequential MB audio remain limits.

## Modern consolidation — loops
The 0.7.0-alpha.3 release includes For/Next, Do/Loop and Exit in modern BASIC with classes, besides the QBasic/editor services developed in the source after alpha.2. ElseIf, Select Case and For Each are the next gate. No feature removed; a complete Gorillas match is not certified.

## Modern selection and iteration
Source 0.7.0-alpha.4: `ElseIf`, `Select Case` and `For Each` in the qbs dialect; classes preserved. Build/test/release gate to be reconfirmed.
- Multi-line ElseIf, Boolean conditions, evaluated only up to the first true branch; a single End If, no new scope compared with If.
- Select Case: the selector is evaluated once; Case value, comma list, inclusive range `a To b`, `Is <|<=|=|<>|>=|>` and a final Case Else. First match, no fall-through. Tests evaluated in order and short-circuit, CLR comparisons of the modern dialect (ordinal case-sensitive strings). No Exit Select.
- For Each counter [As type] In expression / Next [counter]: a synchronous CLR IEnumerable, including collections and host objects. Without As the counter must exist; with As it is local per iteration, with checked conversion. The collection is evaluated once, the enumerator is disposed on end/Exit/error/cancellation, Exit For is shared with the numeric For. No snapshot, changes to the collection follow CLR rules; no IAsyncEnumerable or pattern-only enumerables. Synchronous host calls are not pre-emptible.
- Dedicated ASTs and GuardedParser visits all the new children. The REPL recognises End Select; completion includes the keywords.
- Coverage ModernSelectionTests and samples/qbsharp/modern-selection.bas. Run `qbs test samples/qbsharp/modern-selection.bas` after updating the tool.

### QBasic pixel graphics on Windows
From the source: in CMD set `set QBS_GRAPHICS=pixel` and `set QBS_FRAME_PNG=`, then `dotnet run --project src/QBSharp.Cli -c Release -- run samples/qbsharp/gorilla.bas --dialect qbasic`. A Win32 presenter on the Screen9 framebuffer; the whole frame at 4:3, without conversion to cells. Prompt/line input in the console, keyboard also accepted from the window. Editor and global tool not updated by this gate.

For Gorillas with a historical calibration that is too slow, in CMD `set QBS_TIMER_WAIT_SCALE=0.001` before running from the source reduces only the empty TIMER waits. Opt-in, not faithful in durations; default 1. Details in USAGE.md; reproducible measurement with `dotnet run --file ops/measure-gorilla-calibration.cs -c Release`.

The Windows presenter was updated with an off-screen backbuffer and a repaint cache. Gate 704 tests Debug/Release. With a Release process open, the new build verified: `dotnet ops/artifacts/double-buffer/bin/QBSharp.Cli/release/QBSharp.Cli.dll run samples/qbsharp/gorilla.bas --dialect qbasic` (set QBS_GRAPHICS=pixel and the timing from USAGE). The global tool was not updated.
