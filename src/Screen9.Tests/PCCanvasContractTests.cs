namespace Screen9.Tests;

/// <summary>Invarianti che ogni ottimizzazione di PCCanvas (storage, dirty tracking, double buffering) deve preservare.
/// Il modello di riferimento è un semplice byte[] per pagina; la memoria video viene decodificata indipendentemente.</summary>
public sealed class PCCanvasContractTests
{
    public static TheoryData<int> AllModes => new() { 1, 2, 7, 8, 9, 11, 12, 13 };

    [Theory] [MemberData(nameof(AllModes))]
    public void RandomOperationsMatchReferenceModelAndVideoMemory(int mode)
    {
        var pc = new PCCanvas(mode); var random = new Random(mode * 7919); int w = pc.Width, h = pc.Height, colors = pc.ColorCount;
        var reference = new byte[w * h];
        for (int step = 0; step < 400; step++)
        {
            int color = random.Next(colors);
            switch (random.Next(4))
            {
                case 0:
                {
                    int x = random.Next(-5, w + 5), y = random.Next(-5, h + 5);
                    bool inside = (uint)x < (uint)w && (uint)y < (uint)h;
                    Assert.Equal(inside, pc.PSet(x, y, color)); if (inside) reference[y * w + x] = (byte)color;
                    break;
                }
                case 1:
                {
                    int x = random.Next(-20, w), y = random.Next(-20, h), bw = random.Next(0, 40), bh = random.Next(0, 40);
                    pc.Box(x, y, bw, bh, color, true);
                    for (int py = Math.Max(0, y); py < Math.Min(h, y + bh); py++) for (int px = Math.Max(0, x); px < Math.Min(w, x + bw); px++) reference[py * w + px] = (byte)color;
                    break;
                }
                case 2:
                {
                    int sx = random.Next(w - 8), sy = random.Next(h - 8), dx = random.Next(-8, w), dy = random.Next(-8, h);
                    var image = pc.GetImage(sx, sy, 8, 8); var op = (PCPixelOperation)random.Next(5); int? transparent = random.Next(3) == 0 ? color : null;
                    pc.PutImage(image, dx, dy, op, transparent);
                    for (int row = 0; row < 8; row++) for (int col = 0; col < 8; col++)
                    {
                        int px = dx + col, py = dy + row; if ((uint)px >= (uint)w || (uint)py >= (uint)h) continue;
                        int source = image[col, row]; if (source == transparent) continue;
                        int destination = reference[py * w + px];
                        reference[py * w + px] = (byte)(op switch { PCPixelOperation.Copy => source, PCPixelOperation.Preset => source ^ (colors - 1), PCPixelOperation.Xor => source ^ destination, PCPixelOperation.And => source & destination, _ => source | destination });
                    }
                    break;
                }
                default:
                    if (random.Next(20) == 0) { pc.ClearIndex(color); Array.Fill(reference, (byte)color); }
                    break;
            }
        }
        Assert.Equal(reference, pc.PixelSnapshot());
        Assert.Equal(reference, DecodePage(pc, 0));
    }

    [Theory] [MemberData(nameof(AllModes))]
    public void CellProjectionIsAFunctionOfIndexAndFollowsPalette(int mode)
    {
        var pc = new PCCanvas(mode); var random = new Random(mode);
        for (int i = 0; i < 400; i++) pc.Box(random.Next(pc.Width), random.Next(pc.Height), random.Next(1, 12), random.Next(1, 12), random.Next(pc.ColorCount), true);
        var map = new Dictionary<int, ConsoleColor>(); var pixels = pc.PixelSnapshot(); var frame = pc.ToCellCanvas(300, 100);
        for (int y = 0; y < 100; y++) for (int x = 0; x < 300; x++)
        {
            var cell = frame[x, y]; int index = pixels[y * pc.Height / 100 * pc.Width + x * pc.Width / 300]; Assert.Equal(' ', cell.Character);
            if (map.TryGetValue(index, out var expected)) Assert.Equal(expected, cell.Background); else map[index] = cell.Background;
        }
        if (pc.Adapter == PCAdapter.VGA && pc.ColorCount == 256)
        {
            pc.ClearIndex(200); pc.SetDac(200, 63, 21, 21); Assert.Equal(ConsoleColor.Red, pc.ToCellCanvas(1, 1)[0, 0].Background);
            pc.SetDac(200, 21, 21, 63); Assert.Equal(ConsoleColor.Blue, pc.ToCellCanvas(1, 1)[0, 0].Background); Assert.Equal(200, pc.Point(0, 0));
        }
    }

