# QBSharp architecture — phase 7

## Layout and dependencies

src/QBSharp.slnx; QBSharp.Core (Syntax/Diagnostics/Hosting/Runtime/Modules), **QBSharp.StandardLibrary** (Serialization/Files/Configuration/UI), QBSharp.Cli, QBSharp.Tests, samples/qbsharp/. ops/ pipeline/install/completion/verify/artifacts; root documents. net10.0/C#14/nullable/implicit usings/warnings as errors. CLI->Core+StandardLibrary, StandardLibrary->Core+Spectre.Console+YamlDotNet+Microsoft.Extensions.*, Tests->all. Core has no Spectre/YamlDotNet/EF/AI/Roslyn; it uses System.Text.Json for CLR materialisation. The external PySharp README and ARCHITECTURE were read in phase 7 through a .NET inspector: the AST pipeline, the separate library/tool and the opt-in adapters are consistent; a detailed alignment of code style has not yet been verified.

## AST/parser

ScriptSource -> Lexer -> tokens/spans -> ScriptParser -> AST -> GuardedParser -> ScriptSession. The lexer is invariant/UTF-16/cancellable; the parser is recursive/precedence-climbing/fail-fast. Records/ImmutableArray, ClassDeclaration/PropertyDeclaration/NamespaceDeclaration/ImportStatement/NewExpression/TypeSyntax; FunctionDeclaration flags IsSub/IsConstructor. Parser/AST nesting 128, iterative AST guard. A structured TypeSyntax for New/ParseType; declaration metadata keeps the canonical TypeName for initial API compatibility. Phase 5 allows keywords **only after a dot** for data and Ui.Select, without weakening declared names. No ad hoc keyword/AST node for each standard function.

## Runtime and classes

ScriptSession is partial: gate/execute/evaluate/invoke; TypesAndModules class/type/namespace/import; Materialization data mapping. Scope cells are case-insensitive/parent/qualification, with CLR Type or ScriptClass constraints. Class/namespace/module alias/Me/bound methods are readonly; Values arithmetic/conversions.

ScriptClass is AST+closure+full name, ScriptInstance scope+Me/properties/methods, ScriptContainer namespace/module with an IsModule flag. Interpreted classes are not synthetic CLR types: As class is a reference identity, List(Of Object) for instances rather than List(Of ScriptClass). New creates the defaults of all properties, then initialisers in order and Sub New, with property/expr spans kept; construction and Invoke constructor count separately in the call limit. Signature types are deferred to construction/call. Parameters shadow properties, Me qualifies; closure/return across blocks. A namespace is only Class/Namespace/reopenable, not a module alias; If/While have no scope and no hoisting.

## Types/reflection

The CLR registry has primitives + System aliases/List/Dictionary and opt-in options.ClrTypes, including generic definitions/host Type values; no arbitrary Type.GetType/autoload. MakeGenericType constraint/arity, generic arguments CLR-only. MemberAccess engine cache ConcurrentDictionary(Type, uppercase name)->public instance candidates, scoring exact 0/assignable 1/conversion-null 2 per call/unique minimum; constructors use the same binder. Public accessors/readonly are verified, no ByRef/open-generic/optional/paramarray/static/extension. TargetInvocationException is unwrapped/inner/span, cancellation is not wrapped; a reflected Task is an ordinary object, an async HostFunction is awaited. The cache keeps the Type for the engine lifetime, collectible/weak in future.

## Data contracts and materialisation

IScriptMemberProvider GetMember/SetMember dispatch in MemberAccess before reflection, used by the standard groups/DynamicRecord/Config. IScriptPropertyBag Properties is a shallow readonly snapshot: ScriptInstance includes only auto-properties, not Me/methods; IScriptOpaqueValue marks code/types/containers excluded from the serialiser. Neutral contracts in Core, projection in StandardLibrary, no CLR emission.

