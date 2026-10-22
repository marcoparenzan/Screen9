using System.Buffers.Binary;
using System.Text;
using Screen9;

namespace RallyX;

/// <summary>An image of palette indices. <see cref="Clear"/> is the transparent colour of sprites.</summary>
public sealed class RallyXSheet(int width, int height, byte[] pixels)
{
    public const byte Clear = 255;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels;
    public byte this[int x, int y] => Pixels[y * Width + x];
}

/// <summary>The game art, already at true resolution (24 px tiles, 16 px sprites, 320x240 screen), produced by
/// <c>ops/build-rallyx-assets.py</c> from the RallyX project assets. Layout of <c>rallyx.assets</c> (little-endian):
/// "RLX1", game palette and title palette (16 RGB8 colours each), three title indices (white, black, accent),
/// number of sections; each section is a name (length byte + ASCII), width and height (uint16) and one byte per pixel.</summary>
public sealed class RallyXAssets
{
    // Fixed indices of the game palette (the first part of the palette is decided by the converter).
    public const int Black = 0, White = 1, Yellow = 2, Red = 3, PanelBlue = 4, HudGreen = 5, Road = 6, Grass = 7, Cyan = 8;

    public PCRgb[] GamePalette { get; }
    public PCRgb[] TitlePalette { get; }
    public int TitleWhite { get; }
    public int TitleBlack { get; }
    public int TitleAccent { get; }
    public RallyXSheet Tiles => Sheet("tiles");
    public RallyXSheet Cars => Sheet("cars");
    public RallyXSheet Flags => Sheet("flags");
    public RallyXSheet Rocks => Sheet("rocks");
    public RallyXSheet Smoke => Sheet("smoke");
    public RallyXSheet Hud => Sheet("hud");
    public RallyXSheet Title => Sheet("title");
    /// <summary>42x64 map of tileset gids: 1 is road, above 1 is wall.</summary>
    public RallyXSheet Map => Sheet("map");

    private readonly Dictionary<string, RallyXSheet> sheets;

    private RallyXAssets(PCRgb[] game, PCRgb[] title, int[] titleIndexes, Dictionary<string, RallyXSheet> sheets)
    {
        GamePalette = game; TitlePalette = title; this.sheets = sheets;
        (TitleWhite, TitleBlack, TitleAccent) = (titleIndexes[0], titleIndexes[1], titleIndexes[2]);
    }

    private RallyXSheet Sheet(string name) => sheets.TryGetValue(name, out var sheet) ? sheet : throw new InvalidDataException($"rallyx.assets: sezione '{name}' mancante.");

    public static RallyXAssets Load()
    {
        using var stream = typeof(RallyXAssets).Assembly.GetManifestResourceStream("rallyx.assets")
            ?? throw new InvalidOperationException("Risorsa rallyx.assets non trovata (ops/build-rallyx-assets.py).");
        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        return Parse(data);
    }

    public static RallyXAssets Parse(byte[] data)
    {
        if (data.Length < 4 || !data.AsSpan(0, 4).SequenceEqual("RLX1"u8)) throw new InvalidDataException("rallyx.assets: intestazione non valida.");
        int position = 4;
        PCRgb[] ReadPalette()
        {
            var palette = new PCRgb[16];
            for (int i = 0; i < 16; i++) { palette[i] = new(data[position], data[position + 1], data[position + 2]); position += 3; }
            return palette;
        }
        var game = ReadPalette();
        var title = ReadPalette();
        int[] titleIndexes = [data[position], data[position + 1], data[position + 2]];
        position += 3;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position));
        position += 2;
        var sheets = new Dictionary<string, RallyXSheet>();
        for (int i = 0; i < count; i++)
        {
            int length = data[position++];
            string name = Encoding.ASCII.GetString(data, position, length);
            position += length;
            int width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position)), height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position + 2));
            position += 4;
            sheets[name] = new RallyXSheet(width, height, data.AsSpan(position, width * height).ToArray());
            position += width * height;
        }
        return new RallyXAssets(game, title, titleIndexes, sheets);
    }
}
