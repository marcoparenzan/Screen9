namespace Screen9.Tests;

/// <summary>Vista CPU della memoria video (DEF SEG/PEEK/POKE) e porte EGA/VGA (OUT/INP) in 2.2.0.</summary>
public sealed class PortTests
{
    private static void SetDacViaPorts(PCCanvas pc, int index, params byte[] components)
    {
        pc.WritePort(0x3C8, (byte)index); foreach (byte c in components) pc.WritePort(0x3C9, c);
    }

    [Fact]
    public void Mode13WindowIsLinearAtA000()
    {
        var pc = new PCCanvas(13); Assert.Equal((0xA000, 0x10000), (pc.VideoSegment, pc.VideoWindowBytes));
        pc.WriteVideoMemory(320 * 10 + 5, 42); Assert.Equal(42, pc.Point(5, 10)); Assert.Equal(42, pc.ReadVideoMemory(320 * 10 + 5));
        pc.PSet(319, 199, 7); Assert.Equal(7, pc.ReadVideoMemory(63999));
        pc.WriteVideoMemory(65000, 9); Assert.Equal(9, pc.ReadVideoMemory(65000)); // oltre i 64000 byte visibili: solo VRAM
        Assert.Throws<ArgumentOutOfRangeException>(() => pc.WriteVideoMemory(0x10000, 1)); Assert.Throws<ArgumentOutOfRangeException>(() => pc.ReadVideoMemory(-1));
    }

    [Fact]
    public void PlanarWritesUseTheMapMaskAndReadsUseTheReadMap()
    {
        var pc = new PCCanvas(12);
        pc.WriteVideoMemory(0, 0xFF); Assert.All(Enumerable.Range(0, 8), x => Assert.Equal(15, pc.Point(x, 0))); // default: tutti i piani
        pc.WritePort(0x3C4, 2); pc.WritePort(0x3C5, 0b0100); pc.WriteVideoMemory(80, 0x0F); // solo piano 2, riga 1
        Assert.Equal(new[] { 0, 0, 0, 0, 4, 4, 4, 4 }, Enumerable.Range(0, 8).Select(x => pc.Point(x, 1)).ToArray());
        pc.WritePort(0x3CE, 4); pc.WritePort(0x3CF, 2); Assert.Equal(0x0F, pc.ReadVideoMemory(80));
        pc.WritePort(0x3CF, 0); Assert.Equal(0, pc.ReadVideoMemory(80)); Assert.Equal(0xFF, pc.ReadVideoMemory(0));
        pc.WritePort(0x3C5, 0); pc.WriteVideoMemory(0, 0); Assert.Equal(15, pc.Point(0, 0)); // nessun piano abilitato: nessun effetto
        Assert.Equal((2, 0), (pc.ReadPort(0x3C4), pc.ReadPort(0x3C5)));
        Assert.Equal(pc.PixelSnapshot(), PCCanvasContractTests.DecodePage(pc, 0));
    }

    [Fact]
    public void EgaWindowAddressesInactivePagesAndMonoVgaUsesOnePlane()
    {
        var ega = new PCCanvas(9); ega.WriteVideoMemory(28000, 0x80); Assert.Equal(15, ega.Point(0, 0, 1)); Assert.Equal(0, ega.Point(0, 0, 0));
        var mono = new PCCanvas(11); mono.WriteVideoMemory(0, 0xA0); Assert.Equal(new[] { 1, 0, 1, 0 }, Enumerable.Range(0, 4).Select(x => mono.Point(x, 0)).ToArray());
        mono.WritePort(0x3CE, 4); mono.WritePort(0x3CF, 3); Assert.Equal(0, mono.ReadVideoMemory(0)); // piano inesistente
    }

    [Fact]
    public void CgaWindowIsB800AndMirrorsSixteenKilobytes()
    {
        var pc = new PCCanvas(1); Assert.Equal((0xB800, 0x8000), (pc.VideoSegment, pc.VideoWindowBytes));
        pc.WriteVideoMemory(0x4000, 0xC0); Assert.Equal(3, pc.Point(0, 0)); Assert.Equal(0xC0, pc.ReadVideoMemory(0));
        pc.WriteVideoMemory(8192, 0x40); Assert.Equal(1, pc.Point(0, 1));
        Assert.Throws<InvalidOperationException>(() => pc.WritePort(0x3C4, 2)); Assert.Throws<InvalidOperationException>(() => pc.WritePort(0x3C8, 0));
    }

