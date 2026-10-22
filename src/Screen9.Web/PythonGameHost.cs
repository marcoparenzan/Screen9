using PySharpLib;
using PySharpLib.Runtime;

namespace Screen9.Web;

/// <summary>The devices a Python game gets from the website, as the <c>web_host</c> variable of
/// <c>gorillas.py</c>: the same contract as that script's own WindowHost/ScriptHost (<c>present</c>,
/// <c>read_key</c>, <c>flush_keys</c>, <c>sleep</c>, <c>tone</c>, <c>background</c>, <c>seed</c>,
/// <c>checkpoint</c>), so the game file is identical on the desktop and here. Method names are the
/// Python-side names on purpose: PySharp binds .NET members by name.</summary>
#pragma warning disable IDE1006 // snake_case: this is a Python-facing API
public sealed class PythonGameHost(IPixelDisplay display, Action<PCTone> playTone, CancellationToken token)
{
    public void present(PCCanvas? graphics, Canvas cells)
    {
        if (graphics is not null) display.Present(graphics); else display.Present(cells);
    }

    /// <summary>INKEY$: "" when no key is waiting.</summary>
    public string read_key()
    {
        token.ThrowIfCancellationRequested();
        return display.ReadKey();
    }

    public void flush_keys() { while (display.ReadKey().Length > 0) { } }

    public void sleep(double seconds) => Sleep(TimeSpan.FromSeconds(seconds));

    /// <summary>Blocks for the tone's duration, as PCSpeaker.Beep does on the desktop (game pacing depends on it);
    /// the browser plays the sound meanwhile.</summary>
    public void tone(PCTone tone) { playTone(tone); Sleep(tone.Duration); }

    /// <summary>PLAY "MB...": returns at once, the notes play in sequence meanwhile.</summary>
    public void background(PCTone[] tones) => _ = Task.Run(async () =>
    {
        foreach (var tone in tones)
        {
            token.ThrowIfCancellationRequested();
            playTone(tone);
            await Task.Delay(tone.Duration, token);
        }
    }, token);

    public int seed() => Random.Shared.Next();

    public void checkpoint(string name, Canvas cells) { }

    private void Sleep(TimeSpan delay)
    {
        long end = Environment.TickCount64 + (long)delay.TotalMilliseconds;
        do
        {
            token.ThrowIfCancellationRequested();
            display.Refresh();
            Thread.Sleep((int)Math.Clamp(end - Environment.TickCount64, 0, 15));
        } while (Environment.TickCount64 < end);
    }
}
#pragma warning restore IDE1006

/// <summary>Runs a Python source on PySharp (embedded in this process) with a <see cref="PythonGameHost"/> as
/// <c>web_host</c> and an in-memory <c>screen9</c> module over the engine's own assembly — the same names
/// the installable <c>screen9</c> wheel gives <c>pysharp run</c>, here without any package to install.</summary>
public static class PythonGames
{
    private const string Screen9Module = """
        import clr
        clr.AddReference("Screen9")
        from Screen9 import AnsiCanvasRenderer, AnsiFrameRenderer, Canvas, Cell, CgaPalette, PCCanvas, PCFont, PCImage
        from Screen9 import PCKeyboard, PCMusic, PCSpeaker, PCSprite, PCSpriteOperation, PCTextDisplay, PCTone, PCVideoMode
        from Screen9 import PixelWindow
        from System import Array, Int32

        def long_array(length):
            return Array.CreateInstance(clr.GetClrType(Int32), length)
        """;

    /// <summary>Runs on a worker thread. Python output goes to <paramref name="output"/> (one line per call).
    /// Returns "Completed", "Canceled" or the Python error.</summary>
    public static Task<string> RunAsync(string source, string fileName, PythonGameHost host, Action<string> output) => Task.Run(() =>
    {
        var writer = new LineWriter(output);
        var engine = new PyEngine(writer);
        engine.Importer.RegisterSourceModule("screen9", Screen9Module);
        engine.SetVariable("web_host", host);
        try { engine.Run(source, fileName); writer.Flush(); return "Completed"; }
        catch (PyRaise e) when (e.Value.Class.Name is "KeyboardInterrupt" or "SystemExit") { writer.Flush(); return "Canceled"; }
        catch (PyRaise e)
        {
            writer.Flush();
            string message = PyErr.FormatForClr(e.Value);
            output(message);
            return "Error: " + message;
        }
    });

    private sealed class LineWriter(Action<string> emit) : TextWriter
    {
        private readonly System.Text.StringBuilder pending = new();
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void Write(char value)
        {
            if (value == '\n') { emit(pending.ToString()); pending.Clear(); }
            else if (value != '\r') pending.Append(value);
        }
        public override void Flush() { if (pending.Length > 0) { emit(pending.ToString()); pending.Clear(); } }
    }
}
