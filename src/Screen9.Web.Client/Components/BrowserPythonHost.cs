using PySharpLib;

namespace Screen9.Web.Client.Components;

/// <summary>The devices a Python game gets in the browser, as the <c>web_host</c> variable of <c>gorillas.py</c>: the same
/// contract as the server's <c>PythonGameHost</c> (<c>present</c>, <c>read_key</c>, <c>flush_keys</c>, <c>sleep</c>,
/// <c>tone</c>, <c>background</c>, <c>seed</c>, <c>checkpoint</c>), so the game file is identical. The difference is the
/// waits: <c>sleep</c> and <c>tone</c> return a <see cref="Task"/>, which <c>PyEngine.RunAsync</c> awaits where the
/// server's host blocks a thread — the browser has only one. Method names are the Python-side names on purpose:
/// PySharp binds .NET members by name.</summary>
#pragma warning disable IDE1006 // snake_case: this is a Python-facing API
public sealed class BrowserPythonHost(PixelScreen screen, CancellationToken token)
{
    public void present(PCCanvas? graphics, Canvas cells)
    {
        if (graphics is not null) screen.PresentGraphics(graphics); else screen.PresentText(cells);
    }

    /// <summary>INKEY$: "" when no key is waiting.</summary>
    public string read_key()
    {
        token.ThrowIfCancellationRequested();
        return screen.ReadKey();
    }

    public void flush_keys() => screen.FlushKeys();

    public Task sleep(double seconds) => GameClock.Delay(TimeSpan.FromSeconds(seconds), token).AsTask();

    /// <summary>Paces the game for the tone's duration, as PCSpeaker.Beep does on the desktop; the browser plays the sound meanwhile.</summary>
    public Task tone(PCTone tone)
    {
        screen.RequestTone(tone.Frequency, tone.Duration.TotalMilliseconds);
        return GameClock.Delay(tone.Duration, token).AsTask();
    }

    /// <summary>PLAY "MB...": returns at once, the notes play in sequence meanwhile.</summary>
    public void background(PCTone[] tones)
        => _ = GameClock.PlayBackground(tones, tone => screen.RequestTone(tone.Frequency, tone.Duration.TotalMilliseconds), token);

    public int seed() => Random.Shared.Next();

    public void checkpoint(string name, Canvas cells) { }
}
#pragma warning restore IDE1006

/// <summary>Runs a Python source on PySharp inside the browser (<c>PyEngine.RunAsync</c>: on the page's own thread, waiting
/// where the host's tasks wait) with a <see cref="BrowserPythonHost"/> as <c>web_host</c> and an in-memory <c>screen9</c>
/// module over the engine's assembly — the same names the installable <c>screen9</c> wheel gives <c>pysharp run</c>.</summary>
public static class BrowserPython
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

    /// <summary>Returns "Completed", "Canceled" or the Python error. Python output goes to <paramref name="output"/>, one line per call.</summary>
    public static async Task<string> RunAsync(string source, string fileName, BrowserPythonHost host, Action<string> output)
    {
        var writer = new LineWriter(output);
        var engine = new PyEngine(writer);
        engine.Importer.RegisterSourceModule("screen9", Screen9Module);
        engine.SetVariable("web_host", host);
        try { await engine.RunAsync(source, fileName); writer.Flush(); return "Completed"; }
        catch (PySharpLib.Runtime.PyRaise e) when (e.Value.Class.Name is "KeyboardInterrupt" or "SystemExit") { writer.Flush(); return "Canceled"; }
        catch (PySharpLib.Runtime.PyRaise e)
        {
            writer.Flush();
            string message = PySharpLib.Runtime.PyErr.FormatForClr(e.Value);
            output(message);
            return "Error: " + message;
        }
    }

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
