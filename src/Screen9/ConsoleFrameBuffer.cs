using System.Runtime.InteropServices;

namespace Screen9;

/// <summary>Cella nativa Win32 (CHAR_INFO): carattere UTF-16 e attributi a 16 colori.</summary>
[StructLayout(LayoutKind.Explicit, Size = 4)]
internal struct CharInfo
{
    [FieldOffset(0)] public ushort UnicodeChar;
    [FieldOffset(2)] public ushort Attributes;

    // default(Cell) viene presentata come spazio nero.
    public static CharInfo Encode(Cell cell) => new()
    {
        UnicodeChar = cell.Character == '\0' ? ' ' : cell.Character,
        Attributes = (ushort)((int)cell.Foreground | ((int)cell.Background << 4))
    };
}

/// <summary>Rettangolo di celle con estremi inclusi, relativo al canvas.</summary>
internal readonly record struct FrameRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left + 1;
    public int Height => Bottom - Top + 1;
}

/// <summary>Dove è stato presentato un frame: finestra visibile e offset del canvas. Se cambia, il front buffer non è più affidabile.</summary>
internal readonly record struct FrameTarget(int WindowLeft, int WindowTop, int WindowWidth, int WindowHeight, int X, int Y);

/// <summary>Front/back buffer del renderer. Il back buffer è riusato tra i frame; il diff con il frame presentato
/// produce un solo rettangolo, così ogni frame resta una singola scrittura nativa.</summary>
internal sealed class ConsoleFrameBuffer
{
    private CharInfo[] front = [], back = [];
    private FrameTarget target;
    private bool valid;
    public int Width { get; private set; }
    public int Height { get; private set; }
    public CharInfo[] Back => back;

    /// <summary>Dimentica il frame presentato: il prossimo Prepare restituisce l'intero canvas.</summary>
    public void Invalidate() => valid = false;

    /// <summary>Converte le celle nel back buffer e calcola il rettangolo cambiato.</summary>
    /// <returns>False se il frame coincide con quello presentato: non serve scrivere nulla.</returns>
    public bool Prepare(ReadOnlySpan<Cell> cells, int width, int height, FrameTarget target, out FrameRect dirty)
    {
        if (cells.Length != checked(width * height)) throw new ArgumentException("Cell count does not match frame size.", nameof(cells));
        if (width != Width || height != Height)
        {
            front = new CharInfo[cells.Length]; back = new CharInfo[cells.Length];
            Width = width; Height = height; valid = false;
        }
        if (target != this.target) { this.target = target; valid = false; }
        for (int i = 0; i < cells.Length; i++) back[i] = CharInfo.Encode(cells[i]);
        if (!valid) { dirty = new(0, 0, width - 1, height - 1); return true; }

        ReadOnlySpan<uint> presented = MemoryMarshal.Cast<CharInfo, uint>(front), next = MemoryMarshal.Cast<CharInfo, uint>(back);
        int left = int.MaxValue, right = -1, top = -1, bottom = -1;
        for (int row = 0; row < height; row++)
        {
            var before = presented.Slice(row * width, width); var after = next.Slice(row * width, width);
            int first = before.CommonPrefixLength(after); if (first == width) continue;
            int last = width - 1; while (before[last] == after[last]) last--;
            left = Math.Min(left, first); right = Math.Max(right, last);
            if (top < 0) top = row; bottom = row;
        }
        dirty = top < 0 ? default : new(left, top, right, bottom);
        return top >= 0;
    }

    /// <summary>Il back buffer è stato scritto: diventa il frame presentato.</summary>
    public void Commit() { (front, back) = (back, front); valid = true; }
}
