namespace Screen9.Tests;

public sealed class PutPixelsTests
{
    [Theory]
    [InlineData(1)] [InlineData(7)] [InlineData(9)] [InlineData(12)] [InlineData(13)]
    public void WritesTheSamePixelsAndVideoMemoryAsPSetIncludingClipping(int mode)
    {
        var bulk = new PCCanvas(mode); var single = new PCCanvas(mode);
        int colors = bulk.ColorCount, width = 37, height = 11;
        var indices = new byte[width * height];
        for (int i = 0; i < indices.Length; i++) indices[i] = (byte)(i * 7 % colors);
        foreach (var (x, y) in new[] { (0, 0), (5, 3), (-4, -2), (bulk.Width - 10, bulk.Height - 5) })
        {
            bulk.PutPixels(indices, x, y, width, height);
            for (int row = 0; row < height; row++) for (int column = 0; column < width; column++) single.PSet(x + column, y + row, indices[row * width + column]);
        }
        Assert.Equal(single.PixelSnapshot(), bulk.PixelSnapshot());
        for (int plane = 0; plane < bulk.PlaneCount; plane++)
            for (int address = 0; address < bulk.MemoryBytesPerPlane; address++)
                Assert.Equal(single.ReadByte(address, plane), bulk.ReadByte(address, plane));
    }

    [Fact]
    public void RejectsWrongSizesAndColorsOutOfRangeAndIgnoresAnythingOffscreen()
    {
        var pc = new PCCanvas(9);
        Assert.Throws<ArgumentException>(() => pc.PutPixels(new byte[5], 0, 0, 2, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => pc.PutPixels(new byte[] { 16 }, 0, 0, 1, 1));
        pc.PutPixels(new byte[] { 3, 3, 3, 3 }, int.MaxValue, int.MinValue, 2, 2);
        pc.PutPixels(new byte[] { 3, 3, 3, 3 }, -2, -2, 2, 2);
        Assert.All(pc.PixelSnapshot(), p => Assert.Equal(0, p));
    }
}
