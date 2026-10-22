# QBSharp — language and CLI specification

Specification **0.7**, phase 7; CLI **0.7.0-alpha.2**. VB.NET reference, **not full VB.NET/VB6 compatibility**. "Supported" means executable behaviour. An AST interpreter without Roslyn, C#/VB/IL generation or emission of CLR types.

## 1. Lexicon — supported

ScriptSource: .NET text and a file/virtual name; Core does not alter the text. CLI files are strict UTF-8 with a standard BOM, stdin is UTF-8 with a single leading U+FEFF consumed. Later BOMs are not removed.

Names are ordinal case-insensitive: a Unicode letter or `_`, then Unicode letters/digits or `_`. UTF-16 coordinates, line/column from 1, offset from 0, half-open ranges, a tab is one column. LF/CR/CRLF, one statement per line, the last newline optional, blank lines/apostrophe comments. Strings in double quotes with a `""` escape, no C escapes/multi-line.

Non-negative decimal integers are Integer tokens up to Int32.MaxValue then Long up to Int64.MaxValue; the sign is separate, -2147483648 is born Long, Int64.MinValue comes from the host. Reals have digits before/after the point, invariant Double, no exponent/suffixes/hex/date literals. True/False/Nothing literals. No REM, `_` continuation, `:` separator, `[keyword]` or calls without parentheses.

Reserved keywords: Dim, As, If, Then, Else, End, While, Function, Return, True, False, Nothing, Not, And, Or, AndAlso, OrElse, Mod, Import, Class, Namespace, New, Sub, Public, Private, Property, Me, Of. For/Next/Do/Loop/Select/Case are reserved and not implemented; Private is not supported. **After the dot, keyword/literal names are also allowed**: `Ui.Select`, `data.End`, `data.True`; this does not allow `Dim Select` declarations. For keys that are not identifiers use `data.GetValue("a-b")`. HostBindings validates the form, not the keywords: avoid reserved names in global bindings.

## 2. Core — supported

```text
statement := dim | assignment | expression | if | while | function | sub | return | class | namespace | import
dim := 'Dim' name ['As' type] ['=' expression]
assignment := name {'.' memberName} '=' expression
if := 'If' expression 'Then' newline block ['Else' newline block] 'End' 'If'
while := 'While' expression newline block 'End' 'While'
function := ['Public'] 'Function' name '(' [parameters] ')' ['As' type] newline block 'End' 'Function'
sub := ['Public'] 'Sub' name '(' [parameters] ')' newline block 'End' 'Sub'
parameter := name ['As' type]
return := 'Return' [expression]
call := expression '(' [expression {',' expression}] ')'
member := expression '.' memberName
```

Multi-line blocks, also empty, no ElseIf/inline If. An `=` after an assignable chain in statement position is an assignment; in expressions it is equality. Return outside Function/Sub is a parser error; Sub/constructor have no Return value. Calls/members/parentheses can be chained, expressions on one line. An expression statement discards the value, the REPL echoes it.

Dim without As is dynamic, not VB.NET static inference; without an initialiser it is Nothing. As imposes the type on declarations/assignments/parameters/return. Default CLR value type, Nothing for references/script classes. Numeric conversions are checked, integral narrowing only without a fraction; no text/number/Boolean coercions. Nothing is not accepted for a non-nullable value type, Object keeps any object.

Session global scope and lexical Function/Sub, ByVal positional parameters with exact arity, args from the left. Duplicates in the same scope are an error, unknown assignment is an error. If/While do **not** create scope: a repeated Dim in the loop fails. Declarations in execution order, no hoisting, closures/recursion/parent shadowing. The end of a Function without Return gives Nothing (As value type is an error); Sub is always Nothing. Return crosses blocks. Host objects keep identity/mutability.

## 3. Expressions and CLR types — supported

Decreasing precedence: member/call/parentheses/New; `^`; unary signs; `* /`; `\`; Mod; `+ -`; `&`; `= <> < <= > >=`; Not; And/AndAlso; Or/OrElse. Power is right-associative, the other binaries left. -2^2 -> -4 Double, 2^-2 -> 0.25.

AndAlso/OrElse short-circuit; And/Or eager Boolean-only, no bitwise/truthiness. `&` is Convert.ToString invariant / Nothing empty; `+` is numeric. Floating promotions, then Decimal, then Long, then Integer; checked integral/Decimal, overflow without promotion. `/` and `^` are Double, zero/non-finite is an error. `\` Integer/Long truncates towards zero. Mod is the CLR remainder, sign of the dividend, zero is an error. Numeric comparisons are Double if floating, otherwise Decimal; String ordinal case-sensitive, Date chronological. Non-numeric equality is CLR Equals, Nothing only with Nothing; an incompatible ordering is an error.

```text
type := qualifiedName ['(' 'Of' type {',' type} ')']
new := 'New' type '(' [arguments] ')'
```

Structured TypeSyntax for New/ScriptParser.ParseType; declarations keep a canonical string TypeName for the earlier API. Nested qualified/generic names are supported. Aliases Integer/Long/Double/Decimal/Boolean/String/Object/Date, List(Of T), Dictionary(Of K,V); qualified System.Int32/Int64/Double/Decimal/Boolean/String/Object/DateTime and System.Collections.Generic.List/Dictionary.

Other types only through ScriptEngineOptions.ClrTypes alias->Type, also generic definitions, or a Type supplied by the host. No assembly search/download. CLR New with a public constructor of exact arity / binder §5; a value type with zero args is the default. No Optional/ParamArray/open generic/abstract; MakeGenericType checks arity/constraints. CLR-only generics: **no List(Of ScriptClass)**, use List(Of Object). Non-type variables with built-in names do not hide CLR aliases; explicit script types/containers can qualify names.

## 4. Classes and namespaces — supported

```vb
Namespace Ops.Models
    Public Class Counter
        Property Value As Integer = 0
        Property Items As List(Of Integer) = New List(Of Integer)()
        Sub New(initial As Integer)
            Me.Value = initial
        End Sub
        Sub Add(amount As Integer)
            Value = Value + amount
            Items.Add(amount)
        End Sub
        Function Twice() As Integer
            Return Value * 2
        End Function
    End Class
End Namespace
Dim c As Ops.Models.Counter = New Ops.Models.Counter(20)
c.Add(1)
Print(c.Twice())
```

The class body has only auto Property, Function/Sub/Sub New, Public is optional, all are public. Property name [As type] [= expression] on a single line. One member per case-insensitive name / one constructor, no overload/inheritance/interface/events/nested/generic script class/explicit getter-setter/Private/Shared/Readonly.

Class/Namespace are forbidden inside Function/Sub; a Class is possible in a global branch. A Namespace contains only Class/Namespace, no Dim/Function/Import directly; qualified/nested/reopenable without duplicates, collisions with values/modules are an error. Not VB.NET Imports, qualify where necessary.

A Class creates a readonly descriptor, not a CLR Type. Duplicates are a parser error; member types are resolved at construction/invocation. Every New scopes instance + closure, Me/methods are bound readonly, properties are independent (host objects possibly shallow shared). All the properties get their defaults before the initialisers in order, then the constructor; absent -> zero args. Methods access properties implicitly or through Me; parameters shadow properties. Bound methods can be stored/invoked. As ScriptClass accepts Nothing or an instance of that descriptor, identity not structural typing. ToString is the qualified name (module basename canonical, `__init__` too). Initialiser/conversion errors keep the property source; construction and constructor Invoke count separately in the call limit.

**Phase 5 data API:** an instance implements IScriptPropertyBag with a shallow readonly snapshot of the auto-properties only, not of methods/Me/closures. Classes/namespaces/modules are IScriptOpaqueValue and not serialisable as code. The host can inspect Properties or use EvaluateAsync. IScriptMemberProvider exposes GetMember/SetMember for dynamic objects and libraries.

## 5. Interoperability — supported

Reflection only on public instances: property/field get/set with accessors/readonly checked, method calls, void->Nothing. Indexer through get_Item/set_Item. A reflected Task/ValueTask is not awaited; an async HostFunction is awaited. A delegate is not directly callable. No static/extension/Optional/ParamArray/ByRef/generic method invocation.

Engine cache (Type, uppercase name)->candidates, overload per call: exact 0 / assignable 1 / conversion-null 2, a unique minimum sum, a tie is an error. New has the same scoring. The cache keeps the Type for the engine lifetime, weak/collectible in future. TargetInvocationException is unwrapped, inner/span kept; cancellation propagated. Script/container/provider dispatch without reflection on interpreted members.

## 6. Runtime Import — supported

```vb
Import "lib/models" As Models
Dim c = New Models.Ops.Counter(21)
Dim moduleName = "helpers"
If True Then
    Import moduleName As Helpers
