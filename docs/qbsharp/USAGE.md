# QBSharp usage — phase 7

## Starting and the tool

```powershell
qbs --version
qbs --help
qbs help run
qbs run samples/qbsharp/hello.bas
qbs run samples/qbsharp/phase4.bas
qbs run samples/qbsharp/phase5.bas
qbs test samples/qbsharp/phase4-test.bas
qbs repl
qbs new main.bas
```

The QBSharp.Cli tool **0.7.0-alpha.4**, command qbs, registered per user:

```powershell
dotnet build src/QBSharp.slnx -c Release
dotnet test src/QBSharp.slnx -c Release
dotnet pack src/QBSharp.Cli -c Release -o ops/artifacts
dotnet tool update --global QBSharp.Cli --version 0.7.0-alpha.4 --add-source ./ops/artifacts
qbs --version
```

For a first installation use install instead of update. PATH $HOME/.dotnet/tools, a local package not on nuget.org. Smoke tests after installation: `qbs test samples/qbsharp/qbasic-core.bas --dialect qbasic`, `qbs test samples/qbsharp/modern-selection.bas`. Uninstall: dotnet tool uninstall --global QBSharp.Cli.

## Classes, namespaces and CLR

```vb
Namespace Ops
    Class Job
        Property Name As String = "deploy"
        Property Count As Integer
        Sub New(count As Integer)
            Me.Count = count
        End Sub
        Function Description() As String
            Return Name & ":" & Count
        End Function
    End Class
End Namespace
Dim jobValue As Ops.Job = New Ops.Job(3)
Print(jobValue.Description())
Dim values As List(Of Integer) = New List(Of Integer)()
values.Add(42)
Dim map = New Dictionary(Of String, List(Of Integer))()
map.Add("numbers", values)
Print(map.get_Item("numbers").get_Item(0))
Dim today As Date = New Date(2026, 1, 2)
```

Everything is public, Public is optional; single-line auto-property, one constructor, no inheritance/overload/Private/Shared. Default properties come before the initialiser, then the constructor. Me is readonly; the parameter Count shadows the property, use Me.Count. Namespaces are nested/qualified/reopenable and contain only Class/Namespace. No Class/Namespace inside Function/Sub. As class checks the descriptor identity; without As the variable is dynamic. If/While have no scope, a repeated Dim in a loop fails.

A script class is not an emitted CLR Type, use List(Of Object) for script instances (no List(Of Job)). Other CLR types are registered by the host, no assembly download. Indexers through get_Item/set_Item, no static/generic method syntax. Conversions are checked, no implicit String->Integer; the overload binder is exact simple arity.

## Runtime modules

```vb
Import "lib/models" As Models
Dim counter = New Models.Ops.Counter(20)
counter.Add(1)
Print(counter.Twice())
```

Examples samples/qbsharp/phase4.bas + lib/models.bas. The Import expression is a dynamic String, also Args.GetValue(0), executed only when reached. It is not a textual include / no names poured into the global scope; use alias.Member. As is optional if the identifier basename is not a keyword. Package tools/`__init__.bas` from Import "tools".

Search: the directory of the file that contains the Import -> $HOME/.qbs/lib -> AppContext.BaseDirectory/stdlib; name.bas then name/`__init__.bas`. REPL/virtual stdin base cwd. Imported functions use the file of the definition. This differs from the standard filesystem, which uses cwd. YAML does not change the import roots. The host overrides options/resolver.

A module scope is isolated from the caller, and receives explicit host bindings (CLI Print/Args and the standard library). It exports its own names, live cells. Different aliases share the same state/cache per session, the initialiser runs once; the resolver still re-reads the identity. A failed one is cached until a new session, a cancelled loading is retried; no reload. Cycles/depth diagnostics, readonly aliases, namespace/module collisions rejected; side effects without rollback, no sandbox/realpath symlink.

## Dynamic and typed JSON/YAML

```vb
Dim data = Json.Deserialize("{""Name"":""deploy"",""Count"":42}")
data.Count = 43
Print(data.Name)
Print(data.GetValue("Count"))
data.SetValue("a-b", "value")
Print(data.GetValue("a-b"))
Print(Yaml.Serialize(data))
Dim items = Json.Deserialize("[1,2,3]")
Print(items.get_Item(0))
```

DynamicRecord: case-insensitive/mutable names, a missing key is an error, fallback helpers Count/Keys/GetValue/SetValue are shadowed by data with the same name. Sequences are List(Of Object). Keywords are allowed after a dot (`data.Select`, `data.End`), but `Dim Select` is forbidden. CLR Dictionary get_Item/set_Item.