The HostCallContext.Materialize delegate captures the current scope/call span and uses ResolveDeclared. Script mapping builds a **zero-argument** class with initialisers/constructor, then sets known properties case-insensitively with type conversion/recursive mapping. Unknown ones are an error/missing default; constructors may have side effects, with no bypass/rollback. Limit 64/budget/cancel. CLR mapping normalises property bags/sequences/dictionaries and uses System.Text.Json; primitive numeric conversion follows Values. The host DeserializeClr codec is independent of the interpreter.

## Modules

FileModuleResolver: importer directory -> user -> standard, file name.bas before package name/`__init__.bas`, an explicit .bas is exact. Engine defaults cwd/$HOME/.qbs/lib/AppContext.BaseDirectory/stdlib, no .bas payload distributed. ImportingFile from the AST span, also for an exported function, virtual BaseDirectory. FullPath is not realpath/sandbox. An explicit/default alias is valid, not a keyword/literal, with no automatic global.

Session cache identity is ignore-case on Windows/ordinal elsewhere, with loading/loaded/failed + stack. The resolver is always called (the source is re-read), the cache covers evaluation, not I/O. A module has an isolated scope with parent hostScope, exports are live own cells, multiple aliases are shared; the caller's script globals are not visible, the standard bindings are. An alias is published only after success, idempotent for the same module, side effects are not rolled back. Failed is cached / cancel removes + retry / cycles 3002 / depth 3003 max 64, budget/token shared. No reload/export-selection. IAssemblyProvider is a phase 9 contract.

## Standard library

StandardLibraryHost creates the Json/Yaml/File/Directory/Path/Config/Ui bindings, explicit options and ITextUi; the CLI registers per session, embedding is opt-in. StandardNamespace is readonly with HostFunction validating arity/types/cancel before-after, `.Names` discovery. No implicit UI I/O in Core. DI AddQBSharpStandardLibrary options singleton/default UnconfiguredTextUi and host transient.

ObjectGraph projects primitives/ISO dates/property bags/CLR public properties (no indexer)/IDictionary String/IEnumerable; a reference-active set rejects cycles (a shared duplicate is allowed), and rejects non-finite/code/types/delegates. JsonCodec uses System.Text.Json, duplicates are rejected case-insensitively, reads Integer/Long/Decimal/Double and DynamicRecord/List. YamlCodec preflights events (tag/anchor/alias/multidoc/depth/node), then data-only RepresentationModel; quoted String, dates/yes-no String. YAML DeserializeClr goes through a JSON projection, not deserialisation of types from tags. Limits depth 64/nodes 10000/text 1048576, not a total allocation quota; CLR getters/constructors are host code.

FileServices base cwd captured/host options, read UTF-8 strict byte limit/BOM, write UTF-8 without BOM and binary copy through a temporary sibling + move overwrite/best-effort cleanup. Max 1 MiB/file / 10000 traversal entries, recursive listing per glob Match with Microsoft.Extensions.FileSystemGlobbing, skips reparse points, no truncation. Relative globs with no ../, absolute results; File/Directory/Path groups with no shell subprocess. RestrictToBaseDirectory containment/reparse is opt-in, not race-proof/a sandbox, and not applied to Core Import/reflection. Synchronous fs checks are cooperative before-after only, deleting the base is prohibited.

ConfigurationData looks for qbs.yaml or qbs.yml in the base, both is an error / absent is empty, a root mapping/null is empty, Source and DynamicRecord Data/Get dotted default. A readonly container / mutable data is an in-memory snapshot, no watcher/persistence/merge/env/engine override. CliApplication Report distinguishes **InvalidDataException** (not IOException) as exit 5 at config load; standard calls wrapped as runtime exit 4 with call span.

SpectreTextUi implements ITextUi with panel/message/input/selection/menu/multiselect/table/progress snapshots. Texts are literal/escaped, limits 1000 choices/1000 rows/100 columns. InputBox/selections require interactive, with no redirected stdin consumption; MessageBox is a readonly panel + OK without a prompt when non-interactive. Table/progress output is human-readable, not structured pipeline data. Window is not the full-screen editor, Progress is not a worker handle. The fake test adapter is deterministic/no network/no real terminal.

## Embedding, security and CLI