End If
```

Import expression [As alias], a non-empty String, only when executed, also in Function/If/While. Not a textual include / VB.NET Imports. The default alias is the last component without extension, `__init__.bas` is the directory name, an identifier that is not a keyword/literal, or As is required.

FileModuleResolver: directory of the importing file -> UserLibraryDirectory -> StandardLibraryDirectory; virtual BaseDirectory. Engine defaults cwd, $HOME/.qbs/lib, AppContext.BaseDirectory/stdlib; no stdlib .bas payload. Host override / IModuleResolver. The YAML config does **not** change resolver/roots. Per root an explicit .bas is exact, otherwise name.bas then name/`__init__.bas`. Absolute/../ allowed, FullPath is not symlink realpath/sandbox. Identity is case-insensitive on Windows, ordinal elsewhere. An imported function uses its own file's AST, not the caller's.

A module scope is isolated, parent explicit host bindings, not the caller's globals/locals. It exports its own names, live cells; alias/namespace/class/methods are readonly, variables writable. Host SetVariable/RegisterFunction updates the host parent, script global mutations do not.

Session cache identity loading/loaded/failed: the initialiser runs once, different aliases share state. The resolver is still called / re-reads the file per identity, not an I/O cache. No reload/eviction. A failed parse/runtime is cached until a new session; cancel removes loading and allows a retry. The alias is published only on success, host side effects are not rolled back; an alias collision can follow the initialiser, same alias / same module is idempotent.

Cycles QBS3002 with a chain/import span; depth QBS3003, MaximumImportDepth 64 range 1..64. Tokens/budget shared. Import I/O/missing at runtime is QBS2001 exit 4; main missing is exit 5; a module parse keeps its own file/line exit 3. IAssemblyProvider is a phase 9 contract, not implemented.

## 7. Embedding and diagnostics — supported

ScriptEngine/ScriptParser interfaces, ParseType concrete. CreateSession is a shallow binding snapshot, isolated state/modules. ExecuteAsync/EvaluateAsync, Variables readonly snapshot, SetVariable/RegisterFunction/DisposeAsync. Concurrency/read/mutation/dispose during execute are rejected: cancel + await then dispose. Shared host objects require synchronisation. Dispose is repeatable.

DI AddQBSharp bindings transient / parser-engine singleton; AddQBSharpContracts bindings only. The parser is complete before execute, the lexer has multiple diagnostics / the parser is fail-fast, the runtime does not roll back. ScriptRuntimeException Diagnostic/inner. Core has no UI/Spectre/YAML/EF/AI; Import I/O only as requested by the script; CLR mapping uses System.Text.Json. Not a sandbox.

| Code | Meaning |
|---|---|
| QBS1001/1002/1003 | Literal out of range / unterminated string / unexpected character |
| QBS1100 | Syntax / parser nesting 128 |
| QBS1101 | AST depth 128 |
| QBS2001 | Runtime/host/conversion/Import/standard library |
| QBS2002/2003 | Step / call-construction limit |
| QBS3002/3003 | Import cycle/depth |

Cancellation of lexer/parser/AST/type/nodes/loop/Import I/O and before-after host; it does not forcibly interrupt a sync host. Synchronous CPU until an incomplete await (UI host Task.Run if necessary). MaximumSteps 1M (>0), MaximumCallDepth 128 (1..128), reset per Execute/Evaluate; nodes, not lines. Parser/AST 128 includes Class/Property/Namespace/Import/New/TypeSyntax. Standard library limits §9 separate, no general memory quota / complete script stack trace.

## 8. CLI and REPL — supported

```text
qbs run file.bas [--max-steps N] [-- args]
qbs test file.bas [--max-steps N] [-- args]
qbs repl [--max-steps N]
qbs new [file.bas]
qbs edit [file.bas]
qbs complete [prefix]
qbs help [command]
qbs --help | -h | --version
```

run/test `-` is the whole stdin, canonical file for diagnostics. The CLI is case-sensitive, the language case-insensitive; a file/args after -- are literal. Default is help / context --help/-h. New main.bas, .bas required / CreateNew UTF-8 / no-overwrite / existing parent. --max-steps > QBS_MAX_STEPS IConfiguration > 1M positive Int64. YAML is data only, **not** engine options. Informational help/version/complete and new do not load config; run/test/repl do, before executing.

Print is variadic space-separated invariant / Nothing empty / newline; Print() blank. Args String[], GetValue(0)/Length. Test: Assert(Boolean[,message]), AssertEqual(expected,actual[,message]) fail-fast / count / CLR Equals (1 differs from 1.0); at least one assertion. No discovery/deep comparison/tolerance. The Print/Args/assertion bindings are CLI, manual opt-in for hosts.

Stdout literal without markup/reflow for Print, stderr errors; help/test/:vars/UI Spectre human-readable. Exit 0 success; 1 assertion / no assertions; 2 CLI/options/edit non-interactive; 3 lexer/parse/AST; 4 runtime/host/limits/Import/standard calls/assertion misuse; 5 main I/O/encoding/new existing/**invalid or ambiguous YAML config**; 6 internal; 130 cancellation/Ctrl+C. File errors from standard calls are wrapped with a call site exit 4. GenericHost DI/config/logging without lifecycle chatter.

REPL persistent state; a single expression echoes non-null, others Execute; x=3 assign, (x=3) compare. If/While/Function/Sub/Class/Namespace/Public blocks are multi-line, a detector outside comments/strings, a blank line forces parse; no multi-line parentheses. Buffer 65536 / history 200. :help/:vars/:reset/:cancel/:complete prefix/:quit. The first error is sticky until EOF/quit (reset does not clear it), EOF parses pending, quit discards pending, cancel empties the buffer, Ctrl+C ends with 130. <repl> positions relative to the submission. Reset reloads config through new bindings.

Tty only for non-redirected stdin/stdout / interactive capabilities, Tab first candidate of the final token / Up-Down history / Backspace end-of-line / Ctrl+D empty EOF; not a full-screen editor. Wide glyph/wrapping/advanced navigation in future. Redirected without banner/prompt. Complete prefix of commands/options/.bas/directories max 1000 entries; REPL keywords/variables, no member/context completion. PowerShell adapter ops/register-completion.ps1, quote spaces, no automatic profile / Bash-Zsh.

Tool QBSharp.Cli/qbs 0.7.0-alpha.2 local per user, not nuget.org; edit available (§12). ops/verify-tool.proj checks the version and the installed commands.

## 9. Standard library — supported phase 5

QBSharp.StandardLibrary is separate, the CLI automatically registers **Json, Yaml, File, Directory, Path, Config, Ui**. Host: `await new StandardLibraryHost(options, ui).CreateBindingsAsync(token)` then CreateSession; Core alone does not receive them. AddQBSharpStandardLibrary DI, the default host UI is UnconfiguredTextUi: provide an ITextUi for rendering/dialogs.

### JSON/YAML and data

| Script API | Behaviour |
|---|---|
| Json.Serialize(value), Yaml.Serialize(value) | Text from CLR/dynamic data/script properties |
| Json.Deserialize(text [, typeName]), Yaml.Deserialize(text [, typeName]) | Dynamic if no type; otherwise a type resolved in the interpreter scope |
| File.ReadJson(path [, typeName]), File.ReadYaml(path [, typeName]) | Reads UTF-8 and deserialises |
| File.WriteJson(path, value), File.WriteYaml(path, value) | Serialises and writes |

TypeName is a String, e.g. "Ops.Job", "List(Of Integer)"; it must be an available type. Script materialisation uses a **zero-argument** constructor and initialiser, then known case-insensitive/typed properties; an unknown property is an error, missing is the default. A constructor with mandatory args is not supported, side effects are not rolled back; mapping recursion 64 / budget / token. CLR mapping is System.Text.Json public data contract, not the VB binder; polymorphic fields/metadata not enabled. Host JsonCodec/YamlCodec DeserializeClr(text, Type, token) for CLR, no Script context required.

DynamicRecord is case-insensitive mutable, with a readonly shallow Properties snapshot. Dot get/set; missing is an error; data shadow the helpers Count/Keys/GetValue/SetValue. Fallback helpers: Count, Keys List, GetValue(String), SetValue(String,value). Sequences are List(Of Object), access get_Item/set_Item. JSON numbers Integer/Long/Decimal/Double according to the value; exact numeric types are not guaranteed to round-trip. ObjectGraph projects script auto-properties, CLR public non-indexer properties (not fields), IDictionary with String keys only, IEnumerable, primitives/null. Date/DateTimeOffset ISO O, Guid/TimeSpan/char/Enum as text. Shared references are duplicated; cycles/non-finite/code/namespace/Type/delegate are errors. It serialises neither methods nor object identity.

YAML is data-only, single document; **no explicit anchor/alias/tag, also !!str**, complex keys / multi-document / case-insensitive duplicates / non-finite are rejected. Null/~, Boolean true/false, decimal numbers/exponents; quoted/block scalars as text, dates and yes/no as text (a subset, not full YAML). The serialiser quotes strings, invariant format. An empty/null config -> an empty mapping.

SerializationLimits default depth 64 (1..64), nodes 10000, text 1048576 UTF-16 characters; JSON/YAML checked, project graph/cancellation. No streaming allocation quotas: intermediate serialisation can allocate before checking the length; CLR getters/constructors are host code and cannot be interrupted.

### Filesystem

| API | Meaning |
|---|---|
| File.Exists(path), ReadText(path), WriteText(path,text), Delete(path), Copy(source,dest) | UTF-8 strict / read BOM, write without BOM; binary Copy |
| Directory.Exists(path), Create(path), Delete(path [, recursive]) | Delete defaults to False, deleting the base is prohibited |
| Directory.Files([path]), Directories([path]) | Non-recursive listing, sorted absolute String[] |
| Directory.Glob(pattern [, root]) | Recursive file-only, relative pattern e.g. **/*.bas |
| Path.Combine(parts...), FullPath(path), GetFileName(path), GetDirectoryName(path), GetExtension(path) | Path operations |

BaseDirectory is the cwd captured when the service is created, **not** the script/imported directory; host override through FileSystemOptions. Relative to the base, absolute paths allowed by default. Write parents must exist; Write/Copy overwrite through a temporary in the same directory + move, best-effort cleanup, not a transaction / durability guaranteed cross-OS. Copy does not change the encoding. DeleteFile on a non-existent file is a CLR no-op.

MaximumFileBytes 1048576, MaximumEntries 10000 (also counts directories during traversal, an error without truncation); listing/glob skip reparse points, IgnoreInaccessible false. Glob ../ and absolute patterns are forbidden; glob match is ignore-case on Windows, ordinal elsewhere. RestrictToBaseDirectory is a host opt-in: it verifies containment and reparse along the path up to the base; **not a sandbox**, races/ancestors/symlinks/Import/reflection not covered. The CLI default is not restricted. I/O/traversal tokens and before-after dispatch; the sync filesystem is not forcibly interruptible.

### Configuration

Automatic search for qbs.yaml **or** qbs.yml in the base (CLI cwd), both -> error, absent/empty -> empty data. A root mapping is required, no merge/env substitution/reload watch. Config.Data is an in-memory mutable DynamicRecord, Config.Source full path/null, Config.Get("ops.name" [, default]) dotted case-insensitive lookup, missing default/Nothing. The Config container is readonly; data are not persisted automatically. YAML does **not** configure max steps/import roots/credentials. Host LoadConfiguration=false disables it.

### Spectre UI

| Script API | Result |
|---|---|
| Ui.Window(title,text) | Panel with a double border, Nothing, not full-screen |
| Ui.MessageBox(text [, title]) | Panel, Enter if interactive, "OK"; redirected does not read input |
| Ui.InputBox(prompt [, default]) | String, empty -> default |
| Ui.Menu(title,choices), Ui.Select(title,choices) | A String choice |
| Ui.MultiSelect(title,choices) | A sequence of Strings, also empty |
| Ui.Table(rows) | A table, union of columns of mappings or scalars, Nothing |
| Ui.Progress(percent [, description]) | Progress snapshot 0..100, Nothing |

Choices are a sequence of 1..1000 ordinal-unique Strings. InputBox/Menu/Select/MultiSelect reject non-interactive with a runtime diagnostic, **they do not consume stdin**. Window/MessageBox/Table/Progress are allowed redirected; redirected Progress is percentage text. Table max 1000 rows / 100 columns, mixed scalar/mapping is an error, no rows is a message; nested values ToString, not pretty JSON. User texts literal / markup escaped. Progress is not a persistent handle / worker task, Window is not an editor popup. Cancellation of async prompt / token dispatch; a real terminal is not simulated by the redirected process tests.

## 10. Not supported / later phases

Explicit accessors/Private/Shared/Readonly/inheritance/generic script classes/interfaces/structures/enums/events/overload/Optional/ParamArray/ByRef are absent. ElseIf/For/ForEach/DoLoop/SelectCase were future (now see the modern consolidation sections at the end). AsyncAwait/syntactic LINQ/XML literals/My/WithEvents/Handles/OnError/GoTo/VB6 binaries/Option Strict-Explicit outside the initial plan.

Phase 6 editor implemented (§12); phase 7 Ralf implemented (§13); next phases: 8 parametric SQL / dynamic EF proof of concept / template syntax; 9 dynamic package/assembly opt-in / policy. No future API ready, do not automatically run generated code.

## 11. Test matrix

| Surface | Suite |
|---|---|
| Lexicon/positions/grammar/errors/cancel/nesting | ParserTests, CancellationTests, ContractTests, Phase5MemberTests |
| Operators/CLR/defaults/conversions/scope/functions/closures/steps/concurrency/dispose/rollback | LanguageTests, SafetyTests |
| Reflection/public-private/readonly/cache/overload/exception | ReflectionTests |
| Class/autoProperty/Me/SubNew/methods/typed/Namespace/New/CLR generics | ClassTests, Phase4TypingTests, Phase4RegressionTests |
| Runtime Import/alias/cache/isolation/roots/package/source/cycle/depth/cancel | ModuleTests, Phase4RegressionTests |
| Embedding/classes/modules/CLI/REPL/.bas processes | Phase4CliTests, Phase4ProcessTests, EndToEndTests |
| CLI/streams/help/new/exit/Print/Args/assertions/encoding/BOM/CPU cancel/completion | CliTests, CliInputTests, CliProcessTests, CliBomTests, CliCancellationTests |
| JSON/YAML/CLR/dynamic/script properties/typed/cycles/limits/invariant/invalid | SerializationTests |
| File/Directory/Path/glob/config/UTF-8/temp/policy/cancel | FileAndConfigurationTests |
| UI all APIs/adapter/DI/readonly/literal/callsite/non-interactive | StandardUiTests |
| CLI config cwd/invalid/REPL keyword/redirected dialogs/.bas end-to-end | Phase5ProcessTests: phase5.bas, phase5-test.bas |

A separate global tool smoke test after build/test. Windows ConPTY editor keyboard/mouse/save/run/exit is automated; real terminal UX and the Unix backend still require manual smoke tests. Every new feature requires updated SPEC/tests/OPENPOINTS.


## 12. Interactive editor — supported phase 6

`qbs edit [file.bas]`: an interactive ANSI terminal, stdin/stdout not redirected (otherwise exit 2, no file created). A single .bas document; an absent path is Untitled, a non-existent path is a named buffer without creating the file until Save. UTF-8 strict / read BOM / write without BOM, max 1048576 UTF-16 characters and 4 MiB editor I/O; the file's newline is preserved, new insertions normalised. Save through temporary/move, comparison of disk text vs last save before overwrite; not race-proof/transaction/locking. Existing parents. New/Open/Exit with dirty state propose Save/Discard/Cancel; Save As/overwrite confirmed, an open error keeps the buffer.

File/Edit/Run/Help menu, F10/Alt F-E-R-H and arrows/Enter/Esc, mouse click. F1 help, F2 Save, F3 Open (path dialog), F4 go to error, F5 run buffer, F6 stop. Ctrl N/O/S/Q, A/C/X/V and Z/Y; **internal** clipboard, no OS clipboard. Arrows/Home/End/PgUp-PgDown, Ctrl Home/End, Shift movement selects; mouse click positions, Shift click extends, wheel scrolls code/output. Enter newline, Tab 4 spaces, Backspace/Delete; undo/redo max 64 snapshots. Ctrl+C is copy in the editor, not cancellation. Esc closes menu/dialog, otherwise stops the run or toggles output focus. Clear output menu.

F5 runs a snapshot in a new AST session on Task.Run, it does not save implicitly. Args empty, steps 1M (edit does not accept --max-steps nor use QBS_MAX_STEPS), import relative to the document path or cwd for Untitled; imported modules read from disk, not multiple buffers. Standard file/config cwd, config loaded per run. One run at a time, editing allowed during a run. Output capped at 1000 lines / 262144 characters queued per run, final Completed/Error/Canceled; not a shell/REPL/debugger. Script errors stay in the editor and do not determine the CLI exit: normal editor exit 0, host cancellation 130; startup I/O 5 / arguments 2.

F4/click on the output jumps to the file/line/column span. A buffer modified after a run: the jump is refused, a rerun is required; an import diagnostic can open the definition file with dirty confirmation. Config/host errors without a span are shown in the output but not navigable. RunUi: Window/Table/Progress in the output; MessageBox/InputBox/Select/Menu/MultiSelect controller dialogs mouse/keyboard (Enter confirms, Esc cancels the run, Space toggles multi). Input dialog editing is end-of-line, default already in the field; not a multi-line text editor. No assert binding in the editor run.

Spectre grid renderer blue/grey old-style, line gutter, cursor/selection and lexer highlighting (first 4096 chars of each line), horizontal/vertical scroll and resize 40x12..300x100. Below 40x12 rendering/editing are suspended until resize: avoid small terminals. One cell per code unit: controls sanitised, a tab displayed as a space, characters beyond Latin-1 represented by '?' without modifying the file; CJK/emoji/combining without complete display width. No find/replace, OS clipboard, drag selection, bracketed paste, multi-file tabs, debugger or editor completion in this phase.

Windows INPUT_RECORD input (mouse/window, quick-edit/line/echo/processed disabled), VT output enabled and original modes restored on dispose. Unix .NET key input and SGR mouse parser, VT mode reporting; backend not certified on real terminals. Alternate screen/cursor/ANSI reset in finally, controller/application tests with a fake backend, native normal ConPTY workflow. Cooperative cancellation: a sync host is not forcibly terminable, exit waits for the worker; not a sandbox / autosave after a crash.

Tests: EditorTests (document/controller/frame/decoder/file/run/cancel/lifecycle), EditorRegressionTests (editor.bas whole UI, import source, redirected CLI), EditorTerminalProcessTests (real Windows ConPTY mouse Help, typing, Save, Run, CtrlQ). TerminalHarness is test infrastructure only: it reopens CONIN$/CONOUT$ READ|WRITE before the .NET cache, invokes the compiled CLI entry point; not script generation/compilation. The QBS_EDITOR_CLI_PATH env allows testing the installed package DLL; the host is not distributed with the tool. The ConPTY test is Windows-only (on other OSes it returns without native verification). The ops/verify-tool.proj smoke test also verifies the installed Windows package. Ralf supported (§13).

## 13. RalfAI — supported phase 7

A separate adapter QBSharp.AI -> RalfAI.Providers **1.0.4** and Microsoft.Extensions.AI. It is RalfAI's chat factory, **not** AgentEngine / an autonomous agent: no tool registered, no automatic shell/file/AI execution. Core and StandardLibrary do not depend on AI; the CLI registers the host binding `Ralf` in run/test/repl/editor, embedding hosts opt in with RalfHost.AddBindings or DI AddQBSharpAI. Not a new keyword nor a standalone CLI command `qbs ralf`.

```vb
Dim answer = Ralf("Explain a blue-green deploy")
Print(answer.Text)
Dim parsed = Ralf("Return a JSON object with status = ready", "json")
Print(parsed.Data.status)
```

`Ralf(prompt [, format])`: a non-empty String prompt, max 8192 UTF-16; format text (default) or json, case-insensitive. It returns a DynamicRecord with Text/String, Provider/String, Model/String, Data (Nothing in text, dynamic scalar/list/map/null data in json). Strict data-only JSON through JsonCodec: no Markdown fences / case-insensitive duplicates / non-finite; invalid JSON is an error without echoing the content. The result is serialisable as data, never interpreted/run automatically. Script errors/disabled/config/provider/timeout QBS2001 with a call site; host cancellation OperationCanceledException/CLI 130, no logging of prompts/responses/secrets/provider stack or unsanitised inner exception.

### Configuration and secrets

AI is **disabled by default**. Explicit config through IConfiguration/env QBS_AI_PROVIDER (openai/anthropic/foundry), QBS_AI_MODEL and QBS_AI_API_KEY; a model is mandatory, no implicit cloud default. Foundry requires QBS_AI_ENDPOINT as an absolute HTTPS URL, without userinfo/query/fragment; QBS_AI_FOUNDRY_API chat (default) or responses. A custom endpoint only for Foundry, other providers use the standard SDK endpoint. Model max 200 / no control characters, a non-empty key; do not expose RalfSettings or the provider IConfiguration to scripts.

Alternative: **QBS_AI_CONFIG_FILE**, an absolute path to the external RalfAI JSON; no autodiscovery of any configuration folder nor reading credentials from qbs.yaml. Max 1 MiB, UTF-8 strict/BOM, JSON object depth 32, no watcher; re-read at every call, file/parse errors sanitised (no path/content in the message). Root AIProvider and the groups OpenAI/Anthropic/AzureAIFoundry; ApiKey/ModelId, Foundry Endpoint/Api. Unused non-string metadata ignored, malformed/duplicate credential fields detected. A default AIProvider absent from the file -> foundry (like the Ralf factory), a model is still mandatory. Foundry ContextModels:QBS_AI_CONTEXT_KEY (default qbsharp) -> DefaultModel -> Models:alias:ModelId or alias; otherwise a direct ModelId.

Present QBS_AI_* take precedence over the corresponding fields of the selected file, even when empty. **QBS_AI_PROVIDER="" disables AI and skips reading the file entirely**. The external config stays only in the host and is not added to Config.Data, Variables, prompts or the repository. The key is sent only to the provider by its SDK for authentication. User-provided inputs, the buffer included, are not automatically redacted: do not include secrets in them.

QBS_AI_TIMEOUT_SECONDS default 60 / range 1..300; QBS_AI_MAX_OUTPUT_TOKENS default 2048 / range 64..8192. Text output max 65536 UTF-16. CTS linked to the host token and WaitAsync, timeout distinct from host cancel; sync config loading and an SDK that ignores the token are not forcibly interruptible. No streaming / application retry / conversation history / cost accounting. The RalfAI client is created and disposed per request, no FunctionInvoking pipeline, tool-call content rejected.

### Ralf in the editor

**F7** or Run -> Ralf generate/edit. A consent dialog (default Cancel) explains that the current buffer and the instruction are sent to the provider. Then the instruction input (1..8192 chars), buffer max 65536 chars; context max 74752 chars plus the system guide. File path / other files / config / variables / credentials are not sent. A static guide to the QBSharp subset kept with SPEC, not full VB.NET syntax.

Generation Task.Run / cancellable F6/Esc, excluded when a script/AI is already active. A complete proposal validated with the AST parser, optional bas/vb/vbnet/qbs fences removed, invalid languages/format/syntax rejected; **syntactic validity is not security**. The preview shows all the old lines with '-' and the new ones with '+' (a replacement diff, not a minimal diff); vertical arrows/PgUp/PgDn/mouse wheel and horizontal arrows/Home for long lines. Enter/the Apply button applies only to the buffer, Esc/Discard discards. A buffer/path changed in the meantime -> the proposal is rejected. Application is one Undo transaction; no automatic save/run, F2/F5 stay separate. For a new .bas: New -> F7 -> review/apply -> Save As with an explicit path, generally cwd. It does not create/modify arbitrary files chosen by the model nor multi-file patches. Errors/proposal shown in the editor; nothing is sent to the cloud just by entering the editor.

### Verification and distribution

RalfTests/RalfSafetyTests/RalfConfigurationTests: a real factory without network, schema/overrides/file/secrets, binding/JSON/diag/timeout/cancel/no auto-execution, fake UI/preview/stale buffer/undo/limits/DI/processes and the ralf-test.bas fixture. Windows ConPTY also verified F7 consent / config disabled and no changes; deterministic generation/apply tested with a fake, no network in the normal tests. A separate opt-in live smoke test ops/smoke-ralf-live.cs (one small JSON request + an assertion, output/provider errors captured, not emitted), authorised external config; it does not modify/run generated code.

The original local NuGet feed is not available in the environment: the trusted package is present in the cache, copyable with ops/seed-ralf-feed.cs into the ops/artifacts/nuget-feed feed; ops/nuget.config maps RalfAI.Providers and nuget.org for other dependencies. The tool packs the adapter/dependencies, not credentials or the TerminalHarness. The live test does not prove every provider/model/proposal nor replaces a review of code/files.

## 14. Samples and classic QBasic compatibility

Layout: **samples/qbsharp/** at the root, next to src/ and ops/. The folder previously called src/examples was moved without modifying the files' bytes (SHA256 verified). Test fixtures link/copy into the samples/qbsharp/ output, the internal lib/models Import resolution unchanged relative to the file.

The historical GORILLAS provided by the user is QBasic Gorillas Deluxe 2.2 (header Microsoft/IBM 1.0 and Daniel Beardsmore 2.2); preserved as a **sample not supported by the modern qbs dialect**, not ported to QBS. Run with the modern qbs dialect: exit 3, QBS1003 `gorillas.bas(10,24)` on the first `#` suffix. The lexer precedes the parser and collects errors across the whole source, so DEFINT on line 7 is not necessarily the first diagnostic reported.