```vb
Class Job
    Property Name As String = "default"
    Property Count As Integer
    Function Description() As String
        Return Name & ":" & Count
    End Function
End Class
Dim jobValue As Job = Json.Deserialize("{""Name"":""deploy"",""Count"":42}", "Job")
Dim restored As Job = Yaml.Deserialize(Yaml.Serialize(jobValue), "Job")
Print(restored.Description())
```

The type is passed as a qualified/generic String resolved in scope. Script materialisation runs the initialiser and the zero-argument constructor, then sets known properties; missing is the default, unknown is an error. Do not use classes with mandatory-argument constructors here (the Ops.Job example above is not deserialisable this way). Methods/Me/closures are not serialised. CLR Deserialize uses System.Text.Json; from C# JsonCodec/YamlCodec.DeserializeClr(text, typeof(MyDto)).

Single-document data-only YAML: no explicit anchor/alias/tag, not even !!str, case-insensitive duplicates, complex keys, multi-document/non-finite values. ISO dates as text, yes/no as text; quoted scalars are String. Dynamic numbers can change CLR type after a round trip. Cycles are rejected, shared objects duplicated. Default depth 64 / nodes 10000 / text 1Mi characters, host override; not a streaming/memory quota.

## Files, folders and globs

```vb
Directory.Create("output")
File.WriteText("output/readme.txt", "hello")
File.WriteJson("output/job.json", Json.Deserialize("{""Count"":42}"))
File.WriteYaml("output/job.yaml", File.ReadJson("output/job.json"))
Print(File.ReadYaml("output/job.yaml").Count)
Print(Directory.Glob("**/*.json", "output").Length)
Print(Path.FullPath(Path.Combine("output", "readme.txt")))
```

The service base is cwd, not the directory of the .bas file. Default 1 MiB/file / 10000 entries, listing/glob skip reparse points; parents must exist, writes/Copy overwrite through a temporary + move / best-effort cleanup. Binary Copy; read UTF-8 strict/BOM, write UTF-8 without BOM. Directory.Delete(path [, recursive]) defaults to False, deleting the base is forbidden. Directory.Files/Directories are non-recursive, absolute String[]; a relative glob is file-only, no ../ or absolute pattern. File.Delete/Exists, Directory.Exists, Path.GetFileName/GetDirectoryName/GetExtension.

The filesystem test script creates/deletes only a fresh path:

```powershell
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('qbs-demo-' + [guid]::NewGuid())
qbs test samples/qbsharp/phase5-test.bas -- $scratch
```

RestrictToBaseDirectory is host opt-in containment/reparse, not a sandbox/race-proof; it does not cover Import/reflection. The CLI is not restricted. Do not run untrusted scripts.

## Automatic YAML config

In the **current folder**, choose qbs.yaml or qbs.yml:

```yaml
ops:
  name: deploy
  count: 42
```

```vb
Print(Config.Data.ops.name)
Print(Config.Get("ops.count"))
Print(Config.Get("missing", "fallback"))
Print(Config.Source)
```

Absent/empty/null -> an empty mapping, Source null/file name respectively. A root mapping is required, both files present is an error. The container is readonly, Data is a mutable in-memory snapshot, no saving/reload watch/env merge. `:reset` reloads; help/version/complete/new do not load config. Config is script data, it does not change max steps/import/credentials. Malformed/ambiguous gives exit 5 before execute; LoadConfiguration=false in the host disables it.

## Spectre UI

```vb
Ui.Window("Ops", "Deploy ready")
Dim response = Ui.MessageBox("Continue?", "QBSharp")
Dim name = Ui.InputBox("Name", "deploy")
Dim choices = Json.Deserialize("[""dev"",""prod""]")
Dim target = Ui.Select("Environment", choices)
Dim selected = Ui.MultiSelect("Environments", choices)
Ui.Table(Json.Deserialize("[{""Name"":""deploy"",""Count"":42}]"))
Ui.Progress(100, "Done")
```

InputBox/Select/Menu/MultiSelect require an interactive terminal, otherwise a runtime exit 4 without reading stdin. MessageBox is a panel/Enter only when interactive, returns "OK"; Window/Table/Progress work redirected. Empty input -> default. Menu is an alias of Select, choices 1..1000 unique Strings, MultiSelect allows an empty list. Table of scalars or homogeneous mappings, union of columns, max 1000 rows / 100 columns. Literal markup texts. Window is a panel, not the full-screen editor; Progress is a percentage snapshot, not a persistent task.

## CLI, stdin, exit

```powershell
qbs run "space name.bas" -- "first argument" --literal
'Print(6 * 7)' | qbs run -
@('Dim x = 2', 'x * 3', ':quit') | qbs repl
$env:QBS_MAX_STEPS = '100000'
qbs run script.bas --max-steps 200000
Remove-Item Env:QBS_MAX_STEPS
```

