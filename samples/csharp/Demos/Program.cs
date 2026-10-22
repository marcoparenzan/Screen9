using Screen9;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Questa demo richiede una Console Windows.");
    return 1;
}

// --smoke: anima un numero fisso di frame ed esce senza attendere tasti (verifica del renderer nativo).
// --vga: scena PCCanvas modo 13 con AnsiFrameRenderer truecolor invece della scena a celle.
bool smoke = args.Contains("--smoke"), vga = args.Contains("--vga");

try
{
    var builder = Host.CreateApplicationBuilder(args.Where(a => a is not ("--smoke" or "--vga")).ToArray());
    // Evitare che il logging su stdout si sovrapponga al canvas.
    builder.Logging.ClearProviders();
    builder.Services.AddWindowsConsoleCanvas();
    using var host = builder.Build();
    var renderer = host.Services.GetRequiredService<ICanvasRenderer>();
    var size = renderer.GetViewportSize();
    if (vga) return VgaDemo.Run(size, smoke);
    var canvas = new Canvas(size.Width, size.Height);
    canvas.Clear(new Cell(' ', ConsoleColor.White, ConsoleColor.DarkBlue));
    canvas.DrawRectangle(0, 0, canvas.Width, canvas.Height, new Cell('#', ConsoleColor.Cyan, ConsoleColor.DarkBlue));
    canvas.DrawText(2, 2, "Screen9 - Canvas XY\nOrigine (0,0) in alto a sinistra", ConsoleColor.White, ConsoleColor.DarkBlue);
    canvas.FillRectangle(4, 6, 18, 4, new Cell(' ', ConsoleColor.Black, ConsoleColor.DarkCyan));
    canvas.DrawText(5, 7, "Un rettangolo", ConsoleColor.White, ConsoleColor.DarkCyan);
    canvas.DrawLine(2, 12, 35, 17, new Cell('*', ConsoleColor.Yellow, ConsoleColor.DarkBlue));
    canvas.DrawText(2, canvas.Height - 2, "Premi un tasto per uscire", ConsoleColor.Yellow, ConsoleColor.DarkBlue);
    renderer.Render(canvas);

    // Animazione: lo sfondo è catturato una volta e ridisegnato a ogni frame;
    // il renderer scrive solo il rettangolo cambiato (palla e contatore).
    var background = canvas.Capture(0, 0, canvas.Width, canvas.Height);
    var ball = new Cell('@', ConsoleColor.Yellow, ConsoleColor.DarkBlue);
    double x = 3, y = 3, dx = 0.7, dy = 0.4;
    for (int frame = 0; smoke ? frame < 180 : !Console.KeyAvailable; frame++)
    {
        canvas.Blit(background, 0, 0);
        x += dx; y += dy;
        if (x < 1 || x > canvas.Width - 2) { dx = -dx; x = Math.Clamp(x, 1, canvas.Width - 2); }
        if (y < 1 || y > canvas.Height - 2) { dy = -dy; y = Math.Clamp(y, 1, canvas.Height - 2); }
        canvas.SetPixel((int)x, (int)y, ball);
        canvas.DrawText(canvas.Width - 16, 1, $"frame {frame,6}", ConsoleColor.White, ConsoleColor.DarkBlue);
        renderer.Render(canvas);
        Thread.Sleep(16);
    }
    if (smoke)
    {
        // Rilegge la finestra visibile e la confronta con il canvas: verifica che i rettangoli parziali finiscano nel punto giusto.
        string? mismatch = SmokeCheck.Compare(canvas);
        if (mismatch is not null) { Console.Error.WriteLine($"Smoke fallito: {mismatch}"); return 2; }
    }
    else Console.ReadKey(intercept: true);
    // Il renderer non modifica le impostazioni; la demo ripulisce solo il proprio disegno.
    Console.Clear();
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or
    System.ComponentModel.Win32Exception or IOException)
{
    Console.Error.WriteLine($"Impossibile mostrare la demo: {ex.Message}");
    return 1;
}