    [Fact]
    public void VgaDacRoundTripsAll256EntriesWithoutTouchingPixelsOrMemory()
    {
        var pc = new PCCanvas(13); for (int i = 0; i < 256; i++) pc.PSet(i % 320, i / 320, i);
        var pixels = pc.PixelSnapshot(); var memory = Enumerable.Range(0, 65536).Select(a => pc.ReadByte(a)).ToArray();
        for (int i = 0; i < 256; i++) pc.SetDac(i, i % 64, (i * 3) % 64, 63 - i % 64);
        for (int i = 0; i < 256; i++)
            Assert.Equal(new PCRgb((byte)((i % 64 * 255 + 31) / 63), (byte)(((i * 3) % 64 * 255 + 31) / 63), (byte)(((63 - i % 64) * 255 + 31) / 63)), pc.GetRgb(i));
        Assert.Equal(pixels, pc.PixelSnapshot()); Assert.Equal(memory, Enumerable.Range(0, 65536).Select(a => pc.ReadByte(a)).ToArray());
        pc.ResetPalette(); Assert.Equal(new PCRgb(255, 255, 255), pc.GetRgb(15)); Assert.Equal(pixels, pc.PixelSnapshot());
    }

    [Fact]
    public void VgaDefaultPaletteHasPcColorsGrayRampAndColorCube()
    {
        var pc = new PCCanvas(13);
        Assert.Equal(new PCRgb(0, 0, 0), pc.GetRgb(0)); Assert.Equal(new PCRgb(170, 85, 0), pc.GetRgb(6));
        Assert.Equal(new PCRgb(0, 0, 0), pc.GetRgb(16)); Assert.Equal(new PCRgb(255, 255, 255), pc.GetRgb(31));
        for (int i = 17; i < 32; i++) { var c = pc.GetRgb(i); Assert.True(c.Red == c.Green && c.Green == c.Blue && c.Red >= pc.GetRgb(i - 1).Red); }
        Assert.Equal(new PCRgb(255, 0, 0), pc.GetRgb(32 + 5 * 36)); Assert.Equal(new PCRgb(255, 255, 255), pc.GetRgb(32 + 215));
        for (int i = 248; i < 256; i++) Assert.Equal(new PCRgb(0, 0, 0), pc.GetRgb(i));
    }

    [Theory] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    public void PageFlippingKeepsBackBufferInvisibleUntilSwap(int mode)
    {
        var pc = new PCCanvas(mode); pc.ClearIndex(1); var front = pc.ToCellCanvas(40, 20).Snapshot();
        for (int frame = 0; frame < 3; frame++)
        {
            int back = pc.VisualPage ^ 1; pc.ActivePage = back; pc.ClearIndex(frame + 2); pc.Box(5, 5, 50, 50, 14, true);
            Assert.Equal(front, pc.ToCellCanvas(40, 20).Snapshot()); // il back buffer non trapela
            pc.VisualPage = back; var shown = pc.ToCellCanvas(40, 20).Snapshot(); Assert.NotEqual(front, shown); front = shown;
        }
        Assert.Equal(DecodePage(pc, 0), pc.PixelSnapshot(0)); Assert.Equal(DecodePage(pc, 1), pc.PixelSnapshot(1));
    }

    [Fact]
    public void InactivePageMemoryWritesUpdateOnlyThatPage()
    {
        var pc = new PCCanvas(7); pc.ClearIndex(3); var active = pc.PixelSnapshot(0);
        pc.WriteByte(8000 + 40, 0xFF, 0); pc.WriteByte(8000 + 40, 0xFF, 2);
        Assert.Equal(active, pc.PixelSnapshot(0)); Assert.All(Enumerable.Range(0, 8), x => Assert.Equal(5, pc.Point(x, 1, 1)));
    }

    [Theory] [InlineData(9, false)] [InlineData(9, true)] [InlineData(13, false)] [InlineData(1, true)]
    public void PaintMatchesFourConnectedReferenceFill(int mode, bool withBorder)
    {
        var pc = new PCCanvas(mode); var random = new Random(mode * 31 + (withBorder ? 1 : 0)); int w = pc.Width, h = pc.Height;
        for (int i = 0; i < 300; i++) pc.Line(random.Next(w), random.Next(h), random.Next(w), random.Next(h), 1 + random.Next(Math.Min(3, pc.ColorCount - 1)));
        for (int i = 0; i < 20; i++) pc.Ellipse(random.Next(w), random.Next(h), random.Next(1, 40), random.Next(1, 40), 1);
        for (int attempt = 0; attempt < 5; attempt++)
        {
            int x = random.Next(w), y = random.Next(h), color = random.Next(pc.ColorCount); int? border = withBorder ? 1 : null;
            var expected = pc.PixelSnapshot(); int seed = expected[y * w + x], expectedChanged = 0;
            if (seed != color && seed != border)
            {
                var visited = new bool[w * h]; var queue = new Queue<int>(); queue.Enqueue(y * w + x);
                while (queue.TryDequeue(out int p))
                {
                    if (visited[p]) continue; visited[p] = true; int c = expected[p];
                    if (border is null ? c != seed : c == border || c == color) continue;
                    expected[p] = (byte)color; expectedChanged++; int px = p % w;
                    if (px > 0) queue.Enqueue(p - 1); if (px + 1 < w) queue.Enqueue(p + 1); if (p >= w) queue.Enqueue(p - w); if (p + w < w * h) queue.Enqueue(p + w);
                }
            }
            Assert.Equal(expectedChanged, pc.Paint(x, y, color, border)); Assert.Equal(expected, pc.PixelSnapshot()); Assert.Equal(expected, DecodePage(pc, 0));
        }
    }

    [Fact]
    public void ToCellCanvasUsesNearestNeighborSampling()
    {
        var pc = new PCCanvas(13); pc.Box(0, 0, 160, 200, 4, true); pc.Box(160, 0, 160, 200, 1, true);
        var frame = pc.ToCellCanvas(2, 1); Assert.Equal(ConsoleColor.DarkRed, frame[0, 0].Background); Assert.Equal(ConsoleColor.DarkBlue, frame[1, 0].Background);
        var big = pc.ToCellCanvas(300, 100); Assert.Equal(ConsoleColor.DarkRed, big[149, 99].Background); Assert.Equal(ConsoleColor.DarkBlue, big[150, 0].Background);
    }

    /// <summary>Decodifica indipendente della memoria video: CGA interleaved, EGA/VGA planare, mode 13 lineare.</summary>
    internal static byte[] DecodePage(PCCanvas pc, int page)
    {
        int w = pc.Width, h = pc.Height; var result = new byte[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int color;
            if (pc.Mode == 13) color = pc.ReadByte(y * w + x);
            else if (pc.Mode == 1) color = pc.ReadByte((y & 1) * 8192 + y / 2 * 80 + x / 4) >> ((3 - x % 4) * 2) & 3;
            else if (pc.Mode == 2) color = pc.ReadByte((y & 1) * 8192 + y / 2 * 80 + x / 8) >> (7 - x % 8) & 1;
            else
            {
                int address = page * (w / 8) * h + y * (w / 8) + x / 8; color = 0;
                for (int p = 0; p < pc.PlaneCount; p++) color |= (pc.ReadByte(address, p) >> (7 - x % 8) & 1) << p;
            }
            result[y * w + x] = (byte)color;
        }
        return result;
    }
}