Variadic Print with spaces/newline, invariant/Nothing empty, literal without reflow. Args is a String[] after --. UTF-8 strict / leading BOM; for a file/path with a hyphen use ./path.bas. New main.bas/.bas/CreateNew/no-overwrite/existing parent. edit is available (see the Editor section), non-interactive exit 2. The max steps option > QBS_MAX_STEPS IConfiguration > 1M, AST nodes not lines. Ctrl+C cancels and ends with 130, it does not force a sync host. Stdout is results/human UI, stderr errors, no structured JSON CLI output yet.

| Exit | Meaning |
|---|---|
| 0 | Success/help |
| 1 | Assertion failure/no assertions |
| 2 | Wrong CLI/options / edit non-interactive |
| 3 | Lexer/parser/AST, also in a module |
| 4 | Runtime/host/Import/limits/standard calls/assertion misuse |
| 5 | Main I/O/encoding/permissions/new existing/invalid or ambiguous YAML config |
| 6 | Unexpected internal |
| 130 | Cancellation/Ctrl+C |

## REPL, completion and test scripts

Class/Namespace/Sub/Function/If/While/Public are multi-line with End, not multi-line parentheses. Expression echo non-null; x=3 assign / (x=3) compare. :help/:vars/:reset/:cancel/:complete prefix/:quit; a blank line forces parse/EOF pending diagnostic, cancel discards the buffer / quit discards pending. The first error is sticky also across reset. Buffer 65536 / history 200. Tty: Tab first-token completion / Up-Down history / Backspace end-of-line / Ctrl+D empty EOF; wrap/wide glyph/navigation in future. Redirected without a prompt.

```powershell
qbs complete --m
qbs complete ru
. ./ops/register-completion.ps1
```

PowerShell session/explicit profile, no automatic profile modification; prefix of commands/options/.bas/directories max 1000 entries, REPL keywords/vars. Not member/context-aware / Bash-Zsh.

qbs test is a new session with Assert(Boolean[,message]) / AssertEqual(expected,actual[,message]); fail-fast / at least one assertion / CLR Equals: Integer 1 differs from Double 1.0, use Assert(a=b) for a QBS numeric comparison. No discovery/tolerance/deep compare. Only test has the assertion binding; examples assertions.bas/phase4-test.bas/phase5-test.bas. The C# suite is separate: `dotnet test`.

## Embedding

ProjectReference Core and, for the standard library, StandardLibrary; net10.0:

```csharp
using QBSharp.Core.Hosting;
using QBSharp.StandardLibrary;
using QBSharp.StandardLibrary.Files;
var library = new StandardLibraryHost(new()
{
    Files = new FileSystemOptions { BaseDirectory = Path.GetFullPath("workspace") },
    LoadConfiguration = false
});
var engine = new ScriptEngine(options: new()
{
    BaseDirectory = Path.GetFullPath("scripts"),
    UserLibraryDirectory = Path.GetFullPath("libraries"),
    StandardLibraryDirectory = null,
    MaximumImportDepth = 32
});
await using var session = engine.CreateSession(await library.CreateBindingsAsync());
session.SetVariable("HostValue", 42);
var data = await session.EvaluateAsync(new ScriptSource("Json.Deserialize(\"{\"\"Count\"\":42}\")"));
```

The directories of the example must exist. The host UI defaults to UnconfiguredTextUi; provide a custom ITextUi or `new SpectreTextUi(AnsiConsole.Console, interactive: true)` only if the terminal is really interactive. DI AddQBSharp and AddQBSharpStandardLibrary, host options/adapters can be registered. Print is a host RegisterFunction if required, not automatic.

ClrTypes alias->Type opt-in (also generic definitions), a custom moduleResolver IModuleResolver, parser.ParseType. Execute/Evaluate token, runtime Diagnostic/inner, cancellation OperationCanceledException. The HostCallContext.Materialize delegate is scope-aware for typed data. IScriptPropertyBag.Properties is a snapshot of script properties only; Values is a shallow readonly snapshot. No concurrency/read/mutate/dispose during execute, cancel + await then dispose. Host shared mutable objects are synchronised by the caller, synchronous CPU until an incomplete await (Task.Run if necessary), not a sandbox/rollback/general memory quota.


## QuickBASIC editor

```powershell
qbs edit
qbs edit samples/qbsharp/hello.bas
qbs edit samples/qbsharp/editor.bas
```

Use an interactive ANSI terminal of at least 40x12 (80x25 recommended). Windows supports native input/mouse; Unix VT requires verification on the chosen terminal. Do not use pipelines/redirection with edit. A non-existent .bas path creates only a buffer, Save creates the file. For the editor.bas demo press F5, fill in the dialogs and confirm; no AI or network.