Further independent blocks: suffixes $/%/#/!, DEFINT/DECLARE, TYPE/fixed strings/classic SINGLE, CONST/DIM SHARED/array bounds and multi-dimensional, For/Do/SelectCase/ElseIf/inline If/colon/REM, GoSub/label/GoTo/OnError/Resume, numeric files OPEN/INPUT #/PRINT #, PRINT without parentheses and semicolons. The original QBasic graphics statements are not implemented; some equivalent operations are now available through the PC/Screen9 PCCanvas wrapper, PLAY/SOUND/DOS DEF SEG/PEEK/POKE/INKEY$ absent. Classic Boolean TRUE=-1 and bitwise And/Or differ from the CLR Booleans of the core.

It is not enough to fix the first token or the PRINTs: an explicit port of the game is needed, or a compatible layer plus a graphics/audio/DOS environment backend; no full compatibility is promised by the QBS name or by the editor. No language extension in this change.

**GorillasCompatibilityTests** tests: the original file parser/embedding/CLI, first span/code, zero Print/side effects and zero files created; independent snippets of 12 rejected classic constructs. The other modern samples must keep passing E2E after the relocation.

## 15. PCCanvas CGA/EGA/VGA — ConsoleLib 1.2.0

`PC` is a standard library binding to **Screen9.PCCanvas : Canvas**, NuGet **ConsoleLib 1.2.0** from the authorised local feed. No new VB/QBasic keywords. Mode object get-only, no register/BIOS/hardware/DOS emulated. `PC.Create(mode)` returns a real PCCanvas; Width/Height/Mode/Adapter/ColorCount/PageCount/PlaneCount/ActivePage/VisualPage readable from the host/script. The mode is immutable per instance; create a new canvas to change mode.

