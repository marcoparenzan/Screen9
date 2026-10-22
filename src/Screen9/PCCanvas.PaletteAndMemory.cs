namespace Screen9;

public sealed partial class PCCanvas
{
    public void ResetPalette()
    {
        if (Adapter == PCAdapter.VGA) { ResetDac(); return; }
        for (int i = 0; i < palette.Length; i++) palette[i] = PCColors[i];
        if (ColorCount == 4) { SetCgaPalette(1, false, 0); return; }
        if (ColorCount == 2) { palette[0] = PCColors[0]; palette[1] = PCColors[15]; }
        for (int i = 0; i < palette.Length; i++) UpdateInk(i);
    }
    /// <summary>CGA 320x200 palette family 0 (green/red/brown) or 1 (cyan/magenta/gray), optional bright bit.</summary>
    public void SetCgaPalette(int family, bool bright = false, int background = 0)
    {
        if (Adapter != PCAdapter.CGA || ColorCount != 4) throw new InvalidOperationException("CGA palette families require mode 1.");
        if (family is < 0 or > 1 || background is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(family));
        palette[0] = PCColors[background];
        var indices = family == 0 ? new[] { 2,4,6 } : new[] { 3,5,7 };
        for (int i = 0; i < 3; i++) palette[i + 1] = PCColors[indices[i] + (bright ? 8 : 0)];
        for (int i = 0; i < 4; i++) UpdateInk(i);
    }
    /// <summary>Palette entry from a CGA 16-color or EGA 64-color hardware code. VGA uses SetDac.</summary>
    public void SetPalette(int index, int hardwareColor)
    {
        CheckColor(index);
        if (Adapter == PCAdapter.VGA) throw new InvalidOperationException("Use SetDac for the programmable VGA palette.");
        if (Adapter == PCAdapter.CGA)
        { if (hardwareColor is < 0 or > 15) throw new ArgumentOutOfRangeException(nameof(hardwareColor)); if (Mode == 1 && index != 0) throw new InvalidOperationException("CGA mode 1 uses fixed palette families; only background entry 0 is directly writable."); if (Mode == 2 && index == 0 && hardwareColor != 0) throw new InvalidOperationException("CGA monochrome background is black."); palette[index] = PCColors[hardwareColor]; }
        else
        {
            if (hardwareColor is < 0 or > 63) throw new ArgumentOutOfRangeException(nameof(hardwareColor));
            palette[index] = new((byte)(((hardwareColor >> 2 & 1) * 170) + ((hardwareColor >> 5 & 1) * 85)),
                (byte)(((hardwareColor >> 1 & 1) * 170) + ((hardwareColor >> 4 & 1) * 85)),
                (byte)(((hardwareColor & 1) * 170) + ((hardwareColor >> 3 & 1) * 85)));
        }
        UpdateInk(index);
    }
    /// <summary>VGA six-bit DAC components (0..63) for a logical color, expanded to RGB8. Writes the DAC register the color maps to
    /// (identity in mode 13, default attribute controller in modes 11/12), so it agrees with OUT &amp;H3C8/&amp;H3C9.</summary>
    public void SetDac(int index, int red, int green, int blue)
    {
        RequireDac(); CheckColor(index);
        if (red is < 0 or > 63 || green is < 0 or > 63 || blue is < 0 or > 63) throw new ArgumentOutOfRangeException(nameof(red));
        int register = DacIndex(index);
        dac[register * 3] = (byte)red; dac[register * 3 + 1] = (byte)green; dac[register * 3 + 2] = (byte)blue; RefreshFromDac(register);
    }
    private void CheckAddress(int address, int plane)
    {
        if ((uint)plane >= (uint)PlaneCount) throw new ArgumentOutOfRangeException(nameof(plane));
        if ((uint)address >= (uint)MemoryBytesPerPlane) throw new ArgumentOutOfRangeException(nameof(address));
    }
    public byte ReadByte(int address, int plane = 0) { CheckAddress(address, plane); return memory[plane][address]; }
    public void WriteByte(int address, byte value, int plane = 0)
    {
        CheckAddress(address, plane); memory[plane][address] = value;
        if (VideoMode.Linear)
        { if (address < Width * Height) UpdateFromMemory(0, address % Width, address / Width, value); return; }
        int x, y;
        if (VideoMode.Interleaved)
        {
            int bank = address / 8192, offset = address % 8192;
            y = offset / BytesPerRow * 2 + bank;
            if (y >= Height) return;
            int perByte = ColorCount == 4 ? 4 : 8; x = offset % BytesPerRow * perByte;
            for (int i = 0; i < perByte; i++) UpdateFromMemory(0, x + i, y, ColorCount == 4 ? value >> ((3 - i) * 2) & 3 : value >> (7 - i) & 1);
            return;
        }
        DecodePlanar(address);
    }
    // Ricostruisce gli 8 pixel di un byte planare dai piani correnti; il padding oltre le pagine resta solo in VRAM.
    private void DecodePlanar(int address)
    {
        int page = address / PageBytes; if (page >= PageCount) return;
        int local = address % PageBytes, x = local % BytesPerRow * 8, y = local / BytesPerRow;
        for (int i = 0; i < 8; i++)
        {
            int color = 0; for (int p = 0; p < PlaneCount; p++) color |= (memory[p][address] >> (7 - i) & 1) << p;
            UpdateFromMemory(page, x + i, y, color);
        }
    }
    // La VRAM è già scritta dal chiamante: qui si aggiorna solo l'indice.
    private void UpdateFromMemory(int page, int x, int y, int color)
    {
        pixels[page][y * Width + x] = (byte)color;
    }
    /// <summary>Riempie la VRAM della pagina attiva con un colore; i byte di padding restano invariati.</summary>
    private void FillMemory(int color)
    {
        if (VideoMode.Linear) { Array.Fill(memory[0], (byte)color, 0, Width * Height); return; }
        if (VideoMode.Interleaved)
        {
            byte pattern = (byte)(ColorCount == 4 ? color * 0x55 : color * 0xFF); int bankBytes = Height / 2 * BytesPerRow;
            Array.Fill(memory[0], pattern, 0, bankBytes); Array.Fill(memory[0], pattern, 8192, bankBytes); return;
        }
        for (int p = 0; p < PlaneCount; p++) Array.Fill(memory[p], (color >> p & 1) != 0 ? (byte)0xFF : (byte)0, activePage * PageBytes, PageBytes);
    }
    private void EncodeMemory(int x, int y, int color)
    {
        if (VideoMode.Linear) { memory[0][y * Width + x] = (byte)color; return; }
        if (VideoMode.Interleaved)
        {
            int perByte = ColorCount == 4 ? 4 : 8, shift = (perByte - 1 - x % perByte) * (ColorCount == 4 ? 2 : 1);
            int address = (y & 1) * 8192 + y / 2 * BytesPerRow + x / perByte, mask = (ColorCount - 1) << shift;
            memory[0][address] = (byte)((memory[0][address] & ~mask) | color << shift); return;
        }
        int location = activePage * PageBytes + y * BytesPerRow + x / 8, bit = 1 << (7 - x % 8);
        for (int p = 0; p < PlaneCount; p++) memory[p][location] = (byte)((memory[p][location] & ~bit) | ((color >> p & 1) != 0 ? bit : 0));
    }
}