| Keys | Action |
|---|---|
| F1 / F10 / Alt F,E,R,H | Help / menu / specific menu |
| F2 / Ctrl S | Save, Save As if unnamed |
| F3 / Ctrl O / Ctrl N | Open (path) / New |
| F4 | Jump to the file/line/column of the last error |
| F5 / F6 | Run a buffer snapshot / cooperative Stop |
| Ctrl Q | Exit with Save/Discard/Cancel |
| Ctrl Z,Y / A / C,X,V | Undo/redo / select all / internal clipboard |
| Arrows, Home/End, PgUp/PgDown | Movement, Shift extends the selection |
| Ctrl Home/End | Start/end of document |
| Esc | Closes menu/dialog; stops the run; toggles output focus |

Mouse: menu, dialog/button choice, click cursor / Shift click selection, wheel code/output; clicking the output navigates to an error if there is one. MultiSelect Space toggles, Enter confirms, Esc cancels the run. Ctrl+C copies in the editor, it does not close the program. Path/input dialogs are end-of-line (Backspace to edit the default); OS clipboard / find/replace / bracketed paste are absent.

F5 does not save the file and every run uses an isolated session. Import reads modules from disk relative to the document name; standard file/config cwd. Args is empty, max steps is a fixed 1M for the editor, not a flag/env. A run error stays in the output, the editor continues; F4 requires an unchanged buffer or a rerun. Opening an imported source protects unsaved changes. Save compares the text on disk before overwriting, but it is not locking against concurrent changes.

Limits: document 1Mi UTF-16 / I/O 4MiB, undo 64 snapshots, output 1000 lines / 262144 characters / run. Grid 40x12..300x100; below the minimum it is suspended until resize. Text beyond Latin-1 shows '?' but the file is preserved, a tab is displayed as 1 space / new tab 4 spaces; CJK/emoji/wrap/drag/multiple tabs are not complete. Esc in script dialogs cancels, Window/Table/Progress go into the output. Sync hosts are not abortable: Stop/Exit may wait for the worker.

`dotnet test src/QBSharp.slnx` includes the editor fixture and Windows ConPTY with mouse/save/run/exit; the native test is not run on other OSes. `dotnet msbuild ops/verify-tool.proj`, after a Release build/test and install, runs ConPTY against the installed DLL (Windows, standard dotnet store layout, InstalledCli override if needed). A manual UX smoke test of your own terminal/font/resize remains useful. Ralf is available with F7, consent and preview (see below).

## RalfAI: external configuration and scripts

To use the credentials of the authorised RalfAI file **without copying them**:

```powershell
$env:QBS_AI_CONFIG_FILE = 'C:\path\to\RalfAI\config.json'
qbs run samples/qbsharp/ralf.bas
qbs repl
qbs edit samples/qbsharp/hello.bas
```

It is not autodiscovery: AI is disabled by default until an explicit config is provided. The variable is only a path, not a secret. In the editor press F7, read the consent to send the buffer, give an instruction, review the proposal, Enter applies / Esc discards. F2 saves and F5 runs **only** on request. To generate a new file: Ctrl N -> F7 -> Apply -> Save As. Do not send buffers with secrets.

```vb
Dim answer = Ralf("Briefly explain blue-green deployment")
Print(answer.Text)
Print(answer.Provider & ":" & answer.Model)
Dim result = Ralf("Return JSON with status = ready", "json")
Print(result.Data.status)
```

Text by default, strict json; result fields Text/Provider/Model/Data. No call without parentheses, no standalone qbs ralf command, no automatic tools. ralf-test.bas is a fixture for an offline fake, do not assume a deterministic cloud response.

Providers from the file AIProvider and OpenAI/Anthropic/AzureAIFoundry with ApiKey/ModelId; Foundry also Endpoint/Api/context-model aliases. Env override QBS_AI_PROVIDER/MODEL/API_KEY/ENDPOINT/FOUNDRY_API/CONTEXT_KEY. Do not put keys in .bas/qbs.yaml/git/log: use a protected external file or the host's secret store. The file is absolute/UTF-8/max 1 MiB; re-read at each call, not exposed as Config.Data. Ralf's JSON config may include prices/foreign flags, ignored.

```powershell
$env:QBS_AI_TIMEOUT_SECONDS = '60'
$env:QBS_AI_MAX_OUTPUT_TOKENS = '2048'
# Disable also prevents reading QBS_AI_CONFIG_FILE:
$env:QBS_AI_PROVIDER = ''
# Re-enable file selection:
Remove-Item Env:QBS_AI_PROVIDER
```

Present QBS_AI_* take precedence (even empty), an empty provider is disabled. OpenAI/Anthropic standard endpoints; Foundry HTTPS without query/userinfo, chat or responses API. A model is mandatory; tokens 64..8192, timeout 1..300 s, prompt 8192 / output 65536 chars. Host cancel 130; script errors QBS2001 / exit 4 with the call site, without stack/raw provider details. There is no cost quota/caching/history/streaming; calls may be billed by the provider.