static class VgaDemo
{
    /// <summary>Plasma a 240 colori con ciclo di palette e una palla, in double buffering (ManualPresent) e truecolor a mezzo blocco.</summary>
    public static int Run(CanvasSize viewport, bool smoke)
    {
        if (!AnsiFrameRenderer.TryEnableVirtualTerminal()) { Console.Error.WriteLine("Il terminale non supporta sequenze VT."); return 3; }
        Console.OutputEncoding = System.Text.Encoding.UTF8; // il mezzo blocco ▀ richiede UTF-8
        int columns = Math.Min(viewport.Width, 320), rows = Math.Min(viewport.Height - 1, 100);
        var pc = new PCCanvas(13) { ManualPresent = true }; var renderer = new AnsiFrameRenderer(Console.Out);
        var plasma = new byte[320 * 200];
        for (int y = 0; y < 200; y++) for (int x = 0; x < 320; x++)
            plasma[y * 320 + x] = (byte)(16 + (int)(120 + 40 * Math.Sin(x / 16.0) + 40 * Math.Sin(y / 12.0) + 40 * Math.Sin((x + y) / 24.0)) % 240);
        // Sprite disegnata nella pagina e catturata; l'indice 0 è trasparente in PutImage.
        pc.Ellipse(12, 12, 11, 11, 15, true); pc.Ellipse(9, 9, 3, 3, 14, true); var ball = pc.GetImage(0, 0, 24, 24);
        double bx = 40, by = 30, dx = 2.3, dy = 1.7;
        for (int frame = 0; smoke ? frame < 120 : !Console.KeyAvailable; frame++)
        {
            for (int i = 0; i < plasma.Length; i++) pc.PSet(i % 320, i / 320, plasma[i]);
            for (int i = 0; i < 240; i++)
            {
                double hue = (i + frame * 2) % 240 / 40.0; int sector = (int)hue; int ramp = (int)((hue - sector) * 63);
                var (r, g, b) = sector switch { 0 => (63, ramp, 0), 1 => (63 - ramp, 63, 0), 2 => (0, 63, ramp), 3 => (0, 63 - ramp, 63), 4 => (ramp, 0, 63), _ => (63, 0, 63 - ramp) };
                pc.SetDac(16 + i, r, g, b);
            }
            bx += dx; by += dy;
            if (bx < 0 || bx > 296) { dx = -dx; bx = Math.Clamp(bx, 0, 296); }
            if (by < 0 || by > 176) { dy = -dy; by = Math.Clamp(by, 0, 176); }
            pc.PutImage(ball, (int)bx, (int)by, transparent: 0);
            pc.Present(); renderer.Render(pc, columns, rows);
            Thread.Sleep(16);
        }
        int result = 0;
        if (smoke)
        {
            // Rilegge la finestra: ogni cella deve essere ▀ dove i due pixel differiscono, spazio dove coincidono.
            var expected = new char[columns * rows];
            for (int row = 0; row < rows; row++) for (int column = 0; column < columns; column++)
            {
                int px = column * 320 / columns;
                var up = pc.GetRgb(pc.Point(px, row * 200 / rows)); var down = pc.GetRgb(pc.Point(px, (2 * row + 1) * 200 / (2 * rows)));
                expected[row * columns + column] = up == down ? ' ' : '▀';
            }
            string? mismatch = SmokeCheck.CompareGlyphs(expected, columns, rows);
            if (mismatch is not null) { Console.Error.WriteLine($"Smoke VGA fallito: {mismatch}"); result = 2; }
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
            if ((buffer[i] & 0x00FF_FFFF) != expected) return $"cella ({i % canvas.Width},{i / canvas.Width}): attesa 0x{expected:X8}, letta 0x{buffer[i]:X8}";
        }
        return null;
    }

    public static string? CompareGlyphs(char[] expected, int columns, int rows)
    {
        var buffer = ReadWindow(columns, rows); if (buffer is null) return "ReadConsoleOutputW";
        for (int i = 0; i < expected.Length; i++)
            if ((char)(buffer[i] & 0xFFFF) != expected[i]) return $"cella ({i % columns},{i / columns}): attesa U+{(int)expected[i]:X4}, letta U+{buffer[i] & 0xFFFF:X4}";
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
    // COORD passati per valore come int (X nei 16 bit bassi); SMALL_RECT in/out come short[4].
    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "ReadConsoleOutputW", SetLastError = true)]
    private static extern bool ReadConsoleOutputW(IntPtr handle, [System.Runtime.InteropServices.Out] uint[] buffer, int size, int origin, [System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] short[] region);
}
