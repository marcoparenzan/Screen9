namespace Screen9;

public enum PCAdapter { CGA, EGA, VGA }
public enum PCPixelOperation { Copy, Preset, Xor, And, Or }
public readonly record struct PCRgb(byte Red, byte Green, byte Blue)
{
    public int Packed => Red << 16 | Green << 8 | Blue;
}
public sealed class PCVideoMode
{
    public int Number { get; }
    public PCAdapter Adapter { get; }
    public int Width { get; }
    public int Height { get; }
    public int Colors { get; }
    public int Pages { get; }
    public bool Interleaved { get; }
    public bool Linear { get; }
    /// <summary>Altezza della cella di testo del BIOS: 14 nel modo 9 (EGA 350 righe), 16 nei modi 11/12 (VGA 480), altrimenti 8.</summary>
    public int CharacterHeight => Height == 350 ? 14 : Height == 480 ? 16 : 8;
    public int TextColumns => Width / 8;
    public int TextRows => Height / CharacterHeight;
    /// <summary>Font ROM IBM del modo (EGA 8x8, EGA 8x14 o VGA 8x16), come il BIOS lo usa per il testo in grafica.</summary>
    public PCFont Font => PCFont.ForHeight(CharacterHeight);
    private PCVideoMode(int number, PCAdapter adapter, int width, int height, int colors, int pages, bool interleaved, bool linear)
    { Number=number; Adapter=adapter; Width=width; Height=height; Colors=colors; Pages=pages; Interleaved=interleaved; Linear=linear; }
    public static PCVideoMode FromNumber(int mode) => mode switch
    {
        1 => new(1, PCAdapter.CGA, 320, 200, 4, 1, true, false),
        2 => new(2, PCAdapter.CGA, 640, 200, 2, 1, true, false),
        7 => new(7, PCAdapter.EGA, 320, 200, 16, 8, false, false),
        8 => new(8, PCAdapter.EGA, 640, 200, 16, 4, false, false),
        9 => new(9, PCAdapter.EGA, 640, 350, 16, 2, false, false),
        11 => new(11, PCAdapter.VGA, 640, 480, 2, 1, false, false),
        12 => new(12, PCAdapter.VGA, 640, 480, 16, 1, false, false),
        13 => new(13, PCAdapter.VGA, 320, 200, 256, 1, false, true),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), "Supported graphics modes: 1,2,7,8,9,11,12,13. Text/other BIOS modes are not emulated.")
    };
}
public sealed class PCImage
{
    private readonly byte[] pixels;
    public int Width { get; }
    public int Height { get; }
    public int Colors { get; }
    /// <summary>Copia gli indici in ordine per righe. Accetta array, slice e stackalloc senza copie intermedie.</summary>
    public PCImage(int width, int height, int colors, ReadOnlySpan<byte> source) : this(width, height, colors, source.Length)
    {
        CheckDepth(source, colors); pixels = source.ToArray();
    }
    public PCImage(int width, int height, int colors, IReadOnlyList<byte> source)
        : this(width, height, colors, (source ?? throw new ArgumentNullException(nameof(source))).Count)
    {
        byte[] copy = [.. source]; CheckDepth(copy, colors); pixels = copy;
    }
    // Array appena creato da GetImage, già nella profondità del canvas: nessuna seconda copia né controllo.
    internal PCImage(int width, int height, int colors, byte[] owned, bool trusted) : this(width, height, colors, owned.Length) => pixels = owned;
    private PCImage(int width, int height, int colors, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (colors is not (2 or 4 or 16 or 256) || (long)width * height > 307200 || count != (long)width * height) throw new ArgumentException("Invalid PC image dimensions or depth.");
        Width = width; Height = height; Colors = colors; pixels = [];
    }
    private static void CheckDepth(ReadOnlySpan<byte> source, int colors)
    {
        if (colors < 256 && source.IndexOfAnyInRange((byte)colors, byte.MaxValue) >= 0) throw new ArgumentException("Image pixel exceeds indexed color depth.");
    }
    public byte this[int x, int y] => (uint)x < (uint)Width && (uint)y < (uint)Height ? pixels[y * Width + x] : throw new ArgumentOutOfRangeException();
    public byte[] Snapshot() => (byte[])pixels.Clone();
}

