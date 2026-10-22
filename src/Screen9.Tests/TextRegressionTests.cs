using System.Diagnostics;

namespace Screen9.Tests;

/// <summary>Regressioni 2.3.1: overflow di DrawGlyph, ScrollUp a blocchi equivalente al modello, PCFont.TryLoad robusto.</summary>
public sealed class TextRegressionTests
{
    private static readonly PCFont Solid = PCFont.FromRom(Enumerable.Repeat((byte)0xFF, 256 * 16).ToArray(), 16);

    [Theory]
    [InlineData(1 << 29, 0)] [InlineData(0, 1 << 28)] [InlineData(int.MaxValue, int.MaxValue)] [InlineData(int.MinValue, int.MinValue)]
    [InlineData(-1, 0)] [InlineData(0, -1)] [InlineData(80, 0)] [InlineData(0, 30)]
    public void GlyphCellsOutsideTheScreenDrawNothing(int column, int row)
    {
        var pc = new PCCanvas(12); pc.DrawGlyph(column, row, 'A', 14, 1, Solid);
        Assert.All(pc.PixelSnapshot(), index => Assert.Equal(0, index));
    }

    public static TheoryData<int, int> ModesAndPages => new() { { 1, 0 }, { 2, 0 }, { 7, 0 }, { 7, 3 }, { 8, 1 }, { 9, 1 }, { 11, 0 }, { 12, 0 }, { 13, 0 } };

    [Theory] [MemberData(nameof(ModesAndPages))]
    public void ScrollUpMatchesThePixelModelAndVideoMemory(int mode, int page)
    {
        var pc = new PCCanvas(mode) { ActivePage = page }; var random = new Random(mode * 10 + page);
        for (int i = 0; i < 300; i++) pc.Box(random.Next(pc.Width), random.Next(pc.Height), random.Next(1, 30), random.Next(1, 30), random.Next(pc.ColorCount), true);
        var others = Enumerable.Range(0, pc.PageCount).Where(p => p != page).ToDictionary(p => p, p => pc.PixelSnapshot(p));
        if (pc.VideoMode.Interleaved) pc.WriteByte(8100, 0x5A); // padding CGA oltre le 100 righe di ciascun banco
        for (int step = 0; step < 6; step++)
        {
            int top = random.Next(pc.Height), height = random.Next(pc.Height - top + 1), lines = random.Next(height + 3), fill = random.Next(pc.ColorCount);
            var expected = pc.PixelSnapshot(); int kept = Math.Max(0, height - lines);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < pc.Width; x++)
                    expected[(top + y) * pc.Width + x] = y < kept ? pc.Point(x, top + y + lines) is var c and >= 0 ? (byte)c : (byte)0 : (byte)fill;
            pc.ScrollUp(top, height, lines, fill);
            Assert.Equal(expected, pc.PixelSnapshot()); Assert.Equal(expected, PCCanvasContractTests.DecodePage(pc, page));
        }
        foreach (var (other, pixels) in others) { Assert.Equal(pixels, pc.PixelSnapshot(other)); Assert.Equal(pixels, PCCanvasContractTests.DecodePage(pc, other)); }
        if (pc.VideoMode.Interleaved) Assert.Equal(0x5A, pc.ReadByte(8100));
    }

    [Fact]
    public void ScrollUpValidatesAndTreatsZeroLinesAsNoOp()
    {
        var pc = new PCCanvas(13); pc.PSet(0, 0, 5);
        pc.ScrollUp(0, 200, 0, 9); pc.ScrollUp(10, 0, 4, 9); Assert.Equal(5, pc.Point(0, 0)); Assert.Equal(0, pc.Point(0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => pc.ScrollUp(-1, 1, 1, 0)); Assert.Throws<ArgumentOutOfRangeException>(() => pc.ScrollUp(0, 201, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => pc.ScrollUp(0, 1, -1, 0)); Assert.Throws<ArgumentOutOfRangeException>(() => pc.ScrollUp(0, 1, 1, 256));
    }

    [Theory] [InlineData("\0bad.rom")] [InlineData("missing-folder/none.fon")] [InlineData("")]
    public void TryLoadReturnsNullForUnusablePaths(string path) => Assert.Null(PCFont.TryLoad(path));
}

[Collection(nameof(PerformanceCollection))] [Trait("Category", "Performance")]
public sealed class TextPerformanceTests
{
    [Theory] [InlineData(12)] [InlineData(9)] [InlineData(1)] [InlineData(13)]
    public void ScrollingOneTextRowIsCheapAndDoesNotAllocate(int mode)
    {
        var pc = new PCCanvas(mode); int row = pc.VideoMode.CharacterHeight;
        for (int i = 0; i < 40; i++) pc.ScrollUp(0, pc.Height, row, 1);
        long best = long.MaxValue; double fastest = double.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread(); long start = Stopwatch.GetTimestamp(); // GetTimestamp non alloca
            pc.ScrollUp(0, pc.Height, row, 1);
            fastest = Math.Min(fastest, Stopwatch.GetElapsedTime(start).TotalMilliseconds); best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.Equal(0, best); Assert.InRange(fastest, 0, TimeBudget.Milliseconds(0.5));
    }
}