Embedding: reference QBSharp.AI and RalfHost.FromConfiguration(config).AddBindings(bindings), or DI AddQBSharpAI / a custom fake IRalfClient. Core/StandardLibrary do not load AI by themselves. Do not pass settings/key objects into scripts.

### Restoring the local Ralf package and the smoke test

The original local feed is absent in this environment. RalfAI.Providers 1.0.4 trusted is already in the cache; on a different machine provide the .nupkg from your own feed:

```powershell
dotnet run --file ops/seed-ralf-feed.cs
# or: dotnet run --file ops/seed-ralf-feed.cs -- 'path\RalfAI.Providers.1.0.4.nupkg'
dotnet restore src/QBSharp.slnx --configfile ops/nuget.config
dotnet build src/QBSharp.slnx -c Release
dotnet test src/QBSharp.slnx -c Release
```

The normal tests are offline/fake; they do not use the external credentials. The cloud smoke test is **opt-in, may cost** (it does not print/save the key or the response):

```powershell
dotnet run --file ops/smoke-ralf-live.cs -- 'C:\path\to\RalfAI\config.json'
# After install: includes offline tool smoke + a small JSON call against installed DLL:
dotnet msbuild ops/verify-tool.proj -p:LiveAiConfig='C:\path\to\RalfAI\config.json'
```

Without LiveAiConfig the tool smoke test is offline (ConPTY disables AI). The parser check and consent do not make the generated code safe: review before Apply/Save/Run. Preview replacement diff scrollable (arrows/PgUp/PgDn/wheel, Left/Right/Home), single document, not a minimal/multi-file patch.

## Samples path and Gorillas verification

```powershell
qbs run samples/qbsharp/hello.bas
qbs edit samples/qbsharp/gorillas.bas
# Verification on the current source (expected exit 3, not a runnable game):
dotnet run --project src/QBSharp.Cli -- run samples/qbsharp/gorillas.bas --max-steps 10000
```

samples/qbsharp/ is at the root next to src/, no longer src/examples/. The original game is preserved: QBasic Gorillas Deluxe 2.2, not QBSharp. The first error (10,24) QBS1003 for '#'; other unsupported QBasic/DOS/graphics constructs remain blocking even after removing that suffix. The editor can read the text, but F5 does not start the game. No Gorillas.ini/.lge written: parsing fails before execute. Do not use the historical sample as a certificate of QBasic compatibility.

## PC wrapper quick start

```powershell
qbs run samples/qbsharp/pc-canvas.bas
qbs test samples/qbsharp/pc-canvas-test.bas
qbs edit samples/qbsharp/pc-canvas.bas
qbs run samples/qbsharp/pc-canvas.bas -- render
```

The default sample prints an ASCII index-debug skyline, no native terminal needed. The render option uses an 80x25 cell / 16-colour preview; not a separate pixel graphics window. PC.Render defaults to the renderer viewport; PC.Preview returns a cell Canvas usable by Screen.Render. Prefer PC.Point/Snapshot for exact indices and PC.Rgb for the actual palette RGB.

```vb
Dim cga = PC.Create(1)
PC.CgaPalette(cga, 1, True, 0)
PC.WriteByte(cga, 0, 27)
Print(PC.Point(cga, 3, 0))
Dim ega = PC.Create(9)
PC.Active(ega, 1)
PC.PSet(ega, 0, 0, 6)
PC.Visual(ega, 1)
Dim vga = PC.Create(13)
PC.Dac(vga, 255, 63, 0, 21)
Print(PC.Rgb(vga, 255))
```

Immutable modes; bounds/depth validation errors have source positions. Sprite Get/Put takes a PCImage, not a Screen CanvasRegion or QBasic Long-array blobs. Operation strings copy/pset/preset/xor/and/or, optional transparent index; XOR twice restores the pixels. Palette setters differ: CGA mode 1 fixed families / direct background, EGA code 0..63, VGA Dac components 0..63. Full API and framebuffer layouts in SPEC.md.

The Screen9 project extension lives in the Screen9 repository (formerly ConsoleLib); package 1.2.0 persisted in the authorised local NuGet feed and copied to the ops local feed. QBSharp references NuGet, not the external source. No BIOS/register/CPU/DOS/audio emulation, not automatic Gorillas compatibility. The existing game file is preserved.

### Global tool updated
The retry installed qbs 0.7.0-alpha.2 globally. Use directly qbs run samples/qbsharp/pc-canvas.bas, qbs test samples/qbsharp/pc-canvas-test.bas, qbs edit samples/qbsharp/pc-canvas.bas. Ralf stays opt-in through QBS_AI_CONFIG_FILE/env. Global smoke tests and 2 ConPTY green; no AI call in the retry.