| Mode | Adapter | Pixels | Colors | Pages |
|---|---|---|---|---|
| 1 | CGA | 320x200 | 4 | 1 |
| 2 | CGA | 640x200 | 2 | 1 |
| 7 | EGA | 320x200 | 16 | 8 |
| 8 | EGA | 640x200 | 16 | 4 |
| 9 | EGA | 640x350 | 16 | 2 |
| 11 | VGA | 640x480 | 2 | 1 |
| 12 | VGA | 640x480 | 16 | 1 |
| 13 | VGA | 320x200 | 256 | 1 |

Text/mode 0/mode 10/other BIOS modes rejected; EGA modelled with 256 KiB. Logical framebuffer models, not a full emulation of the card.

| Script wrapper | Contract |
|---|---|
| PC.Create(mode), PC.Modes() | Factory/binding and a list of metadata of the 8 modes |
| PC.Clear(c [, index]) | Clear the active page, default 0 |
| PC.PSet(c,x,y,index), PC.Point(c,x,y [,page]) | Write returns a clipped Boolean; read index / -1 outside |
| PC.Line(c,x1,y1,x2,y2,index) | Line raster with clipped endpoints |
| PC.Box(c,x,y,width,height,index [,filled]) | Rectangle, non-negative dimensions |
| PC.Ellipse(c,x,y,rx,ry,index [,filled]) | Integer radii and fill |
| PC.Circle(c,x,y,radius,index [,start,end,aspect]) | Numeric radius, CCW arc radians 0..2pi, aspect default 1; end<start wraps |
| PC.Paint(c,x,y,index [,border]) | Bounded 4-connected fill; default seed-colour replacement |
| PC.CgaPalette(c,family [,bright,background]) | Mode 1 family 0/1, brightness flag / background PC16 |
| PC.Palette(c,index,hardwareColor) | CGA16/EGA64 code; mode 1 writable entry 0 only, mode 2 background black |
| PC.Dac(c,index,r,g,b), PC.Rgb(c,index) | VGA DAC 6-bit components 0..63; packed RGB8 RRGGBB Integer |
| PC.ResetPalette(c) | Default palette without changing pixel indices |
| PC.Active(c [,page]), PC.Visual(c [,page]) | Drawing/read vs displayed page independent |
| PC.ReadByte(c,address [,plane]), PC.WriteByte(c,address,value [,plane]) | Mapped video memory, bytes 0..255, strict bounds |
| PC.Get(c,x,y,width,height) | Strict copied PCImage indexed data |
| PC.Put(c,image,x,y [,operation,transparent]) | Clipped copy/pset/preset/xor/and/or, depth must match |
| PC.Snapshot(c [,page]) | An independent Integer[] indexed row-major copy |
| PC.Preview(c,columns,rows) | A new cell Canvas, sampled visual page, nearest 16 console colours |
| PC.Ascii(c,columns,rows) | Debug String[]: '.' index 0, hex 1..15, '#' 16..255 (not an exact VGA bitmap) |
| PC.Render(c [,columns,rows]) | Screen9 renderer on a reduced preview; optional pair, default viewport capped at 300x100 |

