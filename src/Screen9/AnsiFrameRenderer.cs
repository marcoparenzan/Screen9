using System.Runtime.InteropServices;
using System.Text;

namespace Screen9;

/// <summary>Renderer truecolor per PCCanvas su terminali VT (Windows Terminal, conhost con VT attivo, Linux, macOS).
/// Ogni cella mostra due pixel verticali con il mezzo blocco ▀ (primo piano = pixel sopra, sfondo = pixel sotto),
/// campionati nearest-neighbor dal frame visibile (VisualPage, o l'ultimo Present se ManualPresent).
/// Double buffering: scrive solo le celle cambiate rispetto all'ultimo frame, in una sola Write racchiusa in
/// synchronized output (CSI ?2026); un frame identico non scrive nulla. Salva e ripristina il cursore e azzera i colori
/// a fine frame. Dopo scritture esterne sul terminale chiamare Invalidate. Non thread-safe.</summary>
public sealed class AnsiFrameRenderer(TextWriter output)
{
    public const int MaxColumns = 1000, MaxRows = 500;
    private const uint Unknown = uint.MaxValue; // mai uguale a un RGB a 24 bit
    private readonly TextWriter output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly StringBuilder frame = new(4096);
    private uint[] top = [], bottom = [];
    private (int Columns, int Rows, int X, int Y) target = (-1, -1, -1, -1);

    /// <summary>Dimentica il frame presentato: il prossimo Render riscrive tutte le celle.</summary>
    public void Invalidate() { Array.Fill(top, Unknown); Array.Fill(bottom, Unknown); }

    /// <summary>Scrive il frame visibile di canvas in columns × rows celle, con origine (x, y) relativa al viewport del terminale.</summary>
    /// <returns>Numero di celle scritte; 0 se il frame coincide con quello presentato.</returns>
    public int Render(PCCanvas canvas, int columns, int rows, int x = 0, int y = 0, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (columns is < 1 or > MaxColumns) throw new ArgumentOutOfRangeException(nameof(columns));
        if (rows is < 1 or > MaxRows) throw new ArgumentOutOfRangeException(nameof(rows));
        if (x is < 0 or > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(x));
        if (y is < 0 or > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(y));
        token.ThrowIfCancellationRequested();
        if (target != (columns, rows, x, y))
        {
            target = (columns, rows, x, y);
            if (top.Length != columns * rows) { top = new uint[columns * rows]; bottom = new uint[columns * rows]; }
            Invalidate();
        }

        ReadOnlySpan<byte> pixels = canvas.VisiblePixels; ReadOnlySpan<PCRgb> palette = canvas.VisiblePalette;
        int width = canvas.Width, height = canvas.Height, written = 0;
        uint foreground = Unknown, background = Unknown; int cursorRow = -1, cursorColumn = -1;
        frame.Clear();
        for (int row = 0; row < rows; row++)
        {
            token.ThrowIfCancellationRequested();
            int upper = 2 * row * height / (2 * rows) * width, lower = (2 * row + 1) * height / (2 * rows) * width;
            for (int column = 0; column < columns; column++)
            {
                int sample = column * width / columns, cell = row * columns + column;
                uint up = (uint)palette[pixels[upper + sample]].Packed, down = (uint)palette[pixels[lower + sample]].Packed;
                if (top[cell] == up && bottom[cell] == down) continue;
                top[cell] = up; bottom[cell] = down;
                if (written++ == 0) frame.Append("\e[?2026h\e7");
                if (cursorRow != row || cursorColumn != column) frame.Append("\e[").Append(y + row + 1).Append(';').Append(x + column + 1).Append('H');
                bool half = up != down;
                if ((half && up != foreground) || down != background)
                {
                    frame.Append("\e[");
                    if (half && up != foreground) { AppendColor(38, up); foreground = up; if (down != background) frame.Append(';'); }
                    if (down != background) { AppendColor(48, down); background = down; }
                    frame.Append('m');
                }
                frame.Append(half ? '▀' : ' ');
                cursorRow = row; cursorColumn = column + 1;
            }
        }
        if (written == 0) return 0;
        frame.Append("\e[0m\e8\e[?2026l");
        output.Write(frame); output.Flush();
        return written;
    }

    private void AppendColor(int selector, uint rgb)
        => frame.Append(selector).Append(";2;").Append(rgb >> 16).Append(';').Append(rgb >> 8 & 0xFF).Append(';').Append(rgb & 0xFF);

    /// <summary>Su Windows attiva l'elaborazione VT dello stdout (stato della Console, da chiamare esplicitamente).
    /// Altrove restituisce true. False se stdout è rediretto o la Console non supporta VT.</summary>
    public static bool TryEnableVirtualTerminal()
    {
        if (!OperatingSystem.IsWindows()) return true;
        if (Console.IsOutputRedirected) return false;
        IntPtr handle = Native.GetStdHandle(-11);
        if (!Native.GetConsoleMode(handle, out uint mode)) return false;
        const uint VirtualTerminalProcessing = 0x0004;
        return (mode & VirtualTerminalProcessing) != 0 || Native.SetConsoleMode(handle, mode | VirtualTerminalProcessing);
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr GetStdHandle(int standardHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetConsoleMode(IntPtr handle, out uint mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetConsoleMode(IntPtr handle, uint mode);
    }
}
