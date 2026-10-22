using Screen9;
using Screen9.Demos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The demos themselves (SceneDemo, PlasmaDemo) live in Core/, shared with the website; this is the console host.
if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("This demo needs a Windows Console.");
    return 1;
}

// --smoke: animates a fixed number of frames and exits without waiting for a key (checks the native renderer).
// --vga: the PCCanvas mode 13 plasma through the truecolor AnsiFrameRenderer instead of the cell scene.
bool smoke = args.Contains("--smoke"), vga = args.Contains("--vga");

try
{
    var builder = Host.CreateApplicationBuilder(args.Where(a => a is not ("--smoke" or "--vga")).ToArray());
    // Keep logging off stdout so it does not overlap the canvas.
    builder.Logging.ClearProviders();
    builder.Services.AddWindowsConsoleCanvas();
    using var host = builder.Build();
    var renderer = host.Services.GetRequiredService<ICanvasRenderer>();
    var size = renderer.GetViewportSize();
    if (vga) return VgaDemo.Run(size, smoke);

    var scene = new SceneDemo(size.Width, size.Height, "Press any key to quit");
    renderer.Render(scene.Canvas);
    // The renderer writes only the changed rectangle (ball and counter) at each frame.
    for (int frame = 0; smoke ? frame < 180 : !Console.KeyAvailable; frame++)
    {
        scene.Step();
        renderer.Render(scene.Canvas);
        Thread.Sleep(16);
    }
    if (smoke)
    {
        // Reads the visible window back and compares it with the canvas: partial rectangles must land in the right place.
        string? mismatch = SmokeCheck.Compare(scene.Canvas);
        if (mismatch is not null) { Console.Error.WriteLine($"Smoke failed: {mismatch}"); return 2; }
    }
    else Console.ReadKey(intercept: true);
    // The renderer changes no settings; the demo only cleans up its own drawing.
    Console.Clear();
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
    System.ComponentModel.Win32Exception or IOException)
{
    Console.Error.WriteLine($"Cannot show the demo: {ex.Message}");
    return 1;
}

static class VgaDemo
{
    /// <summary>The plasma in a terminal: truecolor half blocks, double-buffered (the drawing itself is <see cref="PlasmaDemo"/>).</summary>
    public static int Run(CanvasSize viewport, bool smoke)
    {
        if (!AnsiFrameRenderer.TryEnableVirtualTerminal()) { Console.Error.WriteLine("The terminal does not support VT sequences."); return 3; }
        Console.OutputEncoding = System.Text.Encoding.UTF8; // the half block ▀ needs UTF-8
        int columns = Math.Min(viewport.Width, 320), rows = Math.Min(viewport.Height - 1, 100);
        var demo = new PlasmaDemo(); var pc = demo.Canvas; var renderer = new AnsiFrameRenderer(Console.Out);
        for (int frame = 0; smoke ? frame < 120 : !Console.KeyAvailable; frame++)
        {
            demo.Step();
            renderer.Render(pc, columns, rows);
            Thread.Sleep(16);
        }
        int result = 0;
        if (smoke)
        {
            // Reads the window back: each cell must be ▀ where its two pixels differ, a space where they coincide.
            var expected = new char[columns * rows];
            for (int row = 0; row < rows; row++) for (int column = 0; column < columns; column++)
            {
                int px = column * 320 / columns;
                var up = pc.GetRgb(pc.Point(px, row * 200 / rows)); var down = pc.GetRgb(pc.Point(px, (2 * row + 1) * 200 / (2 * rows)));
                expected[row * columns + column] = up == down ? ' ' : '▀';
            }
            string? mismatch = SmokeCheck.CompareGlyphs(expected, columns, rows);
            if (mismatch is not null) { Console.Error.WriteLine($"VGA smoke failed: {mismatch}"); result = 2; }
        }
        else Console.ReadKey(intercept: true);
        Console.Clear();
        return result;
    }
}

static class SmokeCheck
{
    public static string? Compare(Canvas canvas)
    {
        var buffer = ReadWindow(canvas.Width, canvas.Height); if (buffer is null) return "ReadConsoleOutputW";
        var cells = canvas.Snapshot();
        for (int i = 0; i < cells.Length; i++)
        {
            uint expected = cells[i].Character | (uint)((int)cells[i].Foreground | (int)cells[i].Background << 4) << 16;
            if ((buffer[i] & 0x00FF_FFFF) != expected) return $"cell ({i % canvas.Width},{i / canvas.Width}): expected 0x{expected:X8}, read 0x{buffer[i]:X8}";
        }
        return null;
    }

    public static string? CompareGlyphs(char[] expected, int columns, int rows)
    {
        var buffer = ReadWindow(columns, rows); if (buffer is null) return "ReadConsoleOutputW";
        for (int i = 0; i < expected.Length; i++)
            if ((char)(buffer[i] & 0xFFFF) != expected[i]) return $"cell ({i % columns},{i / columns}): expected U+{(int)expected[i]:X4}, read U+{buffer[i] & 0xFFFF:X4}";
        return null;
    }

    private static uint[]? ReadWindow(int columns, int rows)
    {
        IntPtr handle = GetStdHandle(-11);
        if (!GetConsoleScreenBufferInfo(handle, out var info)) return null;
        var buffer = new uint[columns * rows];
        var region = new short[] { info.WindowLeft, info.WindowTop, (short)(info.WindowLeft + columns - 1), (short)(info.WindowTop + rows - 1) };
        return ReadConsoleOutputW(handle, buffer, (int)((ushort)columns | (uint)rows << 16), 0, region) ? buffer : null;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct BufferInfo { public short SizeX, SizeY, CursorX, CursorY; public ushort Attributes; public short WindowLeft, WindowTop, WindowRight, WindowBottom, MaxX, MaxY; }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int handle);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleScreenBufferInfo(IntPtr handle, out BufferInfo info);
    // COORD passed by value as an int (X in the low 16 bits); SMALL_RECT in/out as a short[4].
    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "ReadConsoleOutputW", SetLastError = true)]
    private static extern bool ReadConsoleOutputW(IntPtr handle, [System.Runtime.InteropServices.Out] uint[] buffer, int size, int origin, [System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] short[] region);
}