Coordinates/index/page/address are Integer (no implicit text/floating coordinates). Radius/angles/aspect numeric; finite/non-negative/range validation, no QBasic negative-angle radial lines. Degenerate Circle arcs rejected; a full degenerate ellipse works. Validation/source-span errors QBS2001, cancellation before/after dispatch and during fill/blit/projection. Sync primitives are bounded by the framebuffer; a partial draw is possible when cancelled mid-fill, no transaction rollback. Models are non-thread-safe, no general memory quota for many canvases.

CGA banks 0/8192 odd/even scanlines in 16 KiB, mode 1 packed 2bpp / mode 2 1bpp; EGA/VGA16 planar 64 KiB each / MSB-first bits (4 planes, single-plane VGA mono); mode 13 linear 64 KiB with 64000 visible bytes. Page offsets per plane width/8*height. PSet/shape and memory writes are synchronised, non-active page writes and padding bytes preserved. No port I/O / latches / masks / chain4 / CPU / timing / scanline / DOS memory or audio; **not PEEK/POKE system memory**.

EGA 64 hardware colours mapped to 2-bit RGB channels, default 16 PC palette. CGA families and VGA programmable 6-bit DAC. The VGA default is deterministic: 16 PC + 16 grey + 216 DAC cube + 8 black, not BIOS-ROM exact. Palette edits preserve logical indices and affect the next preview; rendering loses precision because the existing Screen9 native/text renderer supports only 16 console colours and cells, not pixel resolution or 256 simultaneous VGA colours. Exact indexed pixels/RGB remain available separately. PCImage is not the QBasic GET/PUT Long-array binary format. Generic inherited Canvas writes map the cell background low bits; inherited cell capture/snapshot is **not** indexed sprite fidelity: use PC.Get/Snapshot.

The renderer is injected through StandardLibraryHost/DI as ICanvasRenderer. PC exists with no renderer; PC.Render requires one. The CLI native console uses WindowsConsoleRenderer, redirected/editor uses the reduced TextCanvasRenderer. Text projection has coloured spaces; for a readable redirected sample use PC.Ascii. Spectre remains CLI/editor UI, **not runtime PC drawing**. No need for Roslyn or C# generation.

Tests: Screen9 PCCanvasTests (mode/palette/VRAM/page/shape/paint/sprite/projection/inheritance, plus the previous Canvas tests); QBSharp PCCanvasBindingTests (real NuGet types, wrapper/marshalling/diagnostics/cancel/injection/editor/process fixtures). `samples/qbsharp/pc-canvas.bas` default ASCII skyline; `samples/qbsharp/pc-canvas-test.bas` 12 deterministic assertions CGA/EGA/VGA/XOR/memory/palette. Native preview and hardware-exact visuals require further manual/renderer work. The original gorillas.bas is unchanged: modern qbs rejects its grammar; the opt-in QBasic adapter in §17 connects legacy statements but does not yet certify a complete game.

## 16. Opt-in QBasic dialect — foundations verified in the source

The modern qbs dialect remains the default and unchanged. The source offers `qbs run/test file.bas --dialect qbasic [--max-steps N] [-- args]`; qbs selects the modern parser, qbasic selects QBSharp.QBasic.QBasicEngine. The flag also applies to edit in the updated source (§19); the REPL stays qbs only. The already installed global tool 0.7.0-alpha.2 does not yet contain this dialect: use dotnet run from the source until the next pack/install. No full DOS compatibility promised.

### Lexicon, types and procedures

A dedicated lexer, source positions file/line/column UTF-16, apostrophe/REM comments, colon separators, double-quote strings / "" escape. Variable/procedure suffixes $ STRING, % INTEGER (Int16), & LONG (Int32), ! SINGLE (Single 32), # DOUBLE (Double 64); literals .5, 1#, exponent E/D, hex/octal (a subset, not all the signed behaviour of QB literals). Default SINGLE, DEFINT/DEFLNG/DEFSNG/DEFDBL/DEFSTR ASCII letter ranges. Numeric conversion checked, MidpointToEven rounding for integer assignment/CINT, overflow 6 / non-finite rejected. Numeric Boolean/comparison -1/0, NOT/AND/OR/XOR bitwise, + concatenates only two Strings; this is not the CLR Boolean semantics of qbs.

DIM/DIM SHARED, readonly CONST (inferred type or suffix), TYPE with typed scalar fields and STRING *1..32767 (UTF-16 padding/truncation, not OEM byte width). Record assignment copies the fields, ByRef keeps references; no recursive TYPE beyond 64 / array fields. Suffix identifiers are distinct; a suffix alias on a variable/array declared without suffix is resolved when the type matches. Arrays with lower/upper bounds, multi-dimensional rank 1..8 / max 1M elements, OPTION BASE 0/1, REDIM without PRESERVE. REDIM keeps the type even on an array declared empty; a type change is rejected. A homonymous array/scalar variable and all the details of the original QB declarations are not certified.

DECLARE SUB/FUNCTION are recognised as declarations, procedures hoisted in the file; no complete prototype/definition/external link validation yet. SUB/FUNCTION with optional parentheses for zero args, calls without parentheses / CALL, return by assigning the function name also with suffix/alias, EXIT SUB/FUNCTION. ByRef is the default for scalar/array/TYPE field, ByVal scalar copies, expression/literal/CONST uses a temporary (constants are never mutated); array element types checked, ByVal array not supported. Separate locals, DIM SHARED/CONST/host bindings visible to procedures. Host SetVariable preserves the declared type and does not modify readonly CONST. Arguments/indices/reference targets evaluated once, also null ANY; not ByRef CLR property or a full generic host alias.

### Flow and errors

FOR/NEXT with nonzero STEP, DO [WHILE/UNTIL] ... LOOP [WHILE/UNTIL], WHILE/WEND, EXIT FOR/DO, multi-line/inline If / ElseIf / Else, SELECT CASE with lists/ranges/IS/ELSE. The selector is evaluated once. Textual/numeric labels at the start of a line, GOTO/GOSUB/RETURN [label], Gosub stack 128. Not every jump between procedures or entry into a loop is compatible/certified.

ON ERROR GOTO label/0, ON ERROR RESUME NEXT, ERR/ERL, RESUME/retry/NEXT/label. A local handler or the main label used by procedures, errors in the handler are not swallowed; the diagnostic also reports the original cause and span. Resume clears state/causes after success. Step limit and CancellationToken are not recoverable through ON ERROR. Parse 1400 before execute (no partial execution on invalid syntax), runtime 2400 with an error number, step limit 2401; CLI syntax 3 / runtime 4 / cancellation 130. Error mapping subset: overflow 6 / subscript 9 / division 11 / type 13 / file 52-54-57-62-70 etc.; not all QB codes/error semantics.

### DATA, functions, input/output

DATA literal/string/unquoted text pool, READ typed target and RESTORE/default or label. The DATA position is shared per program; DATA labels of homonymous procedures and all the QB scope/compilation rules are not certified. SWAP, RANDOMIZE/RND per session deterministic with System.Random (not the bit-exact QB sequence). TIMER from the host clock, not hardware time/emulation.

Implemented builtins: ABS/ATN/SIN/COS/TAN/SQR/INT/FIX/CINT/CLNG/CSNG/CDBL/SGN; LEN/LEFT$/RIGHT$/MID$/LTRIM$/RTRIM$/UCASE$/LCASE$/STR$/VAL/CHR$/ASC/INSTR/STRING$/SPACE$/SPC/TAB; EOF/LBOUND/UBOUND/RND/TIMER/ERR/ERL. String indexes 1-based, STR$ positive leading space, PRINT numeric spacing / zones 14 and semicolon/newline; not a complete screen 0 layout. Some mathematical builtins return Double and literals/formatting/random/CP437 are not bit-for-bit compatible. No NaN/Infinity is allowed out of Single arithmetic/CSNG.

