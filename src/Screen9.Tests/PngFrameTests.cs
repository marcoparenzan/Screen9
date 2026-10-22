using System.Buffers.Binary;
using System.IO.Compression;

namespace Screen9.Tests;

/// <summary>PngFrame is verified by decoding its own output (signature, IHDR, PLTE, unfiltered IDAT rows) and
/// comparing pixels back to the source, the same self-contained-decoder style as AnsiFrameRendererTests'
/// bespoke VT screen.</summary>
public sealed class PngFrameTests
{
    /// <summary>Minimal reader for exactly what PngFrame writes: IHDR (8-bit, color type 3, no interlace),
    /// PLTE, one IDAT of filter-type-0 rows, IEND. Returns each pixel's resolved RGB. Not a general decoder.</summary>
    private static (int Width, int Height, int[] Rgb) Decode(byte[] png)
    {
        Assert.Equal<byte>([137, 80, 78, 71, 13, 10, 26, 10], png[..8]);
        int width = 0, height = 0; byte[] palette = []; var idat = new MemoryStream(); int offset = 8;
        while (offset < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            string type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png.AsSpan(offset + 8, length);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(data); height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                Assert.Equal(8, data[8]); Assert.Equal(3, data[9]);
            }
            else if (type == "PLTE") palette = data.ToArray();
            else if (type == "IDAT") idat.Write(data);
            offset += 12 + length;
        }
        idat.Position = 0;
        using var zlib = new ZLibStream(idat, CompressionMode.Decompress);
        using var inflated = new MemoryStream(); zlib.CopyTo(inflated);
        byte[] raw = inflated.ToArray(); int[] rgb = new int[width * height];
        for (int y = 0; y < height; y++)
        {
            Assert.Equal(0, raw[y * (width + 1)]); // filter type None, as PngFrame always writes
            for (int x = 0; x < width; x++)
            {
                int index = raw[y * (width + 1) + 1 + x];
                rgb[y * width + x] = palette[index * 3] << 16 | palette[index * 3 + 1] << 8 | palette[index * 3 + 2];
            }
        }
        return (width, height, rgb);
    }

    [Fact]
    public void GraphicsFrameDecodesToExactPaletteColors()
    {
        var pc = new PCCanvas(9); pc.Box(0, 0, pc.Width, pc.Height, 4, true); pc.PSet(3, 5, 14);
        var (width, height, rgb) = Decode(PngFrame.Encode(pc));
        Assert.Equal((pc.Width, pc.Height), (width, height));
        Assert.Equal(pc.GetRgb(4).Packed, rgb[0]);
        Assert.Equal(pc.GetRgb(14).Packed, rgb[5 * width + 3]);
    }

    [Fact]
    public void PaletteChangesAreReflected()
    {
        var pc = new PCCanvas(9); pc.ClearIndex(1); pc.SetPalette(1, 4); // index 1 now shows EGA red
        var (_, _, rgb) = Decode(PngFrame.Encode(pc));
        Assert.Equal(pc.GetRgb(1).Packed, rgb[0]);
    }

    [Fact]
    public void TextFrameDecodesAtVgaResolutionWithCgaColors()
    {
        var cells = new Canvas(80, 25); cells.SetPixel(0, 0, new('A', ConsoleColor.Yellow, ConsoleColor.DarkBlue));
        var (width, height, rgb) = Decode(PngFrame.Encode(cells));
        Assert.Equal((640, 400), (width, height));
        Assert.Equal(CgaPalette.Rgb(ConsoleColor.DarkBlue).Packed, rgb[0]);           // paper at the cell's corner
        Assert.Contains(CgaPalette.Rgb(ConsoleColor.Yellow).Packed, rgb[..(16 * width)]); // ink somewhere in the 'A' cell row
    }

    [Fact]
    public void TextFrameMatchesPixelWindowCapture()
    {
        var cells = new Canvas(10, 2); cells.DrawText(0, 0, "Gorilla!", ConsoleColor.White, ConsoleColor.Blue);
        var (width, _, rgb) = Decode(PngFrame.Encode(cells));
        var (_, _, bgra) = PixelWindow.Capture(cells);
        for (int i = 0; i < rgb.Length; i++)
            Assert.Equal(bgra[i * 4 + 2] << 16 | bgra[i * 4 + 1] << 8 | bgra[i * 4], rgb[i]);
        Assert.Equal(80, width);
    }

    [Fact]
    public void FramesAreSmallEnoughToStreamRepeatedly()
    {
        var pc = new PCCanvas(9); pc.ClearIndex(1);
        byte[] png = PngFrame.Encode(pc);
        Assert.True(png.Length < 5_000, $"flat EGA frame should compress tiny, was {png.Length} bytes");
    }
}
