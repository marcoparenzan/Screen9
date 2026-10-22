namespace Screen9;

public sealed class CanvasRegion
{
    private readonly Cell[] cells;
    public int Width { get; }
    public int Height { get; }
    /// <summary>Copia le celle in ordine per righe. Accetta array, slice e stackalloc senza copie intermedie.</summary>
    public CanvasRegion(int width, int height, ReadOnlySpan<Cell> source) : this(width, height, source.Length) => cells = source.ToArray();
    public CanvasRegion(int width, int height, IReadOnlyList<Cell> source)
        : this(width, height, (source ?? throw new ArgumentNullException(nameof(source))).Count) => cells = [.. source];
    // Array appena creato da Capture: la regione ne diventa proprietaria, nessuna seconda copia.
    internal CanvasRegion(int width, int height, Cell[] owned, bool trusted) : this(width, height, owned.Length) => cells = owned;
    private CanvasRegion(int width, int height, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (count != checked(width * height)) throw new ArgumentException("Region cell count does not match dimensions.");
        Width = width; Height = height; cells = [];
    }
    public Cell this[int x, int y] => (uint)x < (uint)Width && (uint)y < (uint)Height ? cells[y * Width + x] : throw new ArgumentOutOfRangeException(nameof(x));
    public Cell[] Snapshot() => (Cell[])cells.Clone();
}

public partial class Canvas
{
    /// <summary>Ellipse in cell coordinates, bounded by the viewport. Filled or a one-cell inside contour.</summary>
    public void DrawEllipse(int centerX, int centerY, int radiusX, int radiusY, Cell ink, bool filled = false)
    {
        var plot = new CellPlotter(this, ink); Raster.Ellipse(Width, Height, centerX, centerY, radiusX, radiusY, filled, ref plot);
    }
    /// <summary>Strict capture, copied independently. The rectangle must be inside the canvas.</summary>
    public CanvasRegion Capture(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x); ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if ((long)x + width > Width || (long)y + height > Height) throw new ArgumentException("Capture is outside canvas.");
        var result = new Cell[checked(width * height)];
        for (int row = 0; row < height; row++) Array.Copy(cells, (y + row) * Width + x, result, row * width, width);
        return new(width, height, result, trusted: true);
    }
    /// <summary>Blits an immutable region with clipping and optional transparent character; no XOR pixel emulation.</summary>
    public void Blit(CanvasRegion region, int x, int y, char? transparent = null)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (transparent is { } glyph) _ = new Cell(glyph);
        int left = (int)Math.Clamp(-(long)x, 0, region.Width), right = (int)Math.Clamp((long)Width - x, 0, region.Width);
        int top = (int)Math.Clamp(-(long)y, 0, region.Height), bottom = (int)Math.Clamp((long)Height - y, 0, region.Height);
        for (int row = top; row < bottom; row++) for (int column = left; column < right; column++)
        {
            var cell = region[column, row]; char character = cell.Character == '\0' ? ' ' : cell.Character;
            if (transparent != character) SetPixel((int)((long)x + column), (int)((long)y + row), cell);
        }
    }
}