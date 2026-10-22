using System.Text;

namespace Screen9;

/// <summary>Renderer truecolor del Canvas a celle per terminali VT: ogni colore Console diventa l'RGB esatto di
/// <see cref="CgaPalette"/>, quindi l'aspetto non dipende dal tema del terminale (blu QBasic 0x0000AA, marrone 0xAA5500...).
/// Double buffering come <see cref="AnsiFrameRenderer"/>: scrive solo le celle cambiate rispetto all'ultimo frame, in una sola
/// Write racchiusa in synchronized output (CSI ?2026); un frame identico non scrive nulla. Salva e ripristina il cursore e
/// azzera i colori a fine frame. Su Windows chiamare prima <see cref="AnsiFrameRenderer.TryEnableVirtualTerminal"/>.</summary>
public sealed class AnsiCanvasRenderer(TextWriter output, Func<CanvasSize>? viewport = null) : ICanvasRenderer
{
    private readonly TextWriter output = output ?? throw new ArgumentNullException(nameof(output));
    private readonly StringBuilder frame = new(4096);
    private readonly object gate = new();
    private Cell?[] front = [];
    private (int Width, int Height, int X, int Y) target = (-1, -1, -1, -1);

    public CanvasSize GetViewportSize() => viewport?.Invoke() ?? new(Console.WindowWidth, Console.WindowHeight);

    public void Invalidate() { lock (gate) Array.Clear(front); }

    public void Render(Canvas canvas, int x = 0, int y = 0) => Write(canvas, x, y);

    /// <summary>Come <see cref="Render"/>, restituendo le celle scritte (0 se il frame coincide con quello presentato).</summary>
    public int Write(Canvas canvas, int x = 0, int y = 0)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (x is < 0 or > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(x));
        if (y is < 0 or > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(y));
        lock (gate)
        {
            int width = canvas.Width, height = canvas.Height, written = 0;
            if (target != (width, height, x, y))
            {
                target = (width, height, x, y);
                front = new Cell?[width * height];
            }
            uint foreground = uint.MaxValue, background = uint.MaxValue; int cursorRow = -1, cursorColumn = -1;
            frame.Clear();
            for (int row = 0; row < height; row++)
                for (int column = 0; column < width; column++)
                {
                    var cell = canvas[column, row]; int index = row * width + column;
                    if (front[index] == cell) continue;
                    front[index] = cell;
                    if (written++ == 0) frame.Append("\e[?2026h\e7");
                    if (cursorRow != row || cursorColumn != column) frame.Append("\e[").Append(y + row + 1).Append(';').Append(x + column + 1).Append('H');
                    uint ink = (uint)CgaPalette.Rgb(cell.Foreground).Packed, paper = (uint)CgaPalette.Rgb(cell.Background).Packed;
                    if (ink != foreground || paper != background)
                    {
                        frame.Append("\e[");
                        if (ink != foreground) { AppendColor(38, ink); foreground = ink; if (paper != background) frame.Append(';'); }
                        if (paper != background) { AppendColor(48, paper); background = paper; }
                        frame.Append('m');
                    }
                    frame.Append(cell.Character == '\0' ? ' ' : cell.Character);
                    cursorRow = row; cursorColumn = column + 1;
                }
            if (written == 0) return 0;
            frame.Append("\e[0m\e8\e[?2026l");
            output.Write(frame); output.Flush();
            return written;
        }
    }

    private void AppendColor(int selector, uint rgb)
        => frame.Append(selector).Append(";2;").Append(rgb >> 16).Append(';').Append(rgb >> 8 & 0xFF).Append(';').Append(rgb & 0xFF);
}