Numbered files OPEN FOR INPUT/OUTPUT/APPEND AS #1..255, maximum 16 handles and 1 MiB/file; PRINT #/WRITE #, INPUT #, LINE INPUT #, CLOSE (#list or all), KILL, EOF. UTF-8 strict, quote escaping/CSV WRITE and numeric/text input; not BINARY/RANDOM/GET-PUT file/SEEK/LOF/complete OEM. Console INPUT uses a TextReader, output a host TextWriter; I/O sync/bounded, AllowFileIO=false blocks file operations, default true / BaseDirectory cwd. Absolute/../ paths allowed: not a sandbox. Errors/cancel can leave earlier side effects, no rollback; dispose closes handles.

Embedding: QBasicEngine(QBasicOptions) implements IScriptEngine/IScriptSession, isolated vars/defaults/records/arrays/procedures/data/files/random. Execute/Evaluate, readonly shallow snapshot, SetVariable/RegisterFunction, async disposal, single-execution gate. Options Output/Input/BaseDirectory/AllowFileIO/MaximumSteps (default 1M)/MaximumCallDepth (default 128)/Clock. Public host members cached, exact arity / simple binder, numeric normalisation Short->Integer / Single->Integer if integral or Double for HostFunction; not Optional/ByRef CLR/static/generic binder. The JSON typed materialisation context is not available. No C#/VB/IL emission or Roslyn: an AST expression tree and a statement sequence with control links for labels/loops.

### Gorillas limits and tests

In modern qbs the historical sample still fails on the first #. The qbasic dialect in the source now integrates the services described in §17; this is not a certification of full compatibility or of a Gorillas match.

QBasicTests/QBasicRegressionTests/QBasicErrorContextTests verify the core, the diagnostics and the budget. The earlier 461/461 gate is historical; the updated services gate includes the complete suite 528/528 in Debug and Release (see OPENPOINTS). samples/qbsharp/qbasic-core.bas keeps 8 assertions.

## 17. QBasic services and raster text — verified source gate

These APIs are **only in the source** of the opt-in qbasic dialect. The global package 0.7.0-alpha.2 was not updated. ConsoleLib 1.2.0 remains a NuGet dependency; no code generation or change to the modern Core.

### Memory, input, time and audio

- DEF SEG [=expression], PEEK(offset), POKE offset,value: a private virtual 1 MiB memory per session. Segment/offset 0..65535, byte 0..255, address `(segment*16+offset)&0xfffff`; DEF SEG without a value goes back to virtual segment 0. Real-mode aliases work; the BIOS byte 1047 is virtual, not the host's NumLock state. In a graphics mode the CPU video window is mapped onto the current PCCanvas (Screen9 2.2): `&HA000` for EGA/VGA (64 KiB; in planar modes POKE writes the planes enabled by the map mask, PEEK reads the plane of read map select), `&HB800` for CGA (32 KiB, 16 KiB repeated); in SCREEN 0 those addresses stay ordinary virtual memory.
- OUT port,value / INP(port): only the EGA/VGA ports modelled by PCCanvas: &H3C4/&H3C5 (map mask, index 2), &H3CE/&H3CF (read map select, index 4), &H3C6-&H3C9 (VGA DAC, 6-bit components, auto-increment; in modes 11/12 colours go through the default attribute controller: colour 6 = register 20, 8-15 = 56-63). Any other port, or a DAC port outside a VGA mode, is error 5 with an explicit message; in SCREEN 0 the ports require a graphics mode. No access to real RAM, CALL INTERRUPT/ABSOLUTE, latches/write modes 1-3, 3C0/3DA or complete BIOS emulation.
- INKEY$ reads the non-blocking host callback, default an empty string. PCKeyboard.Encode returns characters and NUL+scan-code for arrows/Home/End/Insert/Delete/PgUp/PgDn/F1..F12; not all the Alt/OEM scan codes. The CLI connects Console.ReadKey/KeyAvailable only if interactive, without consuming redirected stdin.
- TIMER uses the host Clock. SLEEP [seconds] uses a cancellable Delay callback in 20 ms intervals and ends early on a key; zero/omitted waits for a key with 10 ms polling. Range 0..86400. A synchronous/non-cooperative host is not forcibly interruptible; clock and timing are not hardware-exact.
- BEEP emits an 800 Hz / 200 ms event. SOUND frequency,ticks uses ticks/18.2 s, frequency 0 (silence) or 37..32767, duration 0..65535. The PLAY parser is bounded 4096 chars / 1024 events with per-session state: notes A..G, #/+/-/dots, pause P, N0..84, O0..6, L1..64, T32..255, </>, MB/MF/MN/ML/MS. No variable expansion or X macros.
- Audio is the host callback PCTone(Frequency,Duration,Background). If a note needs audio without a backend, an explicit error, not a no-op. The Windows CLI offers Console.Beep only interactive and with `QBS_QBASIC_AUDIO=1`; on other OSes a backend must be supplied. The Background flag reaches the callback, but the CLI adapter plays sequentially: no faithful MB scheduler (see the background music section at the end). Audio tests are offline/fake, no sound or cloud in the gates.

### Legacy graphics and sprites

SCREEN 0 switches to the text model; SCREEN 1/2/7/8/9/11/12/13 creates a PCCanvas. Active/visual pages from the optional third/fourth expression (default 0). The SCREEN colour-switch does not emulate hardware features. PSET/PRESET, POINT(x,y), LINE (x1,y1)-(x2,y2)[,color[,B|BF]], LINE -(x2,y2), CIRCLE with optional color/start/end/aspect, PAINT and PALETTE are connected to the indexed canvas. Coordinates are rounded through BValues.Integer. CIRCLE uses the absolute value of aspect; radial lines/STEP/graphics viewport clipping/style masks/PALETTE USING are not implemented.

GET (x1,y1)-(x2,y2),array& and PUT (x,y),array&[,PSET|PRESET|XOR|AND|OR] use a 1D integer LONG array. A little-endian 16-bit width/height header codec: CGA1 width in bits 2bpp, EGA width in pixels with row-interleaved planes, mono packed 1bpp, VGA13 byte/pixel. Data checked for dimensions/buffer; PUT clipped, default XOR. Not an array with an initial offset, an INTEGER array, GET/PUT files or bit-exact compatibility of every QB format. Tests include the original CGA/EGA banana data and round-trip/XOR in several modes.

### Text, viewport and CLS

- SCREEN 0: an 80x25 grid, COLOR default 7/0. WIDTH [columns][,rows] range 1..300/1..100 resizes and resets cursor/viewport; an error does not alter the state. COLOR [foreground][,background] range 0..15, omitted arguments keep values, invalid ones do not alter the state. A SCREEN change resets background/cursor and picks the foreground within the palette.
- LOCATE [row][,column]: 1-based coordinates, omitted ones keep the current coordinate, grid range. The extra cursor/start/stop parameters recognised by the parser do not produce a hardware cursor. PRINT updates cells/pixels and cursor, newline/wrap/semicolon/zones 14/TAB; TAB continues from the real column even after a wrap, in the same PRINT or in the next. Numbered-file OUTPUT does not go through the renderer. The INPUT prompt stays on the TextWriter, it is not yet a complete raster input box (see the single-window section at the end).
- Graphics grid: width/8 columns; mode 9 = 25 rows (14 pixels), mode 11/12 = 30 (16 pixels), others 25 (8 pixels). IBM CP437 raster font from the Screen9 2.3.0 device (`PCFont`, `PCCanvas.DrawGlyph`, `ScrollUp` for the VIEW PRINT scroll). QBasicOptions.RasterFont=Auto (default): IBM EGA 8x8, EGA 8x14 and VGA 8x16 ROMs included in Screen9 2.4.0 (`PCFont.ForHeight`, CC BY-SA 4.0 licence in the Screen9 package). FontDirectory or QBS_FONT_DIR, if they contain a font for the requested height (`PCFont.Find`), take precedence; cells of a height other than 8/14/16 use the 5x7. QBasic part: uninterpreted control codes (1–27 excluding 7/9/11/12, and 127) draw the CP437 symbol; sanitised text cells `?`. Cells not 8 pixels wide or no font: QBSharp's compact 5x7 fallback (lower case as capitals, `?` for missing glyphs, Ä/Í/³/º/Û). RasterFont=Compact or CLI `QBS_RASTER_FONT=compact` forces the fallback. Characters beyond U+00FF draw `?`. Cells keep the original printable character, controls sanitised. A custom WIDTH can change the raster dimensions and is not hardware-faithful.
- VIEW PRINT top TO bottom sets an inclusive 1-based region and cursor top,1; without arguments it restores the whole grid. Scroll moves cells and pixel rows only inside the region, preserving outside rows.
- CLS/CLS 0: clears the whole framebuffer and cells, cursor 1,1, even if VIEW PRINT is restricted (a choice of the current subset). CLS 1: clears only the graphics framebuffer and keeps cells/cursor. CLS 2: clears cells and pixels of the VIEW PRINT region, cursor top,1. Current background colours; not an emulation of the full QBasic text/graphics viewport distinction. In mode 0 CLS 1 draws no graphics.
- QBasicOptions.PresentText(Action<Canvas>) receives text frames; PresentGraphics(Action<PCCanvas>) receives an indexed graphics frame. Frames are live/mutable, not snapshot/thread-safe: copy them if the host keeps them. With no text callback, PRINT stays a plain TextWriter; headless graphics keeps pixels without presentation. The interactive CLI uses the Screen9 renderer (reduced preview), redirected does not emit runtime ANSI. Spectre remains CLI report/help. (A pixel window is described in the host sections at the end.)
- Cancellation is checked after evaluating device/host arguments and during write/clear/scroll. A drawing already done can remain after cancellation, no rollback; the host callback must cooperate.

