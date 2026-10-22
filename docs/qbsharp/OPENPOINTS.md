# Open points

## Latest gate: global tool 0.7.0-alpha.4 installed

- `qbs` updated from 0.7.0-alpha.3 to **0.7.0-alpha.4** (package `ops/artifacts/QBSharp.Cli.0.7.0-alpha.4.nupkg`, built with a separate `--artifacts-path` because an open Gorillas was locking `bin/Release`). It contains all the work up to background music: IBM fonts, single window, LINE B/BF, PLAY "MB".
- Screen9 2.4.1: the fonts' LICENSE.TXT/SOURCE.TXT also as contentFiles copied to the output; QBSharp.QBasic/StandardLibrary reference 2.4.1 with `PrivateAssets="none"` so they reach the CLI and the tool (`tools/net10.0/any/Screen9.Fonts/`). Before, the tool redistributed the fonts without the licence.
- Gate: Debug 733/733 (Release not recompiled in the repository because of the file lock, a separate Release pack succeeded). Smoke tests on the installed tool: `--version`, run hello.bas, `test` qbasic-core (8 PASS), modern-selection (2), modern-loops (5), qbasic-raster-test with QBS_RASTER_FONT=compact (10), PLAY "MB" with muted audio (`PLAY(0)`=5). The licence is present in the tool's store.
- README/USAGE: references to alpha.2 and to the non-existent scripts `ops/install-tool.ps1`/`verify-tool.proj` replaced.

## Historical gate: background music, 733/733

- `PCSpeaker`: a buffer of 32 tones on a separate task; `PLAY "MB"` returns at once (it waits only when the buffer is full), foreground/SOUND/BEEP queue after the music and wait; `PLAY(n)`; at the end of the program the music finishes, on error or interruption it stops at once. A direct path for foreground tones with an empty buffer: without it, the thread hops per note took the suite from ~15 s to ~45 s (Gorillas plays hundreds of foreground notes).
- In Gorillas the music of the introduction, of the explosions and of the victory dance now plays while the animations go on.
- 6 QBasicSpeakerTests tests (background, foreground after the queue, full buffer, interruption, backend error, PLAY(n)). Debug/Release build 0 warnings, **733/733 from PowerShell**. An intermediate run had an "Access to the path is denied" error when the editor saved in Temp (EditorTerminalProcessTests), not repeated: probably a file locked by a third party.

### Residuals
SOUND in MB mode, overlapping tones and PLAY with variables (`X`/`=`) are not supported. Real audio only on Windows (Console.Beep).

## Historical gate: LINE B/BF fixed, 727/727

- Bug: `LINE (x1,y1)-(x2,y2),c,B|BF` called `PCCanvas.Box(x,y,width,height)` passing the second vertex as width/height. In Gorillas every 3x6 window became a huge rectangle: overlapping yellow buildings, deformed gorillas apparently inside the buildings. Now width/height = |x2-x1|+1, |y2-y1|+1 (inclusive vertices as in QBasic). No test covered the box dimensions.
- Regression `QBasicDeviceTests.LineBoxCornersAreInclusiveAndNotWidthHeight`. The Gorillas city reproduced without a UI: red/cyan/grey buildings with windows, gorillas on the roofs. Debug/Release build 0 warnings, **727/727**.

## Historical gate: single QBasic window, 726/726

- `QBS_GRAPHICS=pixel`: a single window for SCREEN 0 text (Screen9 VGA 8x16 font, 640x400), graphics and INPUT. New `QBasicOptions.ScreenInput`: INPUT/LINE INPUT with prompt, "? " and echo on the screen, keys from ReadKey, Backspace across lines too. The parser records whether INPUT shows "? ". The terminal presenter is not created in this mode; window title "QBSharp — QBasic".
- Hidden start (`Start-Process -WindowStyle Hidden`): ShowWindow repeated, otherwise the window stayed invisible. Verified: a single visible window with Gorillas.
- 4 ScreenInputTests tests (echo, "? ", LINE INPUT, Backspace across a line, 640x400 text capture). Debug/Release build 0 warnings, **726/726 from PowerShell**.

### Residuals
No blinking cursor during INPUT. Final errors on a hidden console are not visible: launch from a terminal to diagnose. No dedicated launcher.

## Historical gate: IBM ROM fonts in Screen9 2.4.0

- The IBM EGA 8x8, EGA 8x14 and VGA 8x16 ROMs with LICENSE.TXT (CC BY-SA 4.0) and SOURCE.TXT moved from `src/QBSharp.QBasic/Fonts` to Screen9 2.4.0 (`PCFont.ForHeight`, `PCVideoMode.Font`): the font belongs to the device. QBSharp no longer contains fonts or resources; FontDirectory/QBS_FONT_DIR remain overrides, heights other than 8/14/16 use the 5x7 (new test UnusualCellHeightsFallBackToCompactFont).
- `samples/qbsharp/Gorilla/GORILLA.BAS` replaced by the user with the Microsoft 1990 original (1135 lines, ending with GAME OVER, a key and END, without "play again"). GorillasCompatibilityTests (Microsoft 1990 copyright) and CurrentGorillaTests (`again$` removed) updated: a one-point match without a UI reaches GAME OVER with Alice 0, Bob 1.
- Debug/Release build 0 warnings, **722/722 from PowerShell**.

## Historical gate: exact IBM ROM fonts in the repository

- IBM EGA 8x8, EGA 8x14 and VGA 8x16 ROMs in `src/QBSharp.QBasic/Fonts` (ibm8xN.rom, embedded resources), converted from the Bm437 .FON files of The Ultimate Oldschool PC Font Pack v2.2 by VileR (archive SHA-256 ec0aad90…2933). CC BY-SA 4.0 licence in `Fonts/LICENSE.TXT`, attribution in `Fonts/SOURCE.TXT` (required by the licence). No dependency on Windows\Fonts or user folders: FontDirectory/QBS_FONT_DIR remain overrides.
- Mode 9/12 PNGs with the original ROM glyphs. New test EmbeddedRomFontsMatchEgaAndVgaGlyphs.

## Historical gate: IBM fonts in the Screen9 2.3.0 device, 720/720

- Fonts and glyphs moved to Screen9 2.3.0 (`PCFont`, `PCVideoMode.CharacterHeight/TextColumns/TextRows`, `PCCanvas.DrawGlyph/ScrollUp`): the character generator belongs to the device. QBSharp keeps the cursor, PRINT/LOCATE/VIEW PRINT, control codes, QBS_FONT_DIR/QBS_RASTER_FONT and the 5x7 fallback. IbmBitmapFont.cs and the InternalsVisibleTo added for its tests were removed; parser and search tested in Screen9 (155 tests).
- QBasic/StandardLibrary references to Screen9 2.3.0 from the local feed. Test PNGs identical to before. IbmFontTests rewritten as a QBasic integration with synthetic ROMs (4 tests, deterministic without system fonts).
- Updated to Screen9 2.3.1 (DrawGlyph overflow, block ScrollUp). The Windows Fonts folder is passed to PCFont.Find only if the Windows folder exists, not a relative `./Fonts` on Unix.
- Debug/Release build 0 warnings; **720/720 from PowerShell** (also after 2.3.1). From Git Bash 1 known environment failure (ConPTY Ctrl+C) and, with TERM set, Spectre ANSI in redirected output.

## Historical gate: IBM CP437 raster font 710/711

- Text in graphics modes with the 8-pixel IBM OEM font (IbmBitmapFont/IbmFontCatalog): exact `ibm8xN.rom` dumps if provided, otherwise Windows .fon (cga80woa 8x8 exact; centred 8x12 for 14/16 cells). Lower case, double/single box drawing, B0–B2 shades, Greek and symbols ☺♥► now correct. The 5x7 fallback is unchanged and selectable (RasterFont=Compact, QBS_RASTER_FONT=compact).
- No font included in the repository. NE/FNT 2.00/3.00 parser and bounded raw, malformed inputs rejected.
- Debug/Release build 0 warnings/errors; **710/711 in both**, 7 new IbmFontTests tests, historical pixel tests pinned to Compact. `OriginalGorillasIntroWaitAcceptsKeyWithoutExhaustingInteractiveBudget` fails (ConPTY: after Ctrl+C the process does not exit within the timeout): reproduced identically with the IBM font disabled, so **not** caused by this gate; to be investigated. With TERM=xterm-256color in the environment 3 redirected-process tests see ESC: run the suite without TERM.

### Residuals

Original EGA 8x14 and VGA 8x16 glyphs only with .rom dumps provided by the user. The editor (80x25 cell projection) and the Screen9 cell presenter do not use the raster font: only the pixel framebuffer/PNG. Unix without .fon: requires QBS_FONT_DIR with .rom. Global tool not updated.

## Historical gate: optimised editor rendering 579/579

- Lazy projection before the throttle, a frame mailbox with latest/dirty/readonly copy, no hidden code/lexer in the program view, cached line offsets/styles and segment grouping. Refresh only for changed frames/UI; queued keys without delay, empty polls paced/cancellable.
- Debug/Release build 0 warnings/errors; suite **579/579 in both**, 0 skipped. 15 new deterministic regressions. A separate benchmark was added to the solution, offline with warmup 3 trials / median / correctness checks.
- Benchmark before/after: Runner 1000 pixels 78.55 -> 21.57 ms / 29.3 -> 5.1 MB alloc; FrameBuild 200 83.14 -> 18.35 ms / 114.9 -> 22.7 MB alloc; Build+Render 493.45 -> 478.59 ms / 160.5 -> 27.9 MB alloc. Not FPS/real time/resident memory, the terminal renderer is still the bottleneck; headless control 152.08 -> 146.98 ms.
- The initial gate had 7 failures: the file samples/qbsharp/gorillas.bas had changed compared with the earlier fixture. An IBM 1991 fragment ending in DI was found, without the complete program. **Not modified**, detected SHA256 724DEB8FAF73440E1888D490D4428C4A3A7FA07A019DBF61F14ED0EF77EA79D5.
- The complete earlier Deluxe 2.2 from the editor-dialect artifact copied byte-identical as samples/qbsharp/gorillas-deluxe.bas (68891 characters), no overwrite. Historical/ConPTY tests retargeted to the stable name; the working file remains the one chosen by the user.
- Isolated Release editor-perf-after; the launcher prefers the new binary and accepts an optional sample. No user process terminated, no pack/install/global update/AI network.

### Residuals

A complete match / victory / ranking / persistence are not attested. The current incomplete Gorillas.bas is not playable: use the preserved complete copy for tests or provide a complete program. AST and TIMER pauses / sequential MB audio remain costs, pixel renderer / IBM fonts / Unix to be completed. No global FPS gain promised. Global tool 0.7.0-alpha.2 still without these gates. DB/templates/dynamic assemblies open.

## Historical gate: QBasic editor 564/564

- edit --dialect qbasic and the Run -> Language / dialect menu, default qbs; F5 buffer -> engine of the chosen dialect, F8 program view, input from the UI, F6/token cancellation and F4 source diagnostic. The original file is not modified.
- The worker uses Screen9/latest bounded frame copies, not native consoles. INPUT dialog and INKEY$ queue; Spectre UI for the editor. Windows audio opt-in/callback, tests without sound; F7 QBS-only AI code explicitly blocked in QBasic before sending.
- A QBasic interactive quota of 10M between polls common to CLI/editor: a real test detected CalcDelay beyond 1M before input. Modern/batch/test/embedding defaults and explicit CLI/env total budgets unchanged. A trusted policy, not a sandbox.
- Debug and Release build 0 warnings/errors; **564/564 tests passed in both**, 0 skipped. 13 new tests, including ConPTY F5 on the original Gorillas -> instructions -> key -> player creation -> F6 Canceled -> CtrlQ exit 0, file unchanged. Not a whole match.
- Isolated Release in ops/artifacts/editor-dialect so as not to overwrite files of the open editor. The new launcher passes --dialect qbasic and prefers the isolated one; user windows not closed. Global tool still 0.7.0-alpha.2 without the new dialect/services, no pack/install.
- SPEC §19/README/USAGE/ARCHITECTURE/PROMPTS updated.

### Still open

A complete match / throws / collisions / scores / persistence, faithful pixel renderer / IBM CP437 fonts, input/Unix terminals, MB audio scheduler. F7 QBasic AI code not implemented, an intentional choice until the guide/validation parser are aligned. Global distribution separate; an old window does not pick up in-memory updates. DB/templates/dynamic assemblies still planned.

## Historical source gate: interactive wait 551/551

- A real bug reproduced: the total budget of 10M was exhausted in SparklePause while reading the introduction. An interactive QBasic run without an explicit budget now uses a budget between INKEY$ polls and 10 ms pacing; not a simple increase of steps.
- The Total default of embedding/batch/test/--max-steps/QBS_MAX_STEPS is unchanged. BetweenInputPolls is for trusted programs; periodic polls can last indefinitely, use a timeout/Total for untrusted code. Loops without polls are still limited.
- A presenter for Screen9 Canvas with alternate screen, 33 ms cadence / pending flush, colours / partial OEM box drawing; restoration before errors/exit/Ctrl+C, no prompt mixed into the game screen.
- Debug/Release build 0 warnings/errors; **551/551 tests passed in both, 0 skipped**. It includes 23 new polling/output/native ConPTY regressions. In the Release gate a sharing race of the old editor test emerged: retry 32/33 within the deadline, then the complete suite reconfirmed in both.
- A native test of the **original Gorillas** with a test config without audio: INSTRUCTIONS wait, key, arrival at Enter name of new player and Ctrl+C 130. A native proof of an error after a key / restoration-before-report as well. Original file unchanged, no network/AI/audio in the tests.
- ops/launch-gorillas.cs uses the Release source, not the global tool; it omits --max-steps 10M and removes the budget env only in the child. It does not close user processes.
- SPEC §18/README/USAGE/ARCHITECTURE updated. No pack/install: the global tool stays 0.7.0-alpha.2 without the new dialect/services.

### Still open

A complete match (player setup, throws/collisions, victory/scores, persistence/reloading and exit), real audio / MB scheduler, complete pixel renderer / IBM CP437 fonts and Unix terminals. Intro/key/setup verified do not attest an end-to-end playable match. Release of the new tool is separate. DB/templates/assembly imports remain planned.

## Historical source gate: raster text/VIEW PRINT/CLS 528/528

- Debug and Release build exit 0, 0 warnings/errors, net10.0.
- The **complete suite 528/528 in both**, 0 skipped: no filter limited to the dialect. It includes 35 new raster/text/CLI tests and the sample qbasic-raster-test.bas with 10 PASS assertions.
- The first current suite of 493/493 was green; new regressions found wrap/TAB, WIDTH/COLOR state after an error, background on SCREEN change and cancellation after a host call. Fixed and the gate repeated. The ANSI test uses an Ordinal comparison to avoid the ignorable control chars of xUnit's culture comparison.
- Source services: virtual memory DEF SEG/PEEK/POKE, INKEY$/SLEEP callbacks, BEEP/SOUND/PLAY host events, legacy primitives and LONG sprites/PCCanvas, text/VIEW PRINT/CLS. No access to real RAM, audio/cloud in the tests or modification of gorillas.bas.
- SPEC §17/README/USAGE/ARCHITECTURE updated. The installed tool is still 0.7.0-alpha.2: **no pack/install in this gate**.

### Still to verify for Gorillas

1. A complete original match: setup/input, trajectory/collisions/explosions, victory/score/ranking, persistence/reloading, exit/cancel. The current tests reach the intro wait with a controlled clock/budget and exercise the original graphics routines; this is not an attestation of a working game.
2. A faithful IBM/CP437 font and pixel-window renderer. The current approximate 5x7 font, raster lower case as capitals, fallback glyphs and a Screen9 cell / 16-colour preview.
3. Real audio and the PLAY MB scheduler: a sequential opt-in Windows CLI adapter, a host callback tested offline. Alt/OEM/BIOS keyboard and complete DOS VRAM mapping not implemented.
4. Distribute the dialect/services with a new version and a smoke test of the installed tool: the global one still lacks these features.

The sections below keep the historical gates and their diagnostics: **DEF SEG/BEEP are no longer the current blocker of the source**. Modern QBS limits remain unchanged; DB/templates/assembly imports still open.

## Historical source gate: QBasic compatibility 461/461

The initial suite ran 444/444 green. 13 targeted regressions were added, 9 failed: REDIM type/default dynamic, typed array suffix alias, ByRef array depth, CONST temporary, null/index re-evaluation, READ/member target evaluation, host variable type, Single overflow. All fixed and reconfirmed; 4 error origin/reset/Gorillas tests added. Final gate: Debug and Release 461/461 passed, 0 skipped; build 0 warnings/errors. Sample qbasic-core.bas 8 PASS assertions in CLI Release. Gorillas runtime exit 4 confirmed: BEEP(323,2) during the handling of DEF SEG not implemented (154,2). No fake graphics/audio/DOS connection, no pack/install in this source gate.

QBasic is opt-in --dialect qbasic for run/test only, separate project/embedding; modern unchanged. SPEC/README/USAGE/ARCHITECTURE now really describe the subset and limits. No new pack/install: the global 0.7.0-alpha.2 still lacks the dialect; a later distribution gate.

Gorillas: the parser goes through the whole original, the runtime BEEP fails in the handler and the cause is now visible: DEF SEG not implemented. Graphics statements / binary sprites, audio/input/BIOS and a faithful pixel renderer still open. Original file not modified, no fake no-op; tests in a temp workspace with no files created. No AI network calls.

### History of earlier gates


## Last closed gate: global qbs 0.7.0-alpha.2

- The installation retry succeeded: global qbs really updated 0.6.0-alpha.1 -> 0.7.0-alpha.2, without closing user processes.
- Build succeeded 0 warnings/errors; Release suite 426/426 passed, 0 skipped (external Screen9 1.2.0: 60 tests green in the earlier gate).
- ops/verify-tool.proj exit 0: exact version/help/CLI/REPL/Import/JSON-YAML/config/file/assert/completion and 2 ConPTY tests against the installed global DLL.
- Fixed samples/qbsharp/repl-phase4.txt: Import is now samples/qbsharp/lib/models, not src/examples/lib/models; verification repeated green. No change to the binary or to gorillas.bas.
- PC wrapper verified on the global command: EGA sample run and pc-canvas-test.bas 12 PASS assertions, plus 2 ConPTY tests against the global package (verify-pccanvas-package.proj with Tool/InstalledCli override).
- Screen9 1.2.0.nupkg already persisted in the authorised feed and copied to ops/artifacts/nuget-feed. Tool package ops/artifacts/QBSharp.Cli.0.7.0-alpha.2.nupkg, no nuget.org publication.
- RalfAI script/editor F7/external config and Screen9 runtime/PC now available globally too; a minimal cloud smoke test had succeeded before the release, no new AI call made in the retry.

### PCCanvas: implemented and residual limits

PCCanvas derives from Canvas: modes 1, 2, 7, 8, 9, 11, 12, 13, CGA/EGA/VGA DAC 6-bit palettes, active/visual pages, PSet/Point/primitives/fill, sprites copy/preset/xor/and/or, CGA interleaved / EGA planar / VGA linear VRAM. Logical indices and RGB preserved; the console preview is reduced to cells / 16 colours. No BIOS/register/CPU/timing/DOS/audio emulation, mode 0/10 and QBasic Long-array sprites not supported; the default VGA palette is deterministic, not ROM-exact. Inherited Canvas primitives are cell-based, use PC.Get/Snapshot for indices.

The original Gorillas is still not runnable: classic QBasic syntax is missing even with the new graphics backend. File preserved; a port/compatibility requires a separate effort. The next gates are 8 database/templates and 9 assembly imports, not implemented.

## History: phase 6 gate

`qbs edit [file.bas]` is available in the global tool **0.6.0-alpha.1**. A full-screen old-style Spectre editor with File/Edit/Run/Help menus, keyboard/mouse, a single buffer, cursor/scroll/selection, internal clipboard, undo/redo, New/Open/Save/Save As and dirty/overwrite confirmations. Execution of a buffer snapshot in an isolated AST session, dedicated output, script dialogs, file/line/column navigable diagnostics and cooperative Stop.