/// <summary>Indexed PC framebuffer. Logical graphics, not a register-accurate PC emulator.
/// Display it with ToCellCanvas (16-color cell preview for ICanvasRenderer) or AnsiFrameRenderer (truecolor).</summary>
public sealed partial class PCCanvas
{
    private readonly byte[][] pixels;
    private readonly byte[][] memory;
    private readonly PCRgb[] palette;
    private readonly Cell[] inks; // proiezione terminale di ogni indice, aggiornata per voce di palette
    private int activePage, visualPage;
    public int Width { get; }
    public int Height { get; }
    public PCVideoMode VideoMode { get; }
    public int Mode => VideoMode.Number;
    public PCAdapter Adapter => VideoMode.Adapter;
    public int ColorCount => VideoMode.Colors;
    public int PageCount => VideoMode.Pages;
    public int PlaneCount => VideoMode.Linear || VideoMode.Interleaved || ColorCount == 2 ? 1 : 4;
    public int MemoryBytesPerPlane => VideoMode.Interleaved ? 16384 : 65536;
    private int BytesPerRow => VideoMode.Linear ? Width : VideoMode.Interleaved && ColorCount == 4 ? Width / 4 : Width / 8;
    private int PageBytes => BytesPerRow * Height;
    public int ActivePage
    {
        get => activePage;
        set { CheckPage(value); activePage = value; }
    }
    public int VisualPage { get => visualPage; set { CheckPage(value); visualPage = value; } }
    public PCCanvas(int mode = 9) : this(PCVideoMode.FromNumber(mode)) { }
    private PCCanvas(PCVideoMode mode)
    {
        VideoMode = mode; Width = mode.Width; Height = mode.Height; pixels = Enumerable.Range(0, mode.Pages).Select(_ => new byte[Width * Height]).ToArray();
        memory = Enumerable.Range(0, PlaneCount).Select(_ => new byte[MemoryBytesPerPlane]).ToArray(); palette = new PCRgb[ColorCount]; inks = new Cell[ColorCount]; ResetPalette();
    }
    private void Store(int x, int y, int color) { pixels[activePage][y * Width + x] = (byte)color; EncodeMemory(x, y, color); }
    private void UpdateInk(int index) => inks[index] = new(' ', ConsoleColor.Black, NearestConsoleColor(palette[index]));
    private void CheckColor(int color) { if ((uint)color >= (uint)ColorCount) throw new ArgumentOutOfRangeException(nameof(color)); }
    private void CheckPage(int page) { if ((uint)page >= (uint)PageCount) throw new ArgumentOutOfRangeException(nameof(page)); }
    public bool PSet(int x, int y, int color)
    {
        CheckColor(color);
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return false;
        Store(x, y, color); return true;
    }
    public int Point(int x, int y) => Point(x, y, activePage);
    public int Point(int x, int y, int page)
    { CheckPage(page); return (uint)x < (uint)Width && (uint)y < (uint)Height ? pixels[page][y * Width + x] : -1; }
    public void ClearIndex(int color = 0)
    {
        CheckColor(color); Array.Fill(pixels[activePage], (byte)color); FillMemory(color);
    }
    public void Line(int x1, int y1, int x2, int y2, int color)
    {
        CheckColor(color); var plot = new IndexPlotter(this, color); Raster.Line(Width, Height, x1, y1, x2, y2, ref plot);
    }
    public void Box(int x, int y, int width, int height, int color, bool filled = false)
    {
        CheckColor(color); var plot = new IndexPlotter(this, color);
        if (filled) Raster.FillRectangle(Width, Height, x, y, width, height, ref plot); else Raster.Rectangle(Width, Height, x, y, width, height, ref plot);
    }
    public void Ellipse(int x, int y, int radiusX, int radiusY, int color, bool filled = false)
    {
        CheckColor(color); var plot = new IndexPlotter(this, color); Raster.Ellipse(Width, Height, x, y, radiusX, radiusY, filled, ref plot);
    }
    public PCRgb GetRgb(int index) { CheckColor(index); return palette[index]; }
    public byte[] PixelSnapshot(int page = -1) { page = page == -1 ? activePage : page; CheckPage(page); return (byte[])pixels[page].Clone(); }
    // Frame presentato (ManualPresent): indici di VisualPage e palette, fissati all'ultimo Present.
    private byte[]? presentedPixels;
    private PCRgb[]? presentedPalette;
    private Cell[]? presentedInks;
    /// <summary>Double buffering per ogni modo, anche VGA a pagina singola. Se true, la proiezione (ToCellCanvas)
    /// mostra VisualPage e palette come erano all'ultimo Present: disegni e cambi di palette successivi non trapelano.
    /// Attivarlo presenta subito il frame corrente. Le celle ereditate da Canvas restano la superficie di disegno.</summary>
    public bool ManualPresent
    {
        get => presentedPixels is not null;
        set
        {
            if (value == ManualPresent) return;
            if (!value) { presentedPixels = null; presentedPalette = null; presentedInks = null; return; }
            presentedPixels = new byte[Width * Height]; presentedPalette = new PCRgb[ColorCount]; presentedInks = new Cell[ColorCount]; Present();
        }
    }
    /// <summary>Rende visibile il frame corrente: copia VisualPage e palette. O(pixel), nessuna allocazione.</summary>
    public void Present()
    {
        if (presentedPixels is null || presentedPalette is null || presentedInks is null) throw new InvalidOperationException("Present requires ManualPresent = true.");
        pixels[visualPage].CopyTo(presentedPixels, 0); palette.CopyTo(presentedPalette, 0); inks.CopyTo(presentedInks, 0);
    }
    /// <summary>Indici del frame visibile: presentato se ManualPresent, altrimenti VisualPage corrente.</summary>
    internal ReadOnlySpan<byte> VisiblePixels => presentedPixels ?? pixels[visualPage];
    internal ReadOnlySpan<PCRgb> VisiblePalette => presentedPalette ?? palette;
    public Canvas ToCellCanvas(int width, int height, CancellationToken token = default)
    {
        CheckProjection(width, height); token.ThrowIfCancellationRequested();
        var result = new Canvas(width, height); Project(result, token); return result;
    }
    /// <summary>Proiezione nel canvas indicato, riusabile tra i frame senza allocazioni. Stessi limiti di dimensione.</summary>
    public void ToCellCanvas(Canvas destination, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(destination); CheckProjection(destination.Width, destination.Height);
        token.ThrowIfCancellationRequested(); Project(destination, token);
    }
    private static void CheckProjection(int width, int height)
    {
        if (width is < 1 or > 300 || height is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(width), "Terminal projection limits: 1..300 by 1..100.");
    }
    private void Project(Canvas destination, CancellationToken token)
    {
        var page = presentedPixels ?? pixels[visualPage]; var colors = presentedInks ?? inks; int width = destination.Width, height = destination.Height;
        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++) destination[x, y] = colors[page[(y * Height / height) * Width + x * Width / width]];
        }
    }
    private readonly struct IndexPlotter(PCCanvas canvas, int color) : IPlotter
    {
        public void Plot(int x, int y) => canvas.Store(x, y, color);
    }
    private static ConsoleColor NearestConsoleColor(PCRgb color)
    {
        int best = 0, distance = int.MaxValue;
        for (int i = 0; i < PCColors.Length; i++)
        {
            int r = color.Red - PCColors[i].Red, g = color.Green - PCColors[i].Green, b = color.Blue - PCColors[i].Blue;
            int candidate = r * r + g * g + b * b; if (candidate < distance) { best = i; distance = candidate; }
        }
        return (ConsoleColor)best;
    }
    private static readonly PCRgb[] PCColors =
    [new(0,0,0),new(0,0,170),new(0,170,0),new(0,170,170),new(170,0,0),new(170,0,170),new(170,85,0),new(170,170,170),new(85,85,85),new(85,85,255),new(85,255,85),new(85,255,255),new(255,85,85),new(255,85,255),new(255,255,85),new(255,255,255)];
}