ScriptEngine reuses parser/options/cache/resolver, isolated sessions / shallow binding snapshots; execute/evaluate/Variables/set/register/dispose gate concurrency, dispose is repeatable. AddQBSharp singleton parser/engine and transient bindings, AddQBSharpContracts bindings only. The parser is complete before execute / no partial parse, no runtime rollback, original call/source spans. Max steps 1M / call 128 / import 64 / parser AST 128; tokens scanner/parser/type/AST/nodes/loop/host/Import/library. Synchronous CPU until an incomplete await, the UI host may Task.Run; a sync host is not forcibly abortable / shared host objects are synchronised. Not a sandbox nor a global memory quota.

Program: GenericHost DI/config/logger without lifecycle chatter, UTF-8 / BomTextReader only leading stdin BOM / Ctrl+C CTS / unregister in finally. CLI max steps option/env/default, standard binding + Print/Args/assertion. LiteralOutput Segment avoids wrapping of pipeline Print, stderr diagnostics; UI panels/tables are human-readable. Informational/new commands do not load config. The REPL persists/cancel/reset/vars/complete/quit with a sticky first exit; reset reloads config. Buffer 65536 / history 200, End block detector, no multi-line expression; tty end-of-line vs redirected no prompt. Completion prefix / files 1000 / PowerShell adapter, no member context.

Tool qbs/QBSharp.Cli 0.7.0-alpha.2 local pack. ops/install-tool.ps1 Release build/test/pack/install-update/verify; ops/verify-tool.proj exact version, smoke of old and new examples/run/test/redirected REPL, an isolated and cleaned scratch filesystem. No remote NuGet publication. An AzureDevOps pipeline is still to be configured.

## Verification and next gate

Previous parser/runtime/contract/reflection/class/module/CLI suites, SerializationTests/FileAndConfigurationTests/StandardUiTests/Phase5MemberTests/Phase5ProcessTests for the new surfaces. Real dotnet processes with a compiled DLL, a temp cwd and redirected UTF-8: config/invalid/keyword Ui.Select/nonblocking/phase5*.bas fixtures. The global tool is verified separately. Windows editor keyboard/mouse/save/run/exit are automated with ConPTY; real terminal UX / Unix and OS Ctrl+C remain manual smoke tests.

The phase 6 editor is implemented as described below; Spectre renderer, not an editor framework. Phase 7 Ralf is implemented as described below; next phase 8 dynamic SQL/EF proof of concept / templates; 9 assembly/NuGet policy. SPEC/OPENPOINTS document the limits, with no anticipation of future features.


## Editor and test host — phase 6

CLI/Editor: EditorDocument is a UTF-16 string / newline / selection / cursor / history 64 / dirty + savedText, bounded I/O through FileServices; EditorController is a state machine for menu/dialog/output/scroll/dirty confirmations/run/error navigation. Open is deferred and awaited in the event loop, no async void. EditorFrame is fixed cells IRenderable/Spectre, sanitises hostile text and lexer colouring, deliberate ASCII/Latin-1 display. EditorInputDecoder logical keys/SGR; EditorTerminal is native Win32 INPUT_RECORD + mode scope or Unix Console.ReadKey + SGR parsing.

EditorApplication factories IEditorInputSource/IEditorScreen for a deterministic lifecycle; alternate screen / hide cursor / mouse reporting and dispose in finally. The renderer polls throttled at 100 ms, input at 10 ms, resize; rendering minimum 40x12. Program forces Spectre ANSI/interactive **only** for edit on Windows with an effective console stdin/stdout, before the cached capability detection; EditorTerminal enables VT output and restores modes. Redirected is rejected, the ordinary CLI mode is unchanged.

EditorRunner is Task.Run on a new session + StandardLibraryHost/Args/Print, a separate CTS. A ConcurrentQueue of output bounded 1000/262144 chars and a dialog channel with a TCS RunContinuationsAsynchronously, cancelled by the token. RunUi has no direct console access from the worker: controller dialogs for input/message/select/multi, output for Window/Table/Progress (a Spectre table captured by a writer). F4 saves SourceText/SourceFile/span to avoid a wrong jump after an edit, imported-source opening is dirty-safe. Not a debugger/Roslyn/shell/script subprocess.

QBSharp.TerminalHarness is a net10/C#14 console project **for tests only**, referenced by Tests for output/runtimeconfig. On Windows ConPTY a child can inherit the runner's stdout pipe even with a pseudo-console; shell >CONOUT$ is write-only and GetConsoleMode fails. The harness opens CONIN$/CONOUT$ GENERIC_READ|WRITE and SetStdHandle before first using Console, AssemblyDependencyResolver and the compiled CLI entry point in the terminal process. No guard bypass in the CLI, no redirected handle accepted by qbs edit. Not included in the tool package; the QBS_EDITOR_CLI_PATH test override points to the installed package DLL, so the same workflow covers the release. The native Win32 input-mode 9001 escape test avoids the ESC/Alt timeout ambiguity; SGR mouse translated conhost->INPUT_RECORD.

EditorTests/EditorRegressionTests cover all the main behaviours and the editor.bas fixture; EditorTerminalProcessTests native keyboard/mouse/menu/save/run/exit. Native tests are Windows-only, the Unix decoder test does not certify a terminal. ops verify help/version/CLI/fixtures + ConPTY on the installed DLL. Multi-window, OS clipboard/find/debugger/Unicode cell widths/hardened Unix backend, advanced input dialog and crash recovery remain declared debt.

## RalfAI and external credentials — phase 7

QBSharp.AI is separate -> Core/StandardLibrary (binding/data), RalfAI.Providers 1.0.4 and Microsoft.Extensions.AI/IConfiguration. CLI DI AddQBSharpAI (IRalfClient a stateless singleton, service/host transient) and RalfHost.AddBindings at every run/repl/test/editor session. Core/stdlib have no AI dependencies. The real chat factory was inspected, not AgentEngine, and there is no automatic tool/function invocation. Provider clients are per request/disposed, an injected factory for adapter tests.

RalfConfiguration.Load reads QBS_AI_CONFIG_FILE only if opted in, absolute path / max 1 MiB / depth 32 / UTF-8-BOM, flatten AIProvider/OpenAI/Anthropic/AzureAIFoundry. Foreign config is ignored; selected provider/group and Foundry context/default/model alias, env override. An empty provider short-circuits to disabled without I/O. Config and secrets are not registered in the CLI's root IConfiguration or in the script's Config.Data, but are in memory for settings/request/provider authentication. Synchronous file read is bounded and errors are fully sanitised, no original inner/path/body. ToString of settings is redacted; a trusted .NET host can of course read ApiKey and must not expose it to scripts/logs.

RalfService checks prompt/format/settings, a linked CTS timeout + WaitAsync, distinguishes host cancel/timeout, output caps / strict JSON. The text/json/code system prompt is static, no history/tools; a deterministic fake IRalfClient. All raw SDK errors are sanitised before ScriptRuntimeException, no logging of requests/provider responses/raw exceptions. The RalfHost result is a DynamicRecord Text/Provider/Model/Data. We neither emit nor run the response as a script.

EditorAiWorkflow is Task.Run on the service, a snapshot of text/path, context only instruction + buffer, the QBSharp guide schema. A parser syntax gate, a fence normaliser, an EditorCodeProposal replacement diff and Apply with an unchanged text/path check + one Undo. Controller F7 disclosure (default cancel) / instruction / pending / cancel / preview / Apply-Discard, renderer vertical/horizontal scroll. Runner/AI are excluded when one is active; Save/Run separate, no writer agent/file tools. No multi-file patch nor model-chosen path; names/path are decided by the user through the editor.

RalfTests/RalfSafetyTests/RalfConfigurationTests/ralf-test.bas are offline tests: real factory construction, fake IChatClient request/disposal/no-tools, timeout/cancel/secrets/JSON, CLI embedding/DI, stale preview/undo/limits/no code execution, external schema/precedence/BOM/missing/invalid/metadata. ConPTY F7 consent + disabled path against the installed DLL. The opt-in cloud ops/smoke-ralf-live.cs captures output/errors and emits only the outcome; one small JSON+assertion call, it does not generate/save/run files. The ops verify LiveAiConfig property separates the offline default from the authorised smoke test.

The original local feed is not present, Providers trusted 1.0.4 from the cache (no invented API/stub). ops/seed-ralf-feed.cs validates the nuspec id/version and copies the supplied .nupkg/cache into artifacts; ops/nuget.config local mapping and nuget.org for other deps. The external PySharp README/ARCHITECTURE were read with a .NET inspector in phase 7: AST/library/tool/adapter separate and consistent, full code-style alignment remains to be verified. External credentials config not copied.

Limits: cloud/model behaviour/cost are not guaranteed, synchronous config I/O and a provider that ignores the token are not forcibly abortable, JSON/code syntax validation is not a policy/sandbox, no semantic validation/streaming/cost/history/multi-file diff. Language changes require updating SPEC and the AI guides. Phase 8 DB/templates and phase 9 dynamic assemblies/packages are still planned.

## Samples at the root and the Gorillas regression

The .NET source/projects stay in src/, operations in ops/, the .bas samples and assets are now in **samples/qbsharp/** next to src/. QBSharp.Tests includes ../../samples/**/*.bas with Link samples/qbsharp/... and copies lib/models transitively; process/embedding tests and ops/documents smoke tests updated. The move was a Directory.Move with SHA256 before/after, no re-encoding of the historical sample. PROMPTS preserves the requests/historical path.

GorillasCompatibilityTests keeps a negative compatibility proof: a whole-file lexer/parser before AST execution, diagnostics before the game/graphics/I-O. The historical sample is not a stdlib nor an evolution of the parser; no shim pretending to be SCREEN or DOS memory. A graphics port and possible retro compatibility are choices separate from the VB.NET scripting/Ops plan. Some Spectre reports break long path names: the process test normalises whitespace only to compare the span, parser/embedding tests verify the exact coordinates.

## PCCanvas dependency and wrapper

ConsoleLib 1.2.0 externally extended and packaged, the QBSharp.StandardLibrary NuGet reference updated. Canvas unsealed with a protected OnCellChanged called by all mutators, not the constructor. PCCanvas is byte-index pages/VRAM mirrors, immutable descriptors/palette, typed draw delegates: the base does clipping/raster, then the hook updates indices + VRAM. Palette/active-page refresh the cell cache; visual-page projection uses indexed state. PCImage immutable indexed capture differs from CanvasRegion. Modes/layouts/hardware limitations in SPEC.md.

PCCanvasBindings registers the PC StandardNamespace with typed HostFunction arity/value checks and source/cancellation behaviour, no parser changes. Real PCCanvas objects inherit Canvas and may be inspected via reflection. Render passes a new ToCellCanvas to the injected ICanvasRenderer; native Windows and text/editor surfaces still require a cell-resolution 16-colour approximation. Exact indices/RGB are independent of the preview. PC.Ascii is a debug fallback (hex 4-bit or '#' for high VGA indices), not exact graphics. No Spectre runtime dependency introduced.

External Screen9 tests plus PCCanvasBindingTests verify NuGet integration, wrapper/cancel/diag/editor and two real-process samples. EGA 256 KiB page model / CGA banks / VGA linear, no port/BIOS or legacy statement emulation. The Gorillas negative compatibility test remains valid, not silently converted. Dynamic .NET imports/DB/template phases unaffected.

## QBasic dialect and regression gate

QBSharp.QBasic is a separate net10 project -> Core contracts/SourceSpan/diagnostics. Lexer suffix/legacy numeric, BParser AST expression and statement sequence/control links for For/Do/If/Select/label/Gosub, QBasicEngine/Session implement standard embedding. No Roslyn compilation or code generation. The CLI --dialect run/test is opt-in and creates a different engine, the modern parser is unchanged.

BCell type constraint/readonly/shared and ArrayElementType metadata also for an unallocated array. REDIM reassigns the same cell for ByRef aliases, keeps the element type and rejects a change; a typed suffix alias resolves the cell. Bounded arrays / rank strides, TYPE record cell-map / value copy, inferred CONST and procedure temporary cells. Destination values/ref/index are evaluated once (null does not require an Eval fallback), host updates keep the constraints. BValues and CSNG reject non-finite values even before storage.

RunFrame keeps control position / Gosub return / For counters / Select temporary / error-handler origin. ON ERROR captures source/error, RESUME reset, the global handler procedure supports resume; a handler fault reports the original cause and span. Cancellation/budget cannot be swallowed. The DATA pool and positions are per program, the BFiles handle table is sync/bounded and TextReader/Writer injectable. Public reflection cached/simple, host args normalisation. The QBasic runtime now has a dedicated device adapter (see below); hardware services not foreseen remain explicit errors.

The initial 444 tests passed, new edge regressions revealed 9 defects and were fixed; the Debug suite of 461 was green in the history after the error-context tests. The 426 modern tests included remain green. Syntax validation does not certify all of QBasic: full prototype matching, signed hex/OEM/exact random, homonymous scalar/array namespaces, DATA label scopes and jumps between procedures are not complete. Global release is still a separate gate.

## QBasic device/raster adapter: source gate 528/528

QBSharp.QBasic -> ConsoleLib 1.2.0 + Core contracts, not Spectre. Devices.cs connects the AST Device to QBasicMemory, PCMusic, PCTextDisplay and QBasicSpriteCodec/PCCanvas. The options ReadKey/Delay/Audio/PresentText/PresentGraphics separate the model/session from the terminal. The CLI connects the Screen9 renderer only when interactive, a native non-blocking ReadKey and opt-in Windows audio; the editor keeps using the modern dialect (no QBasic selection introduced).

QBasicMemory is a session-local 1 MiB: real-mode aliases / 20-bit wrap, a virtual BIOS flag. Not hardware RAM/BIOS/VRAM. QBasicSpriteCodec LONG1D little-endian header and CGA packed / EGA row-planar / VGA linear, distinct from PCImage. Display contains the text Canvas, a 1-based cursor, the VIEW PRINT region and a 5x7 font rasterised onto the PCCanvas; scroll moves only the pixel/cell region. Clear modes are explicit: 0 whole / 1 graphics / 2 text region. A readable custom font, not the IBM ROM; no new pixel-window backend.

Print uses real columns after wrap and intra-PRINT tracking for TAB, file I/O stays separate. WIDTH/COLOR validate before commit; SCREEN resets background/cursor. Cancellation after a host expression and inside write/clear/scroll, without transactional rollback. Presented frames are live/mutable, the host callback is responsible for copies/threading/quotas.

QBasicTextRasterTests 32 + QBasicTextContinuationTests 2 + QBasicRasterProcessTests 1 and the fixture with 10 asserts; the full suite 528/528 Debug/Release, 0 skipped. The earlier ConPTY is included, it is not a QBasic-specific terminal proof / manual UX. GorillasGraphicsRuntimeTests proves the original routines in memory, not the flow of a match. Not released in the global tool 0.7.0-alpha.2; the match / faithful rendering / MB audio / package gates are separate.

## PollKeyAsync and terminal scope (gate 551/551)

InputPolling.cs separates the Total default budget from BetweenInputPolls for trusted interactive hosts. PollKeyAsync waits for the interval/token before ReadKey, checks cancel after the callback and resets only in the opt-in mode. No heuristic on AST loops or zeroing on TIMER. The Cli QBasicRunPolicy selects the mode only for an interactive run without an explicit budget; any --max-steps/QBS_MAX_STEPS, and test/batch/embedding defaults, stay Total. Real pacing with Task.Delay, an injectable fake for tests.

QBasicTerminalOutput in the CLI receives the Screen9 Canvas (a projected PCCanvas), renders 16-colour VT with a 33 ms cadence, keeps the last pending frame and Refresh on poll, alternate screen plus a scope of Windows modes. The report is emitted after Dispose, not on the game screen. No Spectre drawing at runtime, no change to the modern Core/editor. The presenter does not certify a pixel/Unix/full IBM OEM renderer. An injectable terminal factory for the unit lifecycle tests; ConPTY also verifies the original Gorillas intro->key->CreatePlayer and cancel 130, not only the controller.

Gate 551/551 Debug/Release and 0 warnings/errors. The editor test SavedTextMatches retries only Win32 sharing/lock errors 32/33 within the existing deadline, the exact content is always required. A complete match and a global release are still open. The Release launcher from the source is now without the total 10M budget, user processes not terminated.

## Multi-dialect EditorRunner and frame snapshot (gate 564/564)

CLI edit --dialect -> initial EditorApplication/Controller, an explicit menu choice, modern default. Controller F5 passes a Dialect snapshot to EditorRunner.Start; the runner chooses ScriptEngine or QBasicEngine, a separate session per run. QBasic callbacks have an INKEY$ ConcurrentQueue of 1024, a console reader that waits on a dialog TCS in the worker (cancellable), Screen9 models -> EditorProgramFrame a readonly copy and a latest-slot lock. No polling/raw console or terminal drawing from the worker; the main UI receives frames and renders the Spectre grid.

ProgramView/F8 separates program focus from editing: it sends 8-bit keys/scan codes, F6 priority, CtrlQ dirty-safe, menu/dialog over the frame, dedicated scroll/pan. Complete/error/cancel go back to the code keeping the frame for F8, LastResult source/diagnostic/dialect for F4. A dialect change invalidates results, forbidden with a worker/AI/preview; invalid ones are rejected before the terminal lifecycle. F7 AI code is blocked in QBasic because the modern guide/parser are not consistent; the separate Ralf script binding is unchanged.

Publish max 33 ms / final force; frames bounded 300x100, graphics 80x25 16-colour preview. RunWriter bounded/Flush/Dispose captures fragments without a newline. Optional audio callback; Windows application env opt-in, tests fake/no sound. The common interactive burst quota QBasicRunPolicy 10M, modern total 1M and explicit CLI/embedding limits unchanged; CalcDelay showed the insufficiency of a 1M burst in fast processes. Not a sandbox.

EditorQBasicTests/EditorDialectRegressionTests and a real ConPTY of the original Gorillas F5/intro/key/setup/F6/exit 0/no changes. Suite 564/564 Debug/Release, 0 skipped. Release --artifacts-path ops/artifacts/editor-dialect so as not to touch the binaries of an open editor; the launcher prefers the isolated one. No global tool update and no complete match verified yet.

## Performance: lazy projection, row index and segment grouping

EditorFrameMailbox is a single-producer script / consumer UI: the source Canvas/PCCanvas stays on the worker, dirty/latest slots, an 80x25 projection only at the 33 ms deadline, a readonly snapshot under lock, a final flush. No ToCellCanvas before the throttle. Counters for structural verification and Reset when the worker is stopped. EditorRefreshPolicy avoids redrawing the idle program view, preserves UI/input and a 100 ms code fallback.

EditorFrame does not prepare the source when covered by the program/AI preview; 16x16 cached palette styles, contiguous homogeneous segments are emitted together. EditorDocument caches reference-identity text, normalised lines and original offsets, Position binary search / MoveTo prefix index. The public Lines is a shallow array copy that preserves isolation; the cache is not exposed to callers, and is invalidated automatically with snapshot/undo/redo/open. Input polling reads the key before the delay for queued keys, zero delay only when available, cancellation/budget unchanged.

QBSharp.Benchmarks is a separate net10 console (not the tool): a script of 1000 PSet with a final check, FrameBuild/Build+Render 200, warmup 3 trials median / allocated bytes. Before/after results saved in ops/artifacts/perf-*.json; about 4.5x frame preparation, 80% fewer allocations, **not** 4.5x FPS. The total Spectre renderer is almost unchanged in the measured case. 15 deterministic performance tests, no clock/GC thresholds in CI, the full suite 579/579.