### Tests and limits of verification

QBasicTextRasterTests 32 cases: font pixels in 8 modes, overwrite, fallback glyphs, color/locate/omissions, VIEW PRINT/reset/scroll, CLS 0/1/2, invalid/source span, state after an error, wrap/TAB/cancel. QBasicTextContinuationTests 2 cases: intra-PRINT TAB and sanitised controls. QBasicRasterProcessTests runs `samples/qbsharp/qbasic-raster-test.bas` in a redirected CLI process, 10 assertions, zero runtime ANSI/files/audio. QBasicDeviceTests and GorillasGraphicsRuntimeTests verify the services and the original graphics routines without altering samples/qbsharp/gorillas.bas.

The complete suite 528/528 (0 skipped) Debug and Release, not only a QBasic filter. Gorillas with a fake clock reaches the introduction wait (explicit budget) and the routines draw sprites/city/sun; **no complete match verified**, no proof of collisions/victory/ranking end-to-end or real pixel UX/audio. Global pack/install still separate.

## 18. Interactive polling and terminal restoration — Gorillas fix

The INKEY$ wait of classic programs runs the AST repeatedly. A total budget, even 10M, can run out while the user reads the introduction. It is not a parsing or graphics error; it is not fixed by arbitrarily raising the limit or by making errors no-ops.

QBasicOptions.StepBudgetMode:
- **Total** (embedding default): MaximumSteps is the total per Execute/Evaluate, as before. No reset on polls. Redirected batch and qbs test keep this model.
- **BetweenInputPolls**: MaximumSteps limits the instructions before the first poll and between completed INKEY$ polls; the counter is reset after each poll, even if the result is empty. It serves **trusted interactive programs**, not sandboxes. CPU loops without polls stay limited. A loop that periodically calls INKEY$ can last indefinitely; use a Total budget or a host timeout for untrusted code. No automatic recognition of pure/idle loops.

QBasicOptions.InputPollingInterval default 0, range 0..1 second. INKEY$ and INKEY$() go through PollKeyAsync: a cancellable host delay before reading, cancellation also after the callback, zero arity verified. BetweenInputPolls imposes 10 ms when the interval is 0; a host with a fake Delay can simulate time in tests. The interactive CLI uses 10 ms even with an explicit Total budget. Batch/embedding Total interval 0 keeps the previous non-blocking polling. TIMER/SLEEP and the rest of the language do not reset the counter.

The CLI selects BetweenInputPolls **only** for an interactive run --dialect qbasic with neither --max-steps nor a configured QBS_MAX_STEPS. An explicit CLI/env budget stays Total; qbs test, redirected input/stdout, the modern dialect and embedding defaults unchanged. Information available in help run. ops/launch-gorillas.cs now omits the old --max-steps 10000000 and removes QBS_MAX_STEPS only in the child's environment, without modifying the user's.

An interactive QBasic run uses QBasicTerminalOutput, a CLI presenter for Screen9 Canvas/PCCanvas models, not Spectre at runtime. It enables Windows VT output when needed, alternate screen / hidden cursor / wrap disabled, 16 colours, clip to the viewport and a frame cadence of 33 ms. Pending frames updated by polls and a final flush, so a PRINT before a wait stays visible. A partial OEM box-drawing text mapping (Ä/Í/³/º/Û and corners) to Unicode; not complete CP437 or the IBM raster font.

Dispose restores style/wrap/cursor/screen and the original console mode. The CLI restores **before** printing diagnostics/Ctrl+C, avoiding an error/prompt overlapping the old screen. Only an interactive QBasic run: no new ANSI sequence in batch, no change to the global tool or to the editor/modern renderer. Terminals without alternate-screen/VT support require manual verification; live/mutable frames, no new pixel-window renderer or MB audio scheduler (see the host sections at the end).

Verification: QBasicPollingTests 15 cases (budget/pacing/host cancel/invalid/options/policy), QBasicTerminalOutputTests 6 (pending frame/cadence/clip/glyph/lifecycle/CLI error ordering) and 2 new Windows ConPTY ones. The **original** Gorillas file with UseSound=No in a temp workspace passes the instructions wait, accepts a key and reaches Enter name of new player; Ctrl+C exit 130. A second native process verifies the error after a key / restoration ordering. The complete suite **551/551 Debug and Release**, 0 skipped, build 0 warnings/errors. A retry limited to sharing errors 32/33 in the editor test to wait for the save, without changing the expected content or deadline.

A complete match, collisions/scores/persistence, real UX/audio and a global release remain to be verified. Native Windows tests do not certify Unix terminals. No network/AI/audio in the tests.

## 19. Editor linked to the QBasic dialect — source gate 564/564

`edit [file.bas] [--dialect qbs|qbasic]` explicitly selects the F5 parser/runtime. The qbs default is unchanged; no autodetection or modification of the .bas. Run -> Language / dialect offers the same choice, shown in the title bar. Selection is forbidden during run/AI/preview. An invalid value is rejected before opening the terminal. It is not a new keyword, it does not make the REPL compatible and it is not yet in the global tool 0.7.0-alpha.2.

F5 runs the **in-memory buffer**, does not require Save and creates an isolated session of the chosen dialect. QBasic uses QBasicEngine, Screen9 Canvas/PCCanvas and editor callbacks; no Console.ReadKey/native renderer from the worker. Modern keeps the previous AST session. The result includes Dialect, source snapshot, file and diagnostic; F4 goes back to the code / navigates line-column, checking a modified buffer as before. A dialect change invalidates the previous result/frames.

### Program view and input

F5 in QBasic automatically opens the program view inside the editor. **F8** toggles code/frame, also after the end of the run. In the program view, the keyboard goes to the script's INKEY$ queue and does not modify the buffer: 8-bit text, Enter/Tab/Backspace/Esc and NUL+scan-code arrows/Home/End/PgUp/PgDn/Insert/Delete. Esc is sent to the program (useful to abort the game), **F6** always interrupts; Ctrl+Q exits with dirty/cancellation protection. F1/F2/F3/F4/F5/F7/F10 remain editor commands. In the code view the keys edit normally; they are not sent to the script. No hardware input is read twice.

Wheel/Ctrl+PgUp/PgDn vertical scroll, Ctrl+left/right arrows pan; arrows without Ctrl and Tab are not stolen from the game. Menu/dialogs stay navigable with the mouse. The view is fit/crop, not pixel upscale: use a terminal of at least 80x30 to see a whole 80x25 frame plus bars. Minimum 40x12 terminals are allowed but require scrolling. ProgramSafe converts a subset of OEM box drawing to Unicode; the rest is safe-display, not complete CP437 or Unicode width.

EditorProgramFrame is a readonly copy of the Screen9 cells (max 300x100); a latest-frame slot protected by a lock, no infinite queue of frames or shared live canvas objects. Publish cadence 33 ms and the last frame forced at the end of the run; graphics frames projected 80x25 / 16 colours, not a pixel window. No output flood from every PRINT/LOCATE. Log/diagnostics stay in the output panel. Source/undo/save model separate; Spectre editor rendering, Screen9 runtime framebuffer.

INPUT/LINE INPUT console of the dialect uses the runner's TextReader and the editor's QBasic INPUT dialog (single line). A cancellable wait; Ui input/select/message keeps the existing dialog adapter. INKEY$ queue limited to 1024 events, emptied at each run; session/frame/input not inherited from previous runs. JSON/YAML/file/AI script host bindings remain available within the QBasic interop limits.

### Budget, audio and AI

The QBasic editor uses BetweenInputPolls + 10 ms pacing, a quota of **10M steps per burst**. An interactive QBasic CLI run without an explicit budget also now uses 10M between polls: the CalcDelay calibration can exceed 1M before the first poll. It is not an increase of the total budget in waits. The modern editor stays 1M total; embedding default, CLI batch/test and explicit --max-steps/QBS_MAX_STEPS budgets unchanged. No new editor max-steps flags. A program that polls can last indefinitely: trusted code only, F6/host token to interrupt.

Editor audio opt-in on Windows with QBS_QBASIC_AUDIO=1; the EditorRunner host accepts a PCTone callback. No no-op: a missing backend produces an error when a note is emitted. The Gorillas editor launcher enables audio in the child, not in the calling session. PLAY MB stays sequential in the Windows adapter, not a faithful DOS scheduler (see the background music section at the end). Offline/fake tests without sound.

**F7 code generation/modification is QBS-only**: in the QBasic dialect it shows an explanation and does not send the buffer to the cloud. The Ralf script binding stays opt-in; we do not pretend to have a complete QBasic AI guide/parser. No automatic AI save/execution.

### Verification and distribution

EditorQBasicTests 10 cases + EditorDialectRegressionTests 2, a new Windows ConPTY on the **original Gorillas file**: opening --dialect qbasic, F5, instructions, key, new player, F6 Canceled, CtrlQ exit 0 and file unchanged. The complete suite **564/564 Debug and Release**, 0 skipped, build 0 warnings/errors. The complete gate detected a CalcDelay that was too fast in the CLI run as well: a common burst quota of 10M, explicit limits tested. It is not a verification of a complete match / scores / persistence or Unix terminals.

Release built in `ops/artifacts/editor-dialect` with --artifacts-path, without closing the previous editor nor overwriting its binaries. `ops/launch-gorillas-editor.cs` prefers the isolated executable and passes --dialect qbasic; the old window stays the old version. No global pack/install/update in this gate.

## 20. Optimised rendering and polling — source gate 579/579