## Testing the QBasic dialect from the source

```powershell
dotnet run --project src/QBSharp.Cli -- test samples/qbsharp/qbasic-core.bas --dialect qbasic
# Diagnostic check, not the start of a match:
dotnet run --project src/QBSharp.Cli -- run samples/qbsharp/gorillas.bas --dialect qbasic --max-steps 10000
```

--dialect qbs is the default; qbasic only for run/test (not REPL/edit), combinable with max steps / args after --. qbs test adapted to numeric booleans and classic numeric comparison, without changing the modern assertion mode. The installed global tool 0.7.0-alpha.2 does not yet contain this source: no pack/install in this verification step.

Embedding net10: reference QBSharp.QBasic, `new QBasicEngine(new QBasicOptions { Output = writer, Input = reader, BaseDirectory = workspace, AllowFileIO = false }).CreateSession(bindings)`. Execute/Evaluate with CancellationToken, dispose handle; scope/types/arrays per session. INTEGER 16 / LONG 32 / SINGLE / DOUBLE, ByRef default and temporary CONST; REDIM keeps types / no PRESERVE. String/CSV UTF-8 and System.Random are not DOS/OEM/bit-exact QB. SPEC lists the subset and limits.

The suite now actually runs: QBasicTests/Regression/ErrorContext. Errors in handlers include the initial cause; do not use a no-op BEEP to hide DEF SEG/missing services. No AI/network in the tests.

## Updated gate: raster text and QBasic services

Debug/Release builds and the full suite 528/528 green; no pack/install. The previous DEF SEG/BEEP failure is overcome by the new services, it is no longer the expected diagnostic of the source. Gorillas is not yet a verified match: the tests reach the calibration / introduction wait with a controlled clock/budget and exercise the original graphics routines.

```powershell
dotnet build src/QBSharp.slnx -c Release
dotnet test src/QBSharp.slnx -c Release
dotnet run --project src/QBSharp.Cli -c Release -- test samples/qbsharp/qbasic-raster-test.bas --dialect qbasic
```

The fixture has 10 raster/VIEW PRINT/CLS assertions and also works with redirected stdin/stdout. No audio, real memory, files or AI. For an interactive graphics run the Screen9 renderer remains a cell preview; PRINT in SCREEN 0 uses a Canvas, in a graphics SCREEN it writes pixels with an approximate compact font.

Embedding: QBasicOptions.PresentText for the text Canvas, PresentGraphics for the PCCanvas, ReadKey for non-blocking INKEY$, Delay for waits/token, Audio for PCTone events. Missing audio produces an explicit error if a note is emitted. The CLI is Windows and interactive only: `$env:QBS_QBASIC_AUDIO='1'` enables Console.Beep; the adapter plays in sequence, not a faithful asynchronous MB. Font/CP437 and BIOS are virtualised subsets, not a complete DOS emulation. SPEC §17 specifies CLS and VIEW PRINT.

## Interactive Gorillas: do not consume the budget reading the instructions

```powershell
# From the repository root, after a Release build:
dotnet run --file ops/launch-gorillas.cs
# Or in your own terminal:
dotnet run --project src/QBSharp.Cli -c Release -- run samples/qbsharp/gorillas.bas --dialect qbasic
```

For the second command, QBS_MAX_STEPS must be absent if you want the interactive policy; remove it only if desired. The launcher removes it in the environment of the new window without changing the calling session. **Do not use --max-steps 10000000 to get around the waits**: an explicit limit keeps the total meaning and can still end a long wait.

Only an interactive QBasic run without option/env uses BetweenInputPolls (steps between polls, 10 ms pacing). The embedding/batch/test Total default is unchanged. A trusted program can wait indefinitely; for a global limit use --max-steps or a host CancellationToken/timeout. Loops that poll can last indefinitely under the interactive policy, so do not use it with untrusted code.

Alternate-screen display, restoration before errors/exit/Ctrl+C, a pending frame presented even during a wait. A Screen9 16-colour canvas, not a pixel graphics window. The intro and arrival at the new-player prompt were verified in the Windows ConPTY process, not a whole match. Suite **551 tests passed** in Debug/Release; global tool not updated.

## QBasic editor: using F5 on the Gorillas buffer

```powershell
dotnet run --project src/QBSharp.Cli -- edit samples/qbsharp/gorillas.bas --dialect qbasic
# A new console with the isolated Release already built:
dotnet run --file ops/launch-gorillas-editor.cs
```

