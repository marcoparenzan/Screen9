using System.Globalization;
using QBSharp.QBasic;
using Screen9;
using QBSharp.AI;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QBSharp.Core.Diagnostics;
using QBSharp.Core.Hosting;
using QBSharp.Core.Syntax;
using Spectre.Console;
using QBSharp.StandardLibrary;
using QBSharp.StandardLibrary.UI;

namespace QBSharp.Cli;

public sealed class AssertionFailure(string message) : Exception(message);
public static class CliExitCodes
{
    public const int Success = 0, TestFailed = 1, Usage = 2, Syntax = 3, Runtime = 4, Io = 5, Internal = 6, Canceled = 130;
}
public sealed class CliApplication
{
    public const string Version = "0.7.0-alpha.4";
    public const string HelpText = """
        QBSharp (qbs) — fase 7: RalfAI, editor QuickBASIC, AST e standard library
        Uso: qbs <comando> [opzioni]
          run file.bas [--max-steps N] [-- args]   Esegue uno script
          repl [--max-steps N]                    Sessione multilinea
          new [file.bas]                         Crea senza sovrascrivere
          test file.bas [--max-steps N] [-- args]  Esegue asserzioni
          complete [prefisso]                    Suggerimenti per shell
          help [comando]                         Help contestuale
          --version                              Versione
          edit [file.bas]                         Editor full-screen tastiera/mouse

        run/test/edit --dialect qbasic abilita compatibilita classica opt-in. Usa qbs <comando> --help. Ctrl+C interrompe (exit 130).
        Json/Yaml/File/Directory/Path/Config/Ui disponibili; editor disponibile; Ralf(prompt [, format]) disponibile (AI opt-in da ambiente); F7 nell'editor.
        """;
    public const string ReplHelp = """
        REPL: espressioni mostrano il risultato; Dim/assegnazioni aggiornano la sessione.
        Blocchi If/While/For/Do/Function/Sub/Class/Namespace multilinea terminano con il rispettivo End.
        :help  :quit  :vars  :reset  :cancel  :complete [prefisso]
        Riga vuota forza il parse di un blocco incompleto. :cancel svuota il buffer.
        Tab completa keyword/variabili; frecce Su/Giu richiamano le ultime 200 righe.
        Ctrl+D su riga vuota termina; Ctrl+C interrompe la CLI.
        """;
    private readonly IAnsiConsole console, errors;
    private readonly TextReader input;
    private readonly bool interactive;
    private readonly IScriptParser parser;
    private readonly IConfiguration? configuration;
    private readonly ILogger<CliApplication> logger;
    private readonly StandardLibraryHost standardLibrary;
    private readonly RalfHost ralfHost;
    private readonly TextConsole scriptConsole;
    private readonly ICanvasRenderer canvasRenderer;
    private readonly Func<QBasicTerminalOutput>? qbasicTerminalFactory;
    public CliApplication(IAnsiConsole console, IAnsiConsole? errorConsole = null, TextReader? input = null,
        bool interactive = false, IScriptParser? parser = null, IConfiguration? configuration = null, ILogger<CliApplication>? logger = null, StandardLibraryHost? standardLibrary = null, RalfHost? ralfHost = null, TextConsole? scriptConsole = null, Func<QBasicTerminalOutput>? qbasicTerminalFactory = null)
    {
        this.qbasicTerminalFactory=qbasicTerminalFactory; this.ralfHost = ralfHost ?? RalfHost.FromConfiguration(configuration);
        this.console = console; errors = errorConsole ?? console; this.input = input ?? Console.In;
        this.interactive = interactive; this.parser = parser ?? new ScriptParser(); this.configuration = configuration;
        this.logger = logger ?? NullLogger<CliApplication>.Instance;
        this.scriptConsole = scriptConsole ?? new TextConsole(console.Profile.Out.Writer, this.input, interactive);
        ICanvasRenderer renderer = OperatingSystem.IsWindows() && interactive && !Console.IsOutputRedirected ? new WindowsConsoleRenderer() : new TextCanvasRenderer(this.scriptConsole.Output, new(300, 100));
        canvasRenderer = renderer; this.standardLibrary = standardLibrary ?? new StandardLibraryHost(ui: new ConsoleLibTextUi(this.scriptConsole), renderer: renderer);
    }
    private static string Format(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    private void Output(string text) { console.Write(new LiteralOutput(text)); console.WriteLine(); }
    private void Error(string text) { errors.Write(new Text(text, new Style(Color.Red))); errors.WriteLine(); }
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        QBasicTerminalOutput? terminal=null;
        PixelWindow? pixels=null;
        long lastPng=0;
        string ReadDeviceKey()
        {
            string key=pixels?.ReadKey()??"";
            terminal?.Refresh();
            return key.Length>0?key:QBasicConsoleDevices.ReadKey();
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested(); var command = CommandLine.Parse(args);
            if (command.Help || command.Name == "help") { ShowHelp(command.Name); return 0; }
            if (command.Name == "version") { Output(Version); return 0; }
            if (command.Name == "edit")
            {
                if (!interactive || !console.Profile.Capabilities.Ansi) throw new CliUsageException("Editor requires an interactive ANSI terminal (stdin/stdout not redirected).");
                return await new Editor.EditorApplication(ralfHost, command.Dialect).RunAsync(command.File, cancellationToken);
            }
            if (command.Name == "complete")
            { foreach (var candidate in Completion.Cli(command.Prefix ?? "", Directory.GetCurrentDirectory())) Output(candidate); return 0; }
            if (command.Name == "new") { await NewAsync(command.File ?? "main.bas", cancellationToken); return 0; }
            // QBS_GRAPHICS=pixel: one window for the whole QBasic screen (text, graphics, INPUT echo); the console stays unused.
            bool window=command.Dialect=="qbasic" && command.Name=="run" && interactive && configuration?["QBS_GRAPHICS"]=="pixel";
            if(window)pixels=new PixelWindow("QBSharp — QBasic (close to stop)");
            else if(command.Dialect=="qbasic" && command.Name=="run" && interactive)
                terminal=qbasicTerminalFactory?.Invoke() ?? new QBasicTerminalOutput(scriptConsole.Output,()=>{var size=canvasRenderer.GetViewportSize();return(size.Width,size.Height);});
            IScriptEngine engine = command.Dialect == "qbasic"
                ? new QBasicEngine(new() { MaximumSteps = QBasicRunPolicy.MaximumSteps(interactive,command.Name,command.MaximumSteps,configuration?["QBS_MAX_STEPS"],command.MaximumSteps ?? ConfiguredSteps()), Output = scriptConsole.Output, Input = interactive ? new QBasicInteractiveReader(ReadDeviceKey,scriptConsole.Output,cancellationToken) : input,
                    ReadKey = interactive ? ReadDeviceKey : () => "", ScreenInput = window,
                    StepBudgetMode=QBasicRunPolicy.BudgetMode(interactive,command.Name,command.MaximumSteps,configuration?["QBS_MAX_STEPS"]),
                    InputPollingInterval=interactive?TimeSpan.FromMilliseconds(10):TimeSpan.Zero,
                    TimerWaitScale=QBasicRunPolicy.TimerWaitScale(configuration?["QBS_TIMER_WAIT_SCALE"]),
                    RefreshDisplay=interactive?()=>{pixels?.Refresh();terminal?.Refresh();}:null,
                    Audio = QBasicConsoleDevices.ConfigureAudio(configuration?["QBS_QBASIC_AUDIO"],interactive), RasterFont = QBasicConsoleDevices.ConfigureRasterFont(configuration?["QBS_RASTER_FONT"]), FontDirectory = configuration?["QBS_FONT_DIR"],
                    PresentText = interactive ? frame => {if(pixels is not null){pixels.Present(frame);return;}if(terminal is not null)terminal.Present(frame);else canvasRenderer.Render(frame);} : null,
                    PresentGraphics = interactive ? frame => {if(configuration?["QBS_FRAME_PNG"] is { Length: > 0 } pngPath && Environment.TickCount64-lastPng>=1000){FramebufferPng.Save(frame,pngPath,cancellationToken);lastPng=Environment.TickCount64;}if(configuration?["QBS_GRAPHICS"]=="pixel"){pixels??=new PixelWindow("QBSharp — QBasic (close to stop)");pixels.Present(frame);return;}var size=canvasRenderer.GetViewportSize();var preview=frame.ToCellCanvas(Math.Min(size.Width,300),Math.Min(size.Height,100));if(terminal is not null)terminal.Present(preview);else canvasRenderer.Render(preview);} : null })
                : new ScriptEngine(parser, new() { MaximumSteps = command.MaximumSteps ?? ConfiguredSteps() });
            return command.Name == "repl" ? await ReplAsync(engine, cancellationToken) : await RunFileAsync(engine, command, cancellationToken);
        }
        catch (Exception exception) { terminal?.Dispose(); return Report(exception); }
        finally { pixels?.Dispose();terminal?.Dispose(); }
    }
    private long ConfiguredSteps()
    {
        string? value = configuration?["QBS_MAX_STEPS"];
        if (value is null) return 1_000_000;
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var steps) || steps < 1) throw new CliUsageException("QBS_MAX_STEPS deve essere un intero positivo.");
        return steps;
    }
    private int Report(Exception exception)
    {
        switch (exception)
        {
            case OperationCanceledException: Error("Esecuzione interrotta."); return CliExitCodes.Canceled;
            case CliUsageException: Error(exception.Message); return CliExitCodes.Usage;
            case AssertionFailure: Error(exception.Message); return CliExitCodes.TestFailed;
            case ScriptRuntimeException runtime:
                Error(runtime.Diagnostic.ToString());
                if (runtime.InnerException is AssertionFailure) return CliExitCodes.TestFailed;
                return runtime.Diagnostic.Code.StartsWith("QBS1", StringComparison.Ordinal) ? CliExitCodes.Syntax : CliExitCodes.Runtime;
            case IOException or InvalidDataException or UnauthorizedAccessException or DecoderFallbackException: Error(exception.Message); return CliExitCodes.Io;
            case ArgumentException: Error(exception.Message); return CliExitCodes.Usage;
            default: logger.LogError(exception, "Unexpected CLI error"); Error($"Errore interno: {exception.Message}"); return CliExitCodes.Internal;
        }
    }
    private void ShowHelp(string command)
    {
        var help = command switch
        {
            "run" => "qbs run file.bas [--max-steps N] [-- args]\nUsa - per stdin. Print(...) su stdout; Args e un array String CLR.\nQBS_MAX_STEPS configura il default, opzione CLI prevale.\nQBasic run interattivo senza budget esplicito: limite tra poll INKEY$, pacing10ms. Batch/test e --max-steps mantengono budget totale.",
            "test" => "qbs test file.bas [--max-steps N] [-- args]\nAssert(Boolean [, messaggio]), AssertEqual(atteso, reale [, messaggio]).\nEquals CLR (tipi sensibili); almeno una asserzione, fail-fast; exit 1 se fallisce.",
            "new" => "qbs new [file.bas]\nDefault main.bas; UTF-8, richiede estensione .bas, non sovrascrive file esistenti.",
            "repl" => "qbs repl [--max-steps N]\n" + ReplHelp + "\nInput rediretto: niente banner/prompt; EOF restituisce il primo errore incontrato.",
            "complete" => "qbs complete [prefisso]\nComandi, opzioni, directory e file .bas (max 1000 entry esaminate). Una voce per riga.\nops/register-completion.ps1 abilita completamento PowerShell.",
            "edit" => "qbs edit [file.bas] [--dialect qbs|qbasic]\nEditor full-screen ANSI (min 40x12), tastiera/mouse. F1 help, F2 save, F3 open, F4 error, F5 run, F6 stop, F7 Ralf (AI opt-in), F10 menu. F8 toggles QBasic program view; Run menu selects dialect. Ctrl+Q exits with unsaved-change protection.",
            _ => HelpText
        };
        console.Write(new Panel(new Text(help)).Header("QBSharp").Border(BoxBorder.Double));
    }
    private async Task NewAsync(string path, CancellationToken token)
    {
        if (!Path.GetExtension(path).Equals(".bas", StringComparison.OrdinalIgnoreCase)) throw new CliUsageException("Il nuovo file deve avere estensione .bas.");
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
        await stream.WriteAsync(Encoding.UTF8.GetBytes("' QBSharp script\nDim name As String = \"QBSharp\"\nPrint(\"Hello, \" & name)\n"), token);
        Output($"Creato: {Path.GetFullPath(path)}");
    }
    private sealed class Assertions { public int Count; public bool QBasic; }
    private async ValueTask<HostBindings> BindingsAsync(string[] arguments, Assertions? assertions = null, CancellationToken token = default)
    {
        var bindings = await standardLibrary.CreateBindingsAsync(token); ralfHost.AddBindings(bindings); bindings.SetValue("Args", arguments);
        bindings.RegisterFunction("Print", (context, args) =>
        { context.CancellationToken.ThrowIfCancellationRequested(); scriptConsole.WriteLine(string.Join(" ", args.Select(Format))); return ValueTask.FromResult<object?>(null); });
        if (assertions is not null)
        {
            bindings.RegisterFunction("Assert", (context, args) =>
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (args.Count is not (1 or 2) || args[0] is not bool && !(assertions.QBasic && args[0] is short or int or long or float or double)) throw new InvalidOperationException("Assert richiede Boolean e messaggio opzionale.");
                assertions.Count++; if (!(args[0] is bool condition ? condition : Convert.ToDouble(args[0], CultureInfo.InvariantCulture) != 0)) throw new AssertionFailure(args.Count == 2 ? Format(args[1]) : "Assert fallita.");
                return ValueTask.FromResult<object?>(null);
            });
            bindings.RegisterFunction("AssertEqual", (context, args) =>
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                if (args.Count is not (2 or 3)) throw new InvalidOperationException("AssertEqual richiede due valori e messaggio opzionale.");
                assertions.Count++;
                if (!(assertions.QBasic && args[0] is short or int or long or float or double && args[1] is short or int or long or float or double ? Convert.ToDouble(args[0], CultureInfo.InvariantCulture) == Convert.ToDouble(args[1], CultureInfo.InvariantCulture) : Equals(args[0], args[1]))) throw new AssertionFailure(args.Count == 3 ? Format(args[2]) : $"AssertEqual fallita: atteso {Format(args[0])}, reale {Format(args[1])}.");
                return ValueTask.FromResult<object?>(null);
            });
        }
        return bindings;
    }
    private async Task<int> RunFileAsync(IScriptEngine engine, CliCommand command, CancellationToken token)
    {
        string file = command.File!;
        string text = file == "-" ? await input.ReadToEndAsync(token) : await File.ReadAllTextAsync(file, new UTF8Encoding(false, true), token);
        var assertions = command.Name == "test" ? new Assertions { QBasic = command.Dialect == "qbasic" } : null;
        await using var session = engine.CreateSession(await BindingsAsync(command.ScriptArguments, assertions, token));
        await session.ExecuteAsync(new(text, file == "-" ? "<stdin>" : Path.GetFullPath(file)), token);
        if (assertions is not null)
        {
            if (assertions.Count == 0) throw new AssertionFailure("Nessuna asserzione eseguita: test non valido.");
            var table = new Table().AddColumn("Script").AddColumn("Asserzioni").AddColumn("Esito");
            table.AddRow(new Text(file), new Text(assertions.Count.ToString(CultureInfo.InvariantCulture)), new Text("PASS", new Style(Color.Green))); console.Write(table);
        }
        return 0;
    }
    private async Task<int> ReplAsync(IScriptEngine engine, CancellationToken token)
    {
        var terminal = new TerminalLineInput(console); var buffer = new ReplBuffer();
        IScriptSession session = engine.CreateSession(await BindingsAsync([], token: token)); int exit = 0;
        void Remember(int code) { if (exit == 0) exit = code; }
        if (interactive) Output($"QBSharp {Version} — :help per comandi REPL");
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                string? line = interactive ? await terminal.ReadAsync(buffer.Pending, session.Variables.Keys, token) : await input.ReadLineAsync(token);
                if (line is null)
                {
                    if (buffer.Pending)
                    { try { await Submit(session, buffer.Text, token); } catch (Exception e) when (e is not OperationCanceledException) { Remember(Report(e)); } }
                    return exit;
                }
                if (line.TrimStart().StartsWith(':'))
                {
                    string[] parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    string verb = parts[0]; string prefix = parts.Length > 1 ? parts[1] : "";
                    switch (verb)
                    {
                        case ":quit": return exit;
                        case ":help": Output(ReplHelp); break;
                        case ":cancel": buffer.Clear(); break;
                        case ":reset": await session.DisposeAsync(); session = engine.CreateSession(await BindingsAsync([], token: token)); buffer.Clear(); break;
                        case ":complete": foreach (var candidate in Completion.Repl(prefix, session.Variables.Keys)) Output(candidate); break;
                        case ":vars":
                            var table = new Table().AddColumn("Nome").AddColumn("Tipo CLR").AddColumn("Valore");
                            foreach (var (name, value) in session.Variables.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) table.AddRow(new Text(name), new Text(value?.GetType().Name ?? "Nothing"), new Text(Format(value)));
                            console.Write(table); break;
                        default: Error($"Comando REPL sconosciuto: {verb}"); Remember(CliExitCodes.Usage); break;
                    }
                    continue;
                }
                try
                {
                    if (!buffer.Append(line)) continue;
                    string submission = buffer.Text; buffer.Clear(); await Submit(session, submission, token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { buffer.Clear(); Remember(Report(e)); }
            }
        }
        finally { await session.DisposeAsync(); }
    }
    private async Task Submit(IScriptSession session, string text, CancellationToken token)
    {
        var source = new ScriptSource(text, "<repl>"); var parsed = parser.ParseScript(source, token);
        if (parsed.Success && parsed.Root!.Statements is [ExpressionStatement])
        { object? result = await session.EvaluateAsync(source, token); if (result is not null) scriptConsole.WriteLine(Format(result)); }
        else await session.ExecuteAsync(source, token);
    }
}
