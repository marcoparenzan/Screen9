using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Screen9;

/// <summary>Rendering di un frame con WriteConsoleOutputW, senza sequenze ANSI o scrolling.
/// Double buffering: il canvas va in un back buffer riusato e si scrive, con una sola chiamata nativa,
/// solo il rettangolo cambiato rispetto all'ultimo frame presentato; nessuna scrittura se il frame è identico.
/// Resize, scroll della finestra e cambio di posizione o dimensione del canvas forzano un frame completo;
/// dopo scritture esterne sulla Console chiamare Invalidate. Thread-safe per serializzazione.
/// Richiede stdout collegato a una Console Windows, non rediretto.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsConsoleRenderer(ILogger<WindowsConsoleRenderer>? logger = null) : ICanvasRenderer
{
    private readonly ILogger<WindowsConsoleRenderer> log = logger ?? NullLogger<WindowsConsoleRenderer>.Instance;
    private readonly ConsoleFrameBuffer frame = new();
    private readonly Lock gate = new();

    public void Invalidate() { lock (gate) frame.Invalidate(); }

    public CanvasSize GetViewportSize()
    {
        var (_, info) = GetOutput();
        return new(info.Window.Right - info.Window.Left + 1, info.Window.Bottom - info.Window.Top + 1);
    }

    public void Render(Canvas canvas, int x = 0, int y = 0)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        var (handle, info) = GetOutput();
        int width = info.Window.Right - info.Window.Left + 1;
        int height = info.Window.Bottom - info.Window.Top + 1;
        if ((long)x + canvas.Width > width || (long)y + canvas.Height > height)
            throw new ArgumentException("Il canvas non rientra nella finestra visibile della Console.", nameof(canvas));

        lock (gate)
        {
            var target = new FrameTarget(info.Window.Left, info.Window.Top, width, height, x, y);
            if (!frame.Prepare(canvas.Cells, canvas.Width, canvas.Height, target, out var dirty))
            {
                if (log.IsEnabled(LogLevel.Trace)) log.LogTrace("Frame {Width}x{Height} invariato: nessuna scrittura.", canvas.Width, canvas.Height);
                return;
            }
            var region = new SmallRect
            {
                Left = checked((short)(info.Window.Left + x + dirty.Left)),
                Top = checked((short)(info.Window.Top + y + dirty.Top)),
                Right = checked((short)(info.Window.Left + x + dirty.Right)),
                Bottom = checked((short)(info.Window.Top + y + dirty.Bottom))
            };
            var expected = region;
            if (!Native.WriteConsoleOutputW(handle, frame.Back,
                    new Coord(checked((short)canvas.Width), checked((short)canvas.Height)),
                    new Coord((short)dirty.Left, (short)dirty.Top), ref region))
            {
                frame.Invalidate(); throw NativeError("WriteConsoleOutputW");
            }
            if (region.Left != expected.Left || region.Top != expected.Top ||
                region.Right != expected.Right || region.Bottom != expected.Bottom)
            {
                frame.Invalidate(); throw new IOException("La Console è stata ridimensionata durante il rendering. Ridisegnare il frame.");
            }
            frame.Commit();
            if (log.IsEnabled(LogLevel.Debug))
                log.LogDebug("Frame {Width}x{Height} a ({X}, {Y}): scritte {DirtyWidth}x{DirtyHeight} celle.", canvas.Width, canvas.Height, x, y, dirty.Width, dirty.Height);
        }
    }

    private static (IntPtr Handle, ScreenBufferInfo Info) GetOutput()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("È richiesta Windows.");
        if (Console.IsOutputRedirected)
            throw new InvalidOperationException("Il rendering richiede stdout collegato a una Console, non rediretto.");
        IntPtr handle = Native.GetStdHandle(-11);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            throw new InvalidOperationException("Nessun handle Console stdout disponibile.");
        if (!Native.GetConsoleScreenBufferInfo(handle, out var info))
            throw NativeError("GetConsoleScreenBufferInfo");
        return (handle, info);
    }

    private static Win32Exception NativeError(string operation)
        => new(Marshal.GetLastWin32Error(), $"Operazione Console fallita: {operation}.");

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Coord(short x, short y)
    {
        public readonly short X = x;
        public readonly short Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SmallRect { public short Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScreenBufferInfo
    {
        public Coord Size;
        public Coord CursorPosition;
        public ushort Attributes;
        public SmallRect Window;
        public Coord MaximumWindowSize;
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GetStdHandle(int standardHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetConsoleScreenBufferInfo(IntPtr output, out ScreenBufferInfo info);

        [DllImport("kernel32.dll", EntryPoint = "WriteConsoleOutputW", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool WriteConsoleOutputW(IntPtr output, [In] CharInfo[] buffer,
            Coord bufferSize, Coord bufferOrigin, ref SmallRect region);
    }
}