Also choose Run -> Language / dialect. No autodetection: the default is QBS, the title shows the selection. F5 uses a buffer snapshot / new session; F8 alternates code and program view. In the program view text/arrows/Tab/Enter/Esc are sent to the game, they do not edit the file. F6 Stop always, Ctrl+Q protects dirty; Esc in the program view is not Stop. F8 goes back to editing. Wheel/Ctrl+PgUp/PgDn scroll, Ctrl+Left/Right pan; use 80x30 or more for the 80x25 frame and bars. INPUT/LINE INPUT single-line dialog; errors in the output panel and F4 navigates the source.

The code/style/raster are not ROM-faithful, Unicode/OEM display limited, a cell / 16-colour graphics preview. Windows audio only if QBS_QBASIC_AUDIO=1 (the launcher enables it in the child); not a complete MB scheduler. F7 AI code in QBasic is disabled before sending: switch explicitly to QBS if necessary. No implicit save or modification of the original sample.

Interactive QBasic editor/CLI default: a 10M quota **between polls** + 10 ms pacing, not a total quota. The modern editor 1M total. Explicit --max-steps/QBS_MAX_STEPS in CLI run and batch/test/embedding keep the total; the editor does not accept those flags. Use only trusted scripts with a between-polls budget.

Gate 564/564 Debug/Release. Isolated Release build:
```powershell
dotnet build src/QBSharp.slnx -c Release --artifacts-path ops/artifacts/editor-dialect
dotnet test src/QBSharp.slnx -c Release --artifacts-path ops/artifacts/editor-dialect
```
The old editor / global 0.7.0-alpha.2 are neither updated nor closed: open a new window from the launcher or the source. F5/input/F6/exit verified on the original, not a whole match.

## A lighter editor and the preserved complete sample

```powershell
dotnet run --file ops/launch-gorillas-editor.cs -- samples/qbsharp/gorillas-deluxe.bas
# or from the source:
dotnet run --project src/QBSharp.Cli -- edit samples/qbsharp/gorillas-deluxe.bas --dialect qbasic
```

The launcher now accepts an optional .bas and prefers the isolated Release editor-perf-after; the default samples/qbsharp/gorillas.bas is unchanged. That file was found to be an IBM 1991 fragment ending in DI: it is not the complete Deluxe 2.2 of the earlier tests. The earlier complete copy is preserved in gorillas-deluxe.bas, without overwriting the working file.

There is no need to arbitrarily reduce the script pauses to optimise rendering: projection/copy only at the due frame, a program view without the lexer of the hidden code, cached lines/styles and grouped segments. An already available INKEY$ returns at once, empty polling stays paced/cancellable; F6 and limits unchanged. No new PLAY MB scheduler or pixel backend was implemented.

A reproducible benchmark, warmup + 3 trials / median and allocated bytes, not real FPS nor resident memory quota:
```powershell
dotnet run --project src/QBSharp.Benchmarks -c Release --artifacts-path ops/artifacts/editor-perf-after -- ops/artifacts/perf-latest.json
```

Gate 579/579 Debug/Release, the total renderer still has a significant cost. Close the old editor with Ctrl+Q when you want and open the new one; no buffer is forcibly closed.

## Modern consolidation — loops
`qbs test samples/qbsharp/modern-loops.bas` verifies the modern loops. Classes and Import are available in the same dialect; QBasic remains opt-in through `--dialect qbasic`.

## Modern selection and iteration
Source 0.7.0-alpha.4: `ElseIf`, `Select Case` and `For Each` in the qbs dialect; classes preserved. Build/test/release gate to be reconfirmed.
- Multi-line ElseIf, Boolean conditions, evaluated only up to the first true branch; a single End If, no new scope compared with If.
- Select Case: the selector is evaluated once; Case value, comma list, inclusive range `a To b`, `Is <|<=|=|<>|>=|>` and a final Case Else. First match, no fall-through. Tests evaluated in order and short-circuit, CLR comparisons of the modern dialect (ordinal case-sensitive strings). No Exit Select.
- For Each counter [As type] In expression / Next [counter]: a synchronous CLR IEnumerable, including collections and host objects. Without As the counter must exist; with As it is local per iteration, with checked conversion. The collection is evaluated once, the enumerator is disposed on end/Exit/error/cancellation, Exit For is shared with the numeric For. No snapshot, changes to the collection follow CLR rules; no IAsyncEnumerable or pattern-only enumerables. Synchronous host calls are not pre-emptible.
- Dedicated ASTs and GuardedParser visits all the new children. The REPL recognises End Select; completion includes the keywords.
- Coverage ModernSelectionTests and samples/qbsharp/modern-selection.bas. Run `qbs test samples/qbsharp/modern-selection.bas` after updating the tool.