EditorFrameMailbox replaces the eager conversion for every statement: it keeps the last Screen9 model on the worker, applies the 33 ms cadence **before** ToCellCanvas/Capture and publishes a readonly copy into the latest slot. Refresh on polling/end of run captures pending changes; no idle republication if not dirty. GraphicsProjections/PublishedFrames statistics, Reset only with the producer stopped. The UI consumer does not read mutable canvases, only snapshots. Intermediate frames can be coalesced: the last image and the script data remain correct, no promise of every frame or of FPS measured on the terminal.

The program view does not rebuild/highlight the hidden code, the 16x16 styles are reused and Render groups adjacent segments with the same style. EditorDocument indexes lines/offsets by reference to the text: Position/MoveTo on an index/binary search, lazily invalidated by New/Open/Insert/Undo/Redo. The public Lines keeps returning an independent copy; no caller can corrupt the cache by modifying the received array. The program view UI is redrawn for changed frames/UI, about 33 ms cadence; code keeps a 100 ms fallback.

INKEY$ now checks the callback only once **before** the delay: an available key is returned at once; only an empty poll waits for the interval/token. Total/BetweenInputPolls budgets and limits are unchanged, cancellation checked also after the callback and the wait. It changes the cadence on already queued keys, not the program's timeout. It does not modify the TIMER/SLEEP pauses in the .bas or the PLAY MB audio scheduler, still sequential at that gate.

EditorPerformanceTests 15 cases verify the gate before projection, final snapshot, slot/modes/reset, dirty/idle, cached offsets / LF-CRLF-surrogate / undo and snapshot independence, Render clipping/segment semantics, refresh policy and queued-key/cancel. Complete suite 579/579 Debug/Release, 0 skipped; build 0 warnings/errors. The new QBSharp.Benchmarks in the solution, an offline utility (not the CLI package) with warmup + 3 trials and median/allocations; no fragile timing thresholds in CI.

Local benchmarks, same cases (ms / allocated bytes): headless 1000 PSet 152.08/4910584 -> 146.98/4910584; EditorRunner 1000 PSet 78.55/29278808 -> 21.57/5052352; ProgramView Build 200 frames 83.14/114924520 -> 18.35/22658600; Build+Spectre Render 200 frames 493.45/160504872 -> 478.59/27914704. A clear reduction of preparation/allocations, but the total terminal cost is almost unchanged: **not** a 4x gain for the game/FPS nor measured resident memory. Host/SDK/JIT/GC/scheduling influence it; compare the same case, not headless-vs-runner with different warmups. Raw JSON ops/artifacts/perf-before.json and perf-after.json.

### Samples detected during the gate

During verification samples/qbsharp/gorillas.bas had been replaced by an IBM 1991 fragment, ending in DI and without the complete program. Not altered/repaired by the assistant. The earlier complete Deluxe 2.2 (68891 characters) was recovered from the previous Release artifact **byte-identical** as samples/qbsharp/gorillas-deluxe.bas. The historical/ConPTY regressions are anchored to this distinct name, not to a working file that can be modified with a different header/coordinates. The earlier Gorillas attestations refer to Deluxe 2.2, not to the current fragment.

The launcher accepts an optional .bas path, default gorillas.bas preserved; for the complete version use gorillas-deluxe.bas. It prefers the isolated editor-perf-after build. No silent replacement of the user's file, no window closed or code run automatically. Validation of a complete match remains open.

## Modern consolidation — loops
In the `qbs` dialect: `For counter [As type] = start To limit [Step increment] ... Next [counter]`; a counter without As must exist, with As it is local to the loop. Start/limit/step evaluated once; finite numbers, a non-zero Step, overflow and lack of progress are errors. The body has a per-iteration scope. `Do [While|Until cond] ... Loop [While|Until cond]` supports an initial or a final condition, strictly Boolean. `Exit For`, `Exit Do`, `Exit While` reach the corresponding loop in the same function. Classes, modules and embedding remain supported; no merging of the dialects. Coverage: ModernLoopTests, ModernLoopGuardTests, ModernLoopIntegrationTests and samples/qbsharp/modern-loops.bas.

## Modern selection and iteration
Source 0.7.0-alpha.4: `ElseIf`, `Select Case` and `For Each` in the qbs dialect; classes preserved. Build/test/release gate to be reconfirmed.
- Multi-line ElseIf, Boolean conditions, evaluated only up to the first true branch; a single End If, no new scope compared with If.
- Select Case: the selector is evaluated once; Case value, comma list, inclusive range `a To b`, `Is <|<=|=|<>|>=|>` and a final Case Else. First match, no fall-through. Tests evaluated in order and short-circuit, CLR comparisons of the modern dialect (ordinal case-sensitive strings). No Exit Select.
- For Each counter [As type] In expression / Next [counter]: a synchronous CLR IEnumerable, including collections and host objects. Without As the counter must exist; with As it is local per iteration, with checked conversion. The collection is evaluated once, the enumerator is disposed on end/Exit/error/cancellation, Exit For is shared with the numeric For. No snapshot, changes to the collection follow CLR rules; no IAsyncEnumerable or pattern-only enumerables. Synchronous host calls are not pre-emptible.
- Dedicated ASTs and GuardedParser visits all the new children. The REPL recognises End Select; completion includes the keywords.
- Coverage ModernSelectionTests and samples/qbsharp/modern-selection.bas. Run `qbs test samples/qbsharp/modern-selection.bas` after updating the tool.

### Console INPUT: empty answers
In the QBasic dialect, a completed console INPUT line with an empty field assigns an empty string to a string destination, or zero to a numeric destination. A null ReadLine result remains error 62. Numbered-file EOF checks are unchanged. LINE INPUT preserves the whole line.

## Pixel host presenter (not syntax)
QBS_GRAPHICS=pixel enables a Windows window for an interactive QBasic run. It does not change coordinates, palette or language operations. The visible page is shown at 4:3, nearest-neighbour and letterboxing. Input from the window is shared with INKEY$/console INPUT. The editor is excluded. (The Screen9 `PixelWindow` can also scale by whole multiples only and is DPI aware: see docs/engine.)

### Classic interactive TIMER waits
In BetweenInputPolls, empty DO/WHILE loops with a pure arithmetic comparison based on TIMER perform a cooperative 1 ms delay. No body or arbitrary host call is optimised. The budget is renewed only if the clock advances during the delay; a stopped clock keeps the step limit. A single wait is limited to 60 real seconds and is cancellable. Batch, Total and explicit CLI/environment budgets keep the previous behaviour. The original Single precision and TIMER rollover are unchanged.

### Explicit scale of TIMER waits
QBasicOptions.TimerWaitScale (default 1, finite range 0.0001..1000) scales exclusively the threshold of pure empty loops `TIMER - start <op> duration`, including NOT/UNTIL and TIMER(). It does not change the general TIMER, clock, calibration, physics, audio nor loops with a body. A scale other than 1 intentionally alters the original durations: host opt-in / CLI QBS_TIMER_WAIT_SCALE. Total budgets stay active. `QBS_TIMER_WAIT_SCALE=auto` (and `QBasicRunPolicy.CalibratedTimerWaitScale`) measures the speed of the machine once with a CalcDelay-shaped loop and returns `500 / turns`; see OPENPOINTS.

### Double-buffered presentation
A Windows pixel window: whole composition on an off-screen GDI bitmap, then a single BitBlt, without a visible background clear. The backbuffer is reused and recreated on resize. The host RefreshDisplay is distinct from a framebuffer modification: during TIMER waits only pending frames are flushed. SCREEN page semantics and physics unchanged; it is not vsync synchronisation.

### Single window (QBS_GRAPHICS=pixel)
With `QBS_GRAPHICS=pixel` the interactive QBasic run uses a single Windows window for the whole QBasic screen: graphics (PCCanvas) and SCREEN 0 text, drawn per cell with the Screen9 VGA 8x16 ROM font and the 16 PC colours (80x25 = 640x400, shown at 4:3). The terminal presenter is not created; the console receives no program output. `QBasicOptions.ScreenInput` (default false; true in this mode): INPUT/LINE INPUT as in DOS, prompt and echo on the QBasic screen at the cursor position, keys from ReadKey (the same queue as INKEY$), Backspace also across lines without erasing the prompt, maximum 255 characters, Enter wraps to a new line. INPUT adds "? " with no prompt or after `"prompt";`, not after `"prompt",` nor for LINE INPUT. Without ScreenInput the behaviour remains: prompt on Output, line from Input. No blinking cursor is drawn. A process started hidden: the window repeats ShowWindow because the first call uses SW_HIDE from the STARTUPINFO. Final errors and diagnostics stay on stderr/console.

### Background music (PLAY "MB")
A single PC speaker buffer (`PCSpeaker`) of 32 tones, played in order on a separate task through `QBasicOptions.Audio` (the backend can therefore be called from another thread). `PLAY "MB…"`: the notes enter the buffer and the statement returns at once; with a full buffer it waits for a free place. Foreground `PLAY` (default or `MF`), `SOUND` and `BEEP`: if the buffer is empty they play directly on the interpreter thread, otherwise they queue after the current music and wait to be played. `PLAY(n)`: sounding notes still in the buffer or being played (pauses and articulation intervals excluded), 0 without music. Normal end of the program (END too): waits for the buffer to empty. Error, interruption or step limit: buffer emptied and audio stopped at once. A backend error during the music: runtime error QBS2400 at the next sound statement or at the end of the program (sanitised host message). `SOUND` does not follow `MB`. No mixing: one tone at a time, like the speaker.