A fixture mutation was detected separately: the current gorillas.bas is an IBM 1991 fragment, not restored. The Deluxe 2.2 backup from the previous artifact verified by SHA256 and copied as gorillas-deluxe.bas (68891 characters); historical regressions anchored to the distinct name. Launcher default/user files preserved, an optional parameter for the complete one. Release isolated editor-perf-after without closing user processes. The global tool is still not updated.

## Modern consolidation — loops
AST ForStatement/DoStatement/ExitLoopStatement and dedicated partial parser/runtime. The loop stack is separate per invocation; internal control exceptions do not become diagnostics. Limits/cancellation stay active. Two separate interpreters, classes in the modern Core.

## Modern selection and iteration
Source 0.7.0-alpha.4: `ElseIf`, `Select Case` and `For Each` in the qbs dialect; classes preserved. Build/test/release gate to be reconfirmed.
- Multi-line ElseIf, Boolean conditions, evaluated only up to the first true branch; a single End If, no new scope compared with If.
- Select Case: the selector is evaluated once; Case value, comma list, inclusive range `a To b`, `Is <|<=|=|<>|>=|>` and a final Case Else. First match, no fall-through. Tests evaluated in order and short-circuit, CLR comparisons of the modern dialect (ordinal case-sensitive strings). No Exit Select.
- For Each counter [As type] In expression / Next [counter]: a synchronous CLR IEnumerable, including collections and host objects. Without As the counter must exist; with As it is local per iteration, with checked conversion. The collection is evaluated once, the enumerator is disposed on end/Exit/error/cancellation, Exit For is shared with the numeric For. No snapshot, changes to the collection follow CLR rules; no IAsyncEnumerable or pattern-only enumerables. Synchronous host calls are not pre-emptible.
- Dedicated ASTs and GuardedParser visits all the new children. The REPL recognises End Select; completion includes the keywords.
- Coverage ModernSelectionTests and samples/qbsharp/modern-selection.bas. Run `qbs test samples/qbsharp/modern-selection.bas` after updating the tool.

## Windows pixel presenter
The CLI offers an opt-in Win32 GDI adapter through QBS_GRAPHICS=pixel, without Spectre dependencies for drawing. PCCanvas stays the Screen9 framebuffer. Immutable RGB snapshots of the visible page, captured on the interpreter thread at most about 30 Hz; a dedicated native thread presents the latest snapshot and queues keyboard events. A 4:3 layout without clipping, COLORONCOLOR scaling. The editor still uses a cell preview. Diagnostic PNG limited to 1 Hz.

## QBasic TIMER waits
The parser marks only empty loops with pure arithmetic comparisons that include TIMER. Run keeps AST/Single evaluation and in BetweenInputPolls introduces a 1 ms host delay; the budget is renewed only with an advancing clock, a real 60 s deadline per wait. The graphics callback keeps the presenter updated during the wait. Total and code with a body are unchanged.

## Opt-in timing for classic games
TimerWaitScale scales the threshold only of pure empty elapsed-TIMER loops; the default 1 keeps the original execution. The clock and the general TIMER are not accelerated, nor are CalcDelay or Rest intercepted by name. The CLI reads QBS_TIMER_WAIT_SCALE with invariant culture and validation, the editor is not configured yet. (The web host additionally uses `QBasicRunPolicy.CalibratedTimerWaitScale`, measured once at start-up from a CalcDelay-shaped script; see docs/web.)

## Presenter double buffering
PixelWindowBackBuffer owns the DC/bitmap on the UI thread: it composes palette/frame and black bands off-screen, presents via BitBlt, recreates on resize and frees on close. An expose without a new snapshot reuses the already composed backbuffer. PixelWindow distinguishes Present (dirty) from Refresh (flush only if dirty), avoiding RGB snapshots while idle. The runtime's RefreshDisplay keeps flushing during timers without simulating new graphics instructions. The limit of about 30 Hz remains; no vsync guarantee nor atomic synchronisation of several BASIC primitives. `PixelWindow` can scale by whole multiples only (`IntegerScale`) and is DPI aware; see docs/engine.
