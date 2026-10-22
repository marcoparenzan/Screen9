using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Screen9;

/// <summary>Lossless PNG of a visible frame, for hosts with no native window (a browser, a remote client):
/// where <see cref="PixelWindow"/> composes onto GDI, this just returns bytes. Indexed (PNG color type 3):
/// the frame already is palette indices plus a palette of at most 256 entries, so one byte per pixel goes to
/// zlib instead of three — a mostly-flat retro frame compresses to a few KB, quickly.</summary>
public static class PngFrame
{
    /// <summary>The visible page of a PCCanvas, native resolution, with its current palette.</summary>
    public static byte[] Encode(PCCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        byte[] pixels = canvas.PixelSnapshot(canvas.VisualPage);
        var palette = new PCRgb[canvas.VideoMode.Colors];
        for (int i = 0; i < palette.Length; i++) palette[i] = canvas.GetRgb(i);
        return Encode(canvas.Width, canvas.Height, pixels, palette);
    }

    /// <summary>Text cells rasterized with the VGA 8x16 font in the 16 CGA colors, as
    /// <see cref="PixelWindow.Capture(Canvas)"/> shows them: 80x25 = 640x400.</summary>
    public static byte[] Encode(Canvas cells)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var font = PCFont.Vga8x16;
        int width = cells.Width * 8, height = cells.Height * font.Height;
        byte[] pixels = new byte[checked(width * height)];
        for (int row = 0; row < cells.Height; row++)
            for (int column = 0; column < cells.Width; column++)
            {
                var cell = cells[column, row];
                int code = cell.Character is > '\0' and < (char)256 ? cell.Character : ' ';
                byte ink = (byte)((int)cell.Foreground & 15), paper = (byte)((int)cell.Background & 15);
                for (int y = 0; y < font.Height; y++)
                {
                    byte bits = font.Row(code, y);
                    int offset = (row * font.Height + y) * width + column * 8;
                    for (int x = 0; x < 8; x++) pixels[offset + x] = (bits & (0x80 >> x)) != 0 ? ink : paper;
                }
            }
        var palette = new PCRgb[16];
        for (int i = 0; i < 16; i++) palette[i] = CgaPalette.Rgb((ConsoleColor)i);
        return Encode(width, height, pixels, palette);
    }

    /// <summary>Indexed pixels (row-major, one byte each) with their palette (1..256 entries).</summary>
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> pixels, ReadOnlySpan<PCRgb> palette)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
        if (pixels.Length != width * height) throw new ArgumentException("Pixel count must be width * height.", nameof(pixels));
        if (palette.Length is < 1 or > 256) throw new ArgumentException("Palette must have 1..256 entries.", nameof(palette));
        using var image = new MemoryStream();
        image.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 3; // 8-bit indices, color type 3 (palette)
        Chunk(image, "IHDR", header);
        byte[] plte = new byte[palette.Length * 3];
        for (int i = 0; i < palette.Length; i++) { plte[i * 3] = palette[i].Red; plte[i * 3 + 1] = palette[i].Green; plte[i * 3 + 2] = palette[i].Blue; }
        Chunk(image, "PLTE", plte);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            byte[] row = new byte[width + 1]; // row[0] stays 0: filter type None
            for (int y = 0; y < height; y++)
            {
                pixels.Slice(y * width, width).CopyTo(row.AsSpan(1));
                zlib.Write(row);
            }
        }
        Chunk(image, "IDAT", compressed.ToArray()); Chunk(image, "IEND", []);
        return image.ToArray();
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length); stream.Write(number);
        byte[] name = Encoding.ASCII.GetBytes(type); stream.Write(name); stream.Write(data);
        uint crc = uint.MaxValue;
        foreach (byte b in name) crc = Update(crc, b);
        foreach (byte b in data) crc = Update(crc, b);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); stream.Write(number);
    }

    private static uint Update(uint crc, byte value)
    {
        crc ^= value;
        for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
        return crc;
    }
}