Phases 1–5 already completed: interpreter/embedding/CLI, classes/namespaces/New/Import and the filesystem/JSON/YAML/config/UI standard library. No C#/VB/IL generation or Roslyn compilation of scripts.

### Release verifications

- Debug and Release builds reconfirmed **exit 0, 0 warnings/errors**, net10.0/C#14.
- Release suite reconfirmed **316/316 passed, 0 skipped**. Debug suite 316/316 green at the closing of the previous source gate.
- Package created: **ops/artifacts/QBSharp.Cli.0.6.0-alpha.1.nupkg**.
- Global tool really updated **0.5.0-alpha.1 -> 0.6.0-alpha.1**, current user, no nuget.org publication.
- **ops/verify-tool.proj exit 0** after fixing MSBuild home resolution through USERPROFILE/HOME (no change to the distributed code).
- Verified on the installed tool: exact version, help/edit help, run of old examples/classes/JSON-YAML, assert tests, completion, REPL with redirected stdin/BOM/classes/Import/data/non-blocking UI.
- Temp filesystem smoke: **8 PASS assertions**, directory created and cleaned; YAML config smoke **1 PASS**, temp removed.
- **Windows ConPTY against the installed package DLL: 1 test PASS**. Help menu through the mouse, Help dialog, entering Print(42), saving with a content check, run Completed and CtrlQ exit 0. Not only the local build DLL.
- README/SPEC/USAGE/ARCHITECTURE/PROMPTS/OPENPOINTS consistent with phase 6.

Editor suites: EditorTests (document/controller/frame/decoder/I-O/run/cancel/lifecycle), EditorRegressionTests (editor.bas whole UI, Import source, redirected CLI rejection), EditorTerminalProcessTests (ConPTY). Fixture samples/qbsharp/editor.bas.

### ConPTY and infrastructure fixes

Program configures an explicit Spectre ANSI/interactive **only for edit on Windows with effective console stdin/stdout**, before the capabilities cache; EditorTerminal enables VT output and restores the original modes. Redirected ones continue to be rejected (exit 2), without creating files or ANSI sequences.

QBSharp.TerminalHarness is only a test host, not distributed in the tool: it reopens CONIN$/CONOUT$ READ|WRITE before the .NET Console cache and invokes the compiled CLI entry point in the ConPTY process. The stdout handle inherited from the test runner and the write-only CONOUT$ shell redirection were not suitable for GetConsoleMode. The test uses SGR mouse translated by conhost and the Win32 input protocol for Escape without Alt/timeout ambiguity. QBS_EDITOR_CLI_PATH points to the package DLL installed in the smoke test.

ConPTY attests an automated Windows workflow, **not** the manual UX of every terminal/font/resize and **not** a certification of the Unix backend. On other OSes the ConPTY test returns without native verification; decoder/controller tests remain cross-platform.

## Phase 7 released in tool 0.7.0-alpha.2

RalfAI.Providers 1.0.4, Ralf text/JSON, F7 consent/preview/apply, timeout/cancel and opt-in external credentials config are now included in the global tool. No agent tools/auto-execution. No secrets copied; the earlier authorised cloud JSON smoke test succeeded, offline fake/unit and ConPTY confirmed. Residuals: streaming / cost accounting / history / multi-file diff and semantic code validation not implemented.

## Samples relocation and Gorillas verification

- All 15 src/examples files moved to samples/qbsharp/ at the root (next to src/ops), identical SHA256; gorillas.bas not altered.
- Doc/ops/test references updated, fixture copied to the samples/qbsharp/ output.
- A real source CLI test: exit 3 QBS1003 gorillas.bas(10,24) unexpected '#'; the game was not started.
- GorillasCompatibilityTests: the historical source and 12 negative legacy snippets; CLI in a temp cwd with a 15 s timeout / max steps 10000, no output/file/partial execute.
- Gorillas 2.2 requires classic QBasic syntax/arrays/control flow and CGA/EGA graphics / audio / DOS memory that are absent. Fixing '#' or PRINT is not enough; a port or a compatibility backend is separate work, not automatically part of phase 8.
- Final gate: Debug and Release builds exit 0, 0 warnings/errors; complete suite 386/386 passed in both, 0 skipped. It includes 14 new Gorillas regressions. The process test normalises the wrapping of the path name; the lexer/embedding span was verified exactly (10,24). No package/global tool updated in this step.

## Later phases

8. Parametric SQL / dynamic EF proof of concept and inline templates (delimiter/interpolation/escaping/indentation to be defined).
9. Opt-in dynamic assemblies/NuGet, AssemblyLoadContext / trust / versions / collectible cache.

## Explicit limits and debt

### Editor

- Single document; no multiple tabs / find-replace / debugger / editor completion / OS clipboard / drag selection / bracketed paste. Internal clipboard only.
- Input/path dialogs are end-of-line, the default already in the field; backspace to edit it, no multi-line editor in dialogs. UI message/select/multi dialogs, Window/Table/Progress in the output.
- Document max 1Mi UTF-16 / I/O 4MiB, undo 64 snapshots (not a gap buffer/rope or general memory quota). Lines are scanned for movement/render; performance on large files to be improved.
- Fixed-cell rendering, Latin-1; controls sanitised, a tab displayed as 1 space, Unicode beyond Latin-1 shown as '?' without changing the file. CJK/emoji/combining/display-width incomplete; newline/UTF-8/surrogate movement preserved. Highlighting of the first 4096 chars per line.
- Sizes 40x12..300x100; below the minimum rendering/editing are suspended until resize. Avoid small terminals; a reduced message/fallback still to be improved.
- Windows native input and VT output verified with ConPTY; the Unix Console.ReadKey/SGR backend is best-effort, to be tried on real terminals. OS CtrlC/cursor/screen restoration UX to be verified manually beyond the fake/native normal-exit lifecycle tests.
- F5 snapshot / new session, does not save implicitly. Args empty, fixed 1M steps (edit does not accept --max-steps nor use QBS_MAX_STEPS); Import modules on disk relative to the file name, filesystem/config cwd.
- Editing during a run is allowed; F4 refuses to jump if the buffer changed, requires a rerun. Import errors can open the definition file with dirty confirmation; host/config errors without a span are not navigable.
- Output max 1000 lines / 262144 characters queued / run, not a system console/REPL/debugger. A script error does not determine the normal editor exit 0, it stays in the output; host cancellation 130.
- Save compares disk text vs the last savedText, temporary + move / confirmed overwrite: not locking/race-proof/durability/cross-OS transaction, parents existing. No autosave/crash recovery.
- Stop/Exit are cooperative; sync host code is not forcibly abortable, exit waits for the worker. Not a sandbox.

### Language/Core

- Class only public / auto-property / one constructor / one member per name; no explicit getter-setter / Private / Shared / Readonly / inheritance / nested / generic script class / overload / interface / events.
- Namespace only Class/Namespace, no Dim/Function/Import; no Class/Namespace inside Function/Sub. Member types resolved at construction/invoke, no hoisting.
- If/While have no scope; a repeated Dim in the loop fails. Without As it is dynamic, not VB.NET static inference. A script class is not a CLR Type, use List(Of Object), not List(Of ScriptClass); descriptor identity, not structural typing.
- The CLR registry is built-in/host opt-in; no autoload/static/generic methods/ByRef/Optional/ParamArray/extensions. A simple reflection binder, a reflected Task is not awaited / HostFunction is, the cache keeps the Type for the engine lifetime.
- Module cache is evaluation not I/O, the resolver re-reads; no reload/eviction/export-select. Failed until a new session, a cancelled loading retries. An alias collision can follow the initialiser, side effects not rolled back.
- Import FullPath is not symlink realpath/sandbox, absolute/../ allowed. Import error CLI 4 / main I/O 5. Search stdlib root but no .bas payload distributed; YAML does not configure roots.
- Parser fail-fast / AST 128, no recovery / full stack trace / general memory quota, steps 1M / call 128 / import 64; constructor Invoke counts beyond construction.
- Shared mutable hosts require synchronisation; synchronous CPU until an incomplete await. No rollback, untrusted scripts must not be run.
- Date literals and further VB.NET features remain to be scheduled; For/Do/For Each/ElseIf/Select Case are available in the modern dialect.

### Standard library

- JSON/YAML data-only; YAML single document / no anchor-alias-explicit tags (also !!str) / complex keys / duplicates / non-finite. Dates/yes-no as text, exact numeric type/identity not preserved.
- Serialises script properties / CLR public getters, not fields/methods/closures; getters run host code. Shared duplicates/cycles rejected. Limits depth 64 / nodes 10000 / text 1Mi UTF-16, not a streaming allocation quota.
- Script typed deserialise: zero-arg constructor/initialiser then properties; missing default / unknown error, side effects not rolled back. CLR mapping System.Text.Json, not a VB.NET binder.
- DynamicRecord helper names shadowed by data, missing key error; Config.Get dotted, not array index.
- Config qbs.yaml or qbs.yml cwd, mutable data snapshot / no watcher/persistence/merge/env/engine options; invalid/ambiguous exit 5 in run/test/repl, output error in the editor run. :reset reloads, informational/new independent.
- Filesystem base cwd not importer/file, existing parents, default 1MiB/file / 10000 entries, file-only glob / no ../pattern and traversal skips reparse points. RestrictToBaseDirectory host opt-in not race-proof/sandbox and not applied to Import/reflection.
- Screen9 runtime UI (Spectre only CLI/editor), Window panel/progress snapshot, table 1000x100 / homogeneous shape / nested ToString. Redirected, interactive prompts rejected, MessageBox panel + OK without read. The editor has a separate dialog adapter.
- UI output is human-readable, not CLI structured JSON. Some MSBuild/tool wrapper logs decode box drawing badly; direct UTF-8 tests and ConPTY green, font/terminal UX to be verified.

### CLI/operations

- REPL end-of-line / buffer 65536 / history 200, multi-line blocks not parentheses, no wrap/wide glyph/advanced navigation. Completion prefix not context/member-aware, max 1000 entries, PowerShell adapter only.
- Test script fail-fast / CLR Equals / no discovery/deep/tolerance. Native ConPTY Windows-only, other terminal checks manual, not automated.
- RalfAI released in tool 0.7.0-alpha.2; DB/templates/dynamic assemblies planned. PySharp README/ARCHITECTURE read in phase 7; full style alignment open. Original AI feed absent, Providers 1.0.4 trusted from the cache / local ops feed.
- Environment .NET 11 preview SDK / .NET 10 runtime, target net10.0; stable SDK pin / CI matrix open.
- InstalledCli smoke tests assume the standard dotnet tool store layout, an override property for a custom store. The ops pipeline must be configured explicitly.
- Every feature updates SPEC/tests, every phase must have green build/tests and consistent documents before release.

## Modern consolidation — loops
The previous source gate declared 628 tests; the new build/test and release gate is to be reconfirmed. ElseIf, Select Case, For Each to be implemented. No removal. Classes remain. The limits and historical results above do not always describe the current state.

## Modern selection and iteration
Source 0.7.0-alpha.4: `ElseIf`, `Select Case` and `For Each` in the qbs dialect; classes preserved. Build/test/release gate to be reconfirmed.
- Multi-line ElseIf, Boolean conditions, evaluated only up to the first true branch; a single End If, no new scope compared with If.
- Select Case: the selector is evaluated once; Case value, comma list, inclusive range `a To b`, `Is <|<=|=|<>|>=|>` and a final Case Else. First match, no fall-through. Tests evaluated in order and short-circuit, CLR comparisons of the modern dialect (ordinal case-sensitive strings). No Exit Select.
- For Each counter [As type] In expression / Next [counter]: a synchronous CLR IEnumerable, including collections and host objects. Without As the counter must exist; with As it is local per iteration, with checked conversion. The collection is evaluated once, the enumerator is disposed on end/Exit/error/cancellation, Exit For is shared with the numeric For. No snapshot, changes to the collection follow CLR rules; no IAsyncEnumerable or pattern-only enumerables. Synchronous host calls are not pre-emptible.
- Dedicated ASTs and GuardedParser visits all the new children. The REPL recognises End Select; completion includes the keywords.
- Coverage ModernSelectionTests and samples/qbsharp/modern-selection.bas. Run `qbs test samples/qbsharp/modern-selection.bas` after updating the tool.

### Gorillas console INPUT regression
Fixed the empty console answer being parsed as file EOF; added regression tests for player names followed by default points, numeric/empty comma fields and true console/file EOF. Full gameplay and global-tool deployment remain separate gates.

Verification: solution build Debug succeeded with 0 warnings/errors; complete solution tests passed in Debug and Release: 668 passed, 0 failed, 0 skipped. Eight new INPUT regressions passed. This confirms empty-answer handling, not a new full-game interactive certification. Global tool unchanged.

## Pixel diagnostics
Added an opt-in PNG export in the interactive CLI through QBS_FRAME_PNG. A windowed pixel presenter, a visual comparison of the Gorillas framebuffer and a complete match remain open. The global tool is not updated in this gate.

## Pixel window gate
An opt-in Windows run implementation: full frame, RGB palette, visible page, 4:3 layout, snapshots limited to 30 Hz, PNG 1 Hz. Remaining: visual verification with the user, interpreter performance / calibration / audio, raster input and editor integration, platforms other than Windows, original fonts/semantics. Global tool not updated.

Presenter verification: Debug build exit 0, 0 warnings/errors; Debug and Release suites 676/676 passed (none skipped), including the native Windows open/frame/close cycle. These tests do not certify the visual rendering of Gorillas nor an FPS improvement; screenshot comparison, benchmarks and editor integration are still needed.

## Gorillas Rest / TIMER
Line 2087: an empty busy-wait loop, now cooperative in the interactive budget. Remaining: verification with the external ralf launcher and a benchmark / visual match. Sample not modified, no global tool update.

Rest gate verified: Debug build exit 0 without warnings/errors, 684/684 Debug and Release tests. Eight regressions include the original Rest routine, DO/WHILE/WEND, stopped clock, Total budget, non-empty body and cancellation. No claim of an FPS increase; ralf launcher and global tool not updated.

## Gorillas throw speed
Real calibration: 256774 iterations in 520.8 ms, EGA Rest(.02) estimated at 13352.2 ms. Added the opt-in wait factor QBS_TIMER_WAIT_SCALE, an initial suggestion of 0.001. Remaining: actual rendering/FPS benchmarks and a user test. Background audio still separate. No sample modified.

Timing gate: Debug build exit 0 without warnings/errors; 701/701 Debug and Release tests. Seventeen regressions verify the default, scaling, general TIMER, preconditions, the original Rest routine with a measured MachSpeed, exclusion of loops with a body and validation. Nominal wait measured per step, not the game's FPS. Global tool unchanged.

## Double buffering / idle
GDI backbuffer implemented; no visible clear, cache per expose, dirty/refresh separated and the host callback RefreshDisplay. Remaining: visual verification of flicker, an FPS benchmark during a match, vsync, background audio and AST optimisations. It is not an atomic batching of the game's primitives.