    [Fact]
    public void DacPortsWriteAutoIncrementAndReadBack()
    {
        var pc = new PCCanvas(13); pc.PSet(0, 0, 200); var memory = pc.PixelSnapshot();
        SetDacViaPorts(pc, 200, 63, 0, 0, 0, 63, 0); // 200 rosso, 201 verde
        Assert.Equal(new PCRgb(255, 0, 0), pc.GetRgb(200)); Assert.Equal(new PCRgb(0, 255, 0), pc.GetRgb(201));
        Assert.Equal(202, pc.ReadPort(0x3C8)); Assert.Equal(0, pc.ReadPort(0x3C7));
        pc.WritePort(0x3C7, 200); Assert.Equal(3, pc.ReadPort(0x3C7));
        Assert.Equal(new byte[] { 63, 0, 0, 0, 63, 0 }, Enumerable.Range(0, 6).Select(_ => pc.ReadPort(0x3C9)).ToArray());
        SetDacViaPorts(pc, 5, 255, 64, 21); Assert.Equal(new PCRgb(255, 0, 85), pc.GetRgb(5)); // 6 bit: 255->63, 64->0
        Assert.Equal(memory, pc.PixelSnapshot()); Assert.Equal(200, pc.Point(0, 0));
        Assert.Equal(ConsoleColor.DarkRed, pc.ToCellCanvas(1, 1)[0, 0].Background); // (255,0,0) è più vicino a DarkRed che a Red (255,85,85)
    }

    [Fact]
    public void SixteenColorVgaModesGoThroughTheAttributeController()
    {
        var pc = new PCCanvas(12); var before = Enumerable.Range(0, 16).Select(pc.GetRgb).ToArray();
        Assert.Equal(new PCRgb(170, 85, 0), before[6]);
        SetDacViaPorts(pc, 20, 0, 0, 63); // registro 20 = colore 6 (marrone)
        Assert.Equal(new PCRgb(0, 0, 255), pc.GetRgb(6));
        Assert.All(Enumerable.Range(0, 16).Where(i => i != 6), i => Assert.Equal(before[i], pc.GetRgb(i)));
        SetDacViaPorts(pc, 6, 63, 63, 63); Assert.Equal(new PCRgb(0, 0, 255), pc.GetRgb(6)); // registro 6 non è usato da nessun colore
        pc.SetDac(14, 0, 63, 0); pc.WritePort(0x3C7, 62); // giallo = registro 62
        Assert.Equal(new byte[] { 0, 63, 0 }, Enumerable.Range(0, 3).Select(_ => pc.ReadPort(0x3C9)).ToArray());
        var mono = new PCCanvas(11); SetDacViaPorts(mono, 63, 63, 0, 0); Assert.Equal(new PCRgb(255, 0, 0), mono.GetRgb(1));
    }

    [Fact]
    public void ResetPaletteRestoresDacAndPortState()
    {
        var pc = new PCCanvas(13); SetDacViaPorts(pc, 1, 63, 63); pc.WritePort(0x3C6, 0x0F);
        pc.ResetPalette(); Assert.Equal(new PCRgb(0, 0, 170), pc.GetRgb(1)); Assert.Equal(0xFF, pc.ReadPort(0x3C6)); Assert.Equal(0, pc.ReadPort(0x3C8));
        pc.WritePort(0x3C9, 63); pc.WritePort(0x3C9, 0); pc.WritePort(0x3C9, 0); Assert.Equal(new PCRgb(255, 0, 0), pc.GetRgb(0)); // componente parziale scartata
    }

    [Fact]
    public void PortChangesRespectManualPresent()
    {
        var pc = new PCCanvas(13) { ManualPresent = true }; pc.ClearIndex(9); pc.Present();
        SetDacViaPorts(pc, 9, 63, 0, 0); Assert.Equal(ConsoleColor.Blue, pc.ToCellCanvas(1, 1)[0, 0].Background);
        pc.Present(); Assert.Equal(ConsoleColor.DarkRed, pc.ToCellCanvas(1, 1)[0, 0].Background);
    }

    [Theory] [InlineData(0x3DA)] [InlineData(0x60)] [InlineData(0x3C0)] [InlineData(-1)]
    public void UnsupportedPortsAreRejected(int port)
    {
        var pc = new PCCanvas(13);
        Assert.Throws<ArgumentOutOfRangeException>(() => pc.WritePort(port, 0)); Assert.Throws<ArgumentOutOfRangeException>(() => pc.ReadPort(port));
        Assert.Throws<InvalidOperationException>(() => new PCCanvas(9).WritePort(0x3C9, 0));
    }
}
