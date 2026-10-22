using QBSharp.AI;
using Screen9;

namespace QBSharp.Cli.Editor;

public interface IEditorInputSource : IDisposable { EditorInput? Poll(); }
public interface IEditorScreen : IDisposable
{
    int Width { get; }
    int Height { get; }
    void Draw(EditorFrame frame);
}

/// <summary>Full-screen VT terminal (VT mode enabled by EditorTerminal) drawn by Screen9 AnsiCanvasRenderer: exact CGA colors in truecolor, only changed cells written.</summary>
public sealed class TerminalEditorScreen : IEditorScreen
{
    private readonly AnsiCanvasRenderer renderer = new(Console.Out);
    private bool disposed;
    public int Width => Console.WindowWidth;
    public int Height => Console.WindowHeight;
    public TerminalEditorScreen()
    {
        Console.Out.Write("\u001b[?1049h\u001b[?25l\u001b[2J\u001b[H");
        if (!OperatingSystem.IsWindows()) Console.Out.Write("\u001b[?1000h\u001b[?1006h");
    }
    public void Draw(EditorFrame frame)
        => renderer.Render(frame.ToCanvas());
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        Console.Out.Write("\u001b[?1000l\u001b[?1006l\u001b[0m\u001b[?25h\u001b[?1049l"); Console.Out.Flush();
    }
}

/// <summary>Injectable full-screen lifecycle; no terminal mode change before validation/open succeeds.</summary>
public sealed class EditorApplication(Func<IEditorInputSource> inputFactory, Func<IEditorScreen> screenFactory, RalfHost? ralfHost = null, string dialect="qbs")
{
    public EditorApplication(RalfHost? ralfHost = null, string dialect="qbs") : this(() => new EditorTerminal(), () => new TerminalEditorScreen(), ralfHost, dialect) { }
    /// <summary>Where QBasic programs run on F5. Default: a PixelWindow on Windows, unless QBS_GRAPHICS=terminal
    /// keeps them in the editor's program view (null). A web host passes its own browser display.</summary>
    public Func<IPixelDisplay>? ProgramWindow { get; init; } =
        OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("QBS_GRAPHICS") != "terminal" ? () => new PixelWindow("QBSharp — QBasic (close to stop)") : null;
    /// <summary>PC speaker backend for PLAY/BEEP/SOUND. Default: Console.Beep on Windows, QBS_QBASIC_AUDIO=mute for silence.</summary>
    public Func<PCTone, CancellationToken, ValueTask> Audio { get; init; } =
        QBasicConsoleDevices.ConfigureEditorAudio(Environment.GetEnvironmentVariable("QBS_QBASIC_AUDIO"));
    /// <summary>Scale for pure TIMER wait loops (QBasicOptions.TimerWaitScale): QBS_TIMER_WAIT_SCALE, a number or "auto".</summary>
    public double TimerWaitScale { get; init; } = QBasicRunPolicy.TimerWaitScale(Environment.GetEnvironmentVariable("QBS_TIMER_WAIT_SCALE"));
    /// <summary>F7 Ralf. False leaves the editor without an AI workflow (F7 says it isn't configured here).</summary>
    public bool EnableAi { get; init; } = true;
    public async Task<int> RunAsync(string? path, CancellationToken token = default)
    {
        if(dialect is not("qbs" or "qbasic"))throw new ArgumentException("Dialect must be qbs or qbasic.",nameof(dialect));
        var document = new EditorDocument();
        if (path is not null)
        {
            path = EditorDocument.ValidatePath(path);
            if (File.Exists(path)) await document.OpenAsync(path, token); else document.NameNew(path);
        }
        token.ThrowIfCancellationRequested();
        using var input = inputFactory();
        using var screen = screenFactory();
        var aiHost = ralfHost ?? RalfHost.FromConfiguration();
        await using var runner = new EditorRunner(aiHost, Audio, ProgramWindow, TimerWaitScale);
        await using EditorAiWorkflow? ai = EnableAi ? new EditorAiWorkflow(aiHost.Service) : null;
        var controller = new EditorController(document, runner, ai, dialect);
        long lastFrame = 0;
        EditorProgramFrame? displayedProgram=null;
        EditorDialog? displayedDialog=null;
        bool displayedView=false;
        string? displayedStatus=null;
        while (!controller.Exit)
        {
            token.ThrowIfCancellationRequested();
            controller.Tick(); await controller.FlushOpenAsync();
            bool sizeOk = screen.Width >= 40 && screen.Height >= 12;
            bool resized=sizeOk && (controller.Width != screen.Width || controller.Height != screen.Height); if(resized)controller.Resize(screen.Width,screen.Height);
            var item = input.Poll();
            if (item is not null && (sizeOk || item.Key == EditorKey.Escape)) await controller.HandleAsync(item);
            bool uiChanged=item is not null || resized || displayedView!=controller.ProgramView || !ReferenceEquals(displayedDialog,controller.Dialog) || displayedStatus!=controller.Status; if(sizeOk && EditorRefreshPolicy.ShouldDraw(controller.ProgramView,!ReferenceEquals(displayedProgram,controller.ProgramFrame),uiChanged,Environment.TickCount64-lastFrame))
            { screen.Draw(EditorFrame.Build(controller)); lastFrame=Environment.TickCount64;displayedProgram=controller.ProgramFrame;displayedDialog=controller.Dialog;displayedView=controller.ProgramView;displayedStatus=controller.Status; }
            await Task.Delay(10, token);
        }
        return 0;
    }
}