Gate confirmed: Debug build 0 warnings/errors; Debug and isolated Release suite 704/704 passed. The resize harness waits for completion on the UI thread instead of reading the counter before the repaint. Isolated benchmark: 200 redundant EGA snapshots 97.41 ms / 224291152 bytes, 200 idle refreshes 6192 bytes / a single initial capture. It is not an FPS benchmark. The normal Release was blocked by the user process QBSharp.Cli 4588, not terminated; new build in ops/artifacts/double-buffer/bin/QBSharp.Cli/release.


## Video memory and VGA ports (2026-10-21)
Screen9 updated 1.2.0 -> 2.2.0 (PCCanvas no longer derives from Canvas; PCCanvasBindingTests updated accordingly). DEF SEG &HA000/&HB800 with PEEK/POKE mapped onto the PCCanvas VRAM in graphics modes; OUT/INP for the map mask, read map select and the VGA DAC (&H3C6-&H3C9). Gate: Debug/Release build 0 warnings/errors, suite 723/723 in both from PowerShell (12 new QBasicVideoPortTests tests). Environment note: launched from Git Bash the suite has 4 failures not related to the code (TERM=xterm-256color makes Spectre emit ANSI with redirected output, a real defect in Program.cs:32/40 still open; Ctrl+C not delivered to the ConPTY test). Source backup before the change: a tar file outside the repository. Open: Canvas.* receiving a PC now rejects the object; mouse INT 33h; corpus scan for CALL INTERRUPT. Global tool not updated.

## Screen9 2.3.1 (2026-10-21)
Reference moved from 2.3.0 to 2.3.1 (DrawGlyph without overflow, block ScrollUp, PCFont.Find safe outside Windows). The test total goes from 723 to 720 because the IBM font tests were moved into Screen9 with 2.3.0.

## Devices in the Screen9 engine (2026-10-22)
The pixel window, raster text, PLAY/speaker, keyboard and the sprite codec are no longer in QBSharp: Screen9's `PixelWindow`, `PCTextDisplay`, `PCTone`/`PCMusic`/`PCSpeaker`, `PCKeyboard` and `PCSprite` are used. `QBasicOptions.Audio` receives `Screen9.PCTone` (a breaking change for whoever embeds QBSharp). Still here: `QBasicSpriteCodec` (the LONG array adapter), `QBasicConsoleDevices` (the `QBS_QBASIC_AUDIO`/`QBS_RASTER_FONT` configuration) and the reading of `QBS_FONT_DIR`. 714/714 from PowerShell; the device tests are now in Screen9.Tests.

## Editor on the Screen9 engine (2026-10-22)
- Colours: the editor draws a Screen9 `Canvas` with `AnsiCanvasRenderer`, so the 16 CGA colours are exact (truecolor) and the same in every terminal; QBasic IDE style (grey code on blue, black bars on grey, white-on-black highlighting). Spectre is no longer used by the editor.
- F5 in QBasic mode: the program runs in the engine's `PixelWindow` (text, graphics, on-screen INPUT and keys from the window, like `qbs run` with `QBS_GRAPHICS=pixel`); the editor stays usable with the output. At the end of the program the window stays open until a key (in the window or in the editor), F6 or closing. `QBS_GRAPHICS=terminal` keeps the program view in the terminal (F8).
- Audio in the editor active by default (Windows, `PCSpeaker.Beep`); `QBS_QBASIC_AUDIO=mute` for silence with the durations. `qbs run` is unchanged (audio only with `QBS_QBASIC_AUDIO=1`).
- ConPTY tests: the waits read the screen rebuilt by a minimal VT emulator (`Screen`), not the raw stream, because ConPTY re-emits only the changed cells. Harness: `--terminal-graphics`. 715/715 from PowerShell.
- Unstable under load (pre-existing): `CurrentGorillaTests.CurrentFileParsesEntirelyAndReachesIntroWithVirtualDevices` can see 0 tones because background music is discarded when the program ends with an error; isolated it passes.

## Pauses in QBasic games: QBS_TIMER_WAIT_SCALE=auto (2026-10-22)
GORILLA.BAS measures the speed of the machine by counting the turns of an empty loop in half a second (`CalcDelay` → `MachSpeed`) and then waits `MachSpeed * t / SPEEDCONST` seconds (`Rest`), with `SPEEDCONST = 500` calibrated on a PC of the era. This interpreter counts up to ~500,000 turns: every pause lasted tens of times too long (one banana step: seconds instead of 20 ms; measured: CalcDelay + 10 × `Rest(.02)` = 13 s instead of ~0.7 s). It was the cause of the "terrible performance" of GORILLA.BAS, on the desktop as on the web; the Python port uses nominal times and did not suffer from it.
`QBasicRunPolicy.CalibratedTimerWaitScale` measures once per process the same `CalcDelay` (four rounds, keeping the fastest: the first ones run on a cold JIT, ~5 times slower) and returns `500 / turns`; `QBS_TIMER_WAIT_SCALE=auto` uses it in the CLI, and now the editor also reads `QBS_TIMER_WAIT_SCALE` (`EditorApplication.TimerWaitScale`, `EditorRunner(timerWaitScale:)`). With the calibration: 738 ms instead of ~700 expected. The default is unchanged (1, original execution) for the CLI; the web site always uses the calibration. As for `TimerWaitScale`, it scales only empty TIMER wait loops, not CalcDelay or anything else.
