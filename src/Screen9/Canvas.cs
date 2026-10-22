namespace Screen9;

/// <summary>Canvas in memoria. Origine in alto a sinistra, X verso destra, Y verso il basso.
/// Le primitive ritagliano alle dimensioni del canvas; l'indicizzatore è rigoroso.
/// Non thread-safe.</summary>
public partial class Canvas
{
    private readonly Cell[] cells;
    public int Width { get; }
    public int Height { get; }

    public Canvas(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Width = width;
        Height = height;
        cells = new Cell[checked(width * height)];
        Array.Fill(cells, Cell.Empty);
    }

    public Cell this[int x, int y]
    {
        get => cells[Index(x, y)];
        set => cells[Index(x, y)] = value;
    }

    private int Index(int x, int y)
    {
        if ((uint)x >= (uint)Width) throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y));
        return y * Width + x;
    }

    public void Clear(Cell? cell = null) => Array.Fill(cells, cell ?? Cell.Empty);

    /// <returns>False se il punto è fuori dal canvas.</returns>
    public bool SetPixel(int x, int y, Cell cell)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return false;
        cells[y * Width + x] = cell;
        return true;
    }

    /// <summary>Testo con LF o CRLF. Ogni riga riparte dalla X iniziale.
    /// Niente wrapping; tab, controlli e surrogate non sono supportati.</summary>
    public void DrawText(int x, int y, string text, ConsoleColor foreground = ConsoleColor.Gray,
        ConsoleColor background = ConsoleColor.Black)
    {
        ArgumentNullException.ThrowIfNull(text);
        // Validare tutto prima di modificare il canvas.
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n') continue;
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') continue;
            _ = new Cell(c, foreground, background);
        }
        _ = new Cell(' ', foreground, background);
        long column = x, row = y;
        foreach (char c in text)
        {
            if (c == '\r') continue;
            if (c == '\n') { column = x; row++; continue; }
            if (column >= 0 && column < Width && row >= 0 && row < Height)
                SetPixel((int)column, (int)row, new Cell(c, foreground, background));
            column++;
        }
    }

    public void FillRectangle(int x, int y, int width, int height, Cell cell)
    {
        var plot = new CellPlotter(this, cell); Raster.FillRectangle(Width, Height, x, y, width, height, ref plot);
    }

    public void DrawRectangle(int x, int y, int width, int height, Cell cell)
    {
        var plot = new CellPlotter(this, cell); Raster.Rectangle(Width, Height, x, y, width, height, ref plot);
    }

    public void DrawLine(int x1, int y1, int x2, int y2, Cell cell)
    {
        var plot = new CellPlotter(this, cell); Raster.Line(Width, Height, x1, y1, x2, y2, ref plot);
    }

    /// <summary>Vista in sola lettura per i renderer, senza copia; valida fino alla prossima modifica.</summary>
    internal ReadOnlySpan<Cell> Cells => cells;

    /// <summary>Copia indipendente delle celle, in ordine per righe.</summary>
    public Cell[] Snapshot() => (Cell[])cells.Clone();

    private readonly struct CellPlotter(Canvas canvas, Cell cell) : IPlotter
    {
        public void Plot(int x, int y) => canvas.SetPixel(x, y, cell);
    }
}