### Gorillas: empty INPUT answer fix
Use the current source until the global tool is rebuilt: `dotnet run --project src/QBSharp.Cli -c Release -- run samples/qbsharp/gorilla.bas --dialect qbasic`. In CMD enable Windows audio with `set QBS_QBASIC_AUDIO=1`, or timed silent audio with `set QBS_QBASIC_AUDIO=mute`. Pressing Enter at the points prompt is a valid empty answer.

## Original framebuffer diagnostics
In CMD: `set QBS_FRAME_PNG=C:\Temp\gorilla-frame.png`, then run from the source with `--dialect qbasic`. The PNG is updated on interactive graphics callbacks, at native resolution and from the visible page, without reduction to cells. Use an existing folder. This opt-in path intentionally slows the game down: disable it with `set QBS_FRAME_PNG=` after the diagnosis. Not available in the editor.

## PCCanvas graphics window (Windows, QBasic run)
In CMD set `set QBS_GRAPHICS=pixel`, disable the export with `set QBS_FRAME_PNG=`, then run from the source `dotnet run --project src/QBSharp.Cli -c Release -- run samples/qbsharp/gorilla.bas --dialect qbasic`. The window shows the whole framebuffer of the visible page at a 4:3 ratio with black bands and nearest-neighbour; refresh at most about 30 Hz. The keyboard is available from the window or the console; line input and prompts stay in the console. Closing the window interrupts at the next poll/callback; Ctrl+C in the console remains available. The option is not active for edit, batch or non-Windows platforms; `set QBS_GRAPHICS=` restores the console preview. Diagnostic PNGs are limited to one write per second, always costly. It is not hardware emulation nor an FPS guarantee.

### Gorillas: budget during Rest
Use an interactive run without --max-steps and without QBS_MAX_STEPS for cooperative TIMER waits. Empty loops do not continuously consume CPU; a stopped clock or a wait beyond 60 s fails instead of disabling all limits. The Ralf launcher is external: check any explicit budgets imposed by the launcher.

### Slow Gorillas banana: historical calibration
Calibration measured on this machine: 256774 iterations / half a second, an EGA Rest(.02) wait estimated at 13.35 s per step. To try in CMD: `set QBS_TIMER_WAIT_SCALE=0.001` before running from the source; the estimated wait is about 13 ms, no guaranteed FPS. The default 1 keeps the original timing. Restore with `set QBS_TIMER_WAIT_SCALE=`. It scales only empty elapsed-TIMER waits, not the trajectory, the general clock or audio. The value is not portable to all CPUs: adjust it if too fast/slow.

Update 2026-10-21 (Screen9 2.3.1): `ops/measure-gorilla-calibration.cs` measures about 53000 iterations, EGA Rest(.02) about 2.75 s per step; with `QBS_TIMER_WAIT_SCALE=0.005` about 14 ms, tried by hand and judged playable. Re-measure after every interpreter optimisation: faster = longer waits.

The QBS_GRAPHICS=pixel window now uses an off-screen backbuffer and reuse on repaints. Restart from the source to pick up the fix; commands/timing scale unchanged. This does not fix already wrong fonts/sprites nor sequential audio or AST cost.

An isolated double-buffer build (it does not close the game already open): `dotnet ops/artifacts/double-buffer/bin/QBSharp.Cli/release/QBSharp.Cli.dll run samples/qbsharp/gorilla.bas --dialect qbasic`. Keep QBS_GRAPHICS=pixel, QBS_TIMER_WAIT_SCALE=0.001 and the PNG disabled. For an idle benchmark: `dotnet run --file ops/measure-pixel-idle.cs -c Release --artifacts-path ops/artifacts/pixel-benchmark`.

### IBM fonts

Graphics modes use the IBM EGA 8x8, EGA 8x14 and VGA 8x16 ROMs included in Screen9 2.4.0 (The Ultimate Oldschool PC Font Pack v2.2 by VileR, CC BY-SA 4.0: the licence is in `fonts/LICENSE.TXT` of the Screen9 package). To use other fonts: `QBS_FONT_DIR` pointing to a folder with `ibm8xN.rom`. `QBS_RASTER_FONT=compact` forces the 5x7 font.

### Gorillas in a single window

```powershell
$env:QBS_GRAPHICS='pixel'; $env:QBS_QBASIC_AUDIO='1'; $env:QBS_TIMER_WAIT_SCALE='0.005'
Start-Process dotnet -ArgumentList 'src\QBSharp.Cli\bin\Release\net10.0\QBSharp.Cli.dll','run','samples\qbsharp\Gorilla\GORILLA.BAS','--dialect','qbasic' -WindowStyle Hidden
```

Text, graphics and INPUT stay in the "QBSharp — QBasic" window; `-WindowStyle Hidden` avoids the console. Closing the window ends the program. From a terminal that is already open, the same `dotnet ... run` command uses the window and leaves the terminal free but visible; any final errors appear there.
