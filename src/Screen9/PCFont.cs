namespace Screen9;

/// <summary>Font bitmap del generatore di caratteri PC: 256 glifi CP437 larghi 8 pixel (indice = byte, MSB a sinistra).
/// Incluse le ROM IBM EGA 8x8, EGA 8x14 e VGA 8x16 (<see cref="Ega8x8"/>, <see cref="Ega8x14"/>, <see cref="Vga8x16"/>,
/// <see cref="ForHeight"/>, <see cref="PCVideoMode.Font"/>); altri font da dump ROM grezzo o .fon OEM di Windows.</summary>
public sealed class PCFont
{
    private readonly byte[] rows;
    public int Height { get; }
    private PCFont(int height, byte[] rows) { Height = height; this.rows = rows; }

    // Risorse da Fonts/: The Ultimate Oldschool PC Font Pack v2.2 di VileR, CC BY-SA 4.0 (Fonts/LICENSE.TXT, Fonts/SOURCE.TXT).
    private static readonly Lazy<PCFont> ega8x8 = new(() => Embedded(8)), ega8x14 = new(() => Embedded(14)), vga8x16 = new(() => Embedded(16));
    /// <summary>ROM IBM EGA/VGA 8x8: celle dei modi a 200 righe (1, 2, 7, 8, 13).</summary>
    public static PCFont Ega8x8 => ega8x8.Value;
    /// <summary>ROM IBM EGA 8x14: celle del modo 9 (350 righe).</summary>
    public static PCFont Ega8x14 => ega8x14.Value;
    /// <summary>ROM IBM VGA 8x16: celle dei modi 11 e 12 (480 righe).</summary>
    public static PCFont Vga8x16 => vga8x16.Value;
    /// <summary>Font incluso per celle di 8, 14 o 16 righe; ArgumentOutOfRangeException per altre altezze.</summary>
    public static PCFont ForHeight(int cellHeight) => cellHeight switch
    {
        8 => Ega8x8, 14 => Ega8x14, 16 => Vga8x16,
        _ => throw new ArgumentOutOfRangeException(nameof(cellHeight), "Included fonts: 8, 14, 16 rows.")
    };
    private static PCFont Embedded(int height)
    {
        using var stream = typeof(PCFont).Assembly.GetManifestResourceStream($"Screen9.Fonts.ibm8x{height}.rom")
            ?? throw new InvalidOperationException($"Missing embedded font ibm8x{height}.rom.");
        var data = new byte[stream.Length]; stream.ReadExactly(data); return FromRom(data, height);
    }

    /// <summary>Dump ROM grezzo: 256 glifi da <paramref name="height"/> byte, una riga per byte.</summary>
    public static PCFont FromRom(ReadOnlySpan<byte> data, int height)
    {
        if (height is < 1 or > 32 || data.Length != 256 * height) throw new ArgumentException("ROM font must be 256 glyphs of 1..32 rows.");
        return new(height, data.ToArray());
    }
    public bool Pixel(int code, int x, int y) => (uint)code < 256 && (uint)x < 8 && (uint)y < (uint)Height && (rows[code * Height + y] & (0x80 >> x)) != 0;
    /// <summary>Riga del glifo come byte (bit 7 = pixel più a sinistra).</summary>
    public byte Row(int code, int y) => (uint)code < 256 && (uint)y < (uint)Height ? rows[code * Height + y] : throw new ArgumentOutOfRangeException(nameof(code));

    /// <summary>Centra i glifi in una cella più alta. I glifi di blocco e linea 0xB0–0xDF ripetono le righe di bordo, così restano uniti.
    /// Approssimazione per altezze che la sorgente non fornisce (es. Windows: solo 8x8 e 8x12 OEM).
    /// Con un'altezza minore o uguale restituisce questo font invariato: non ritaglia.</summary>
    public PCFont Padded(int height)
    {
        if (height is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(height));
        if (height <= Height) return this;
        var padded = new byte[256 * height]; int top = (height - Height) / 2;
        for (int c = 0; c < 256; c++)
        {
            Array.Copy(rows, c * Height, padded, c * height + top, Height);
            if (c is < 0xB0 or > 0xDF) continue;
            for (int y = 0; y < top; y++) padded[c * height + y] = rows[c * Height];
            for (int y = top + Height; y < height; y++) padded[c * height + y] = rows[c * Height + Height - 1];
        }
        return new(height, padded);
    }

    /// <summary>Primo font RT_FONT di un eseguibile NE (.fon a 16 bit). Null se il formato non è riconosciuto.</summary>
    public static PCFont? TryParseFon(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x40 || data[0] != 'M' || data[1] != 'Z') return null;
        long ne = U32(data, 0x3C); if (ne + 0x40 > data.Length || data[(int)ne] != 'N' || data[(int)ne + 1] != 'E') return null;
        int table = (int)ne + U16(data, (int)ne + 0x24), p = table + 2; if (p > data.Length - 2) return null;
        int shift = U16(data, table); if (shift > 15) return null;
        while (p + 8 <= data.Length)
        {
            int type = U16(data, p), count = U16(data, p + 2), entry = p + 8; if (type == 0) return null;
            if (type == 0x8008)
            {
                for (int i = 0; i < count && entry + 4 <= data.Length; i++, entry += 12)
                {
                    long offset = (long)U16(data, entry) << shift, length = Math.Min((long)U16(data, entry + 2) << shift, data.Length - offset);
                    if (length > 0 && TryParseFnt(data.Slice((int)offset, (int)length)) is { } font) return font;
                }
                return null;
            }
            p = entry + 12 * count;
        }
        return null;
    }

    /// <summary>Risorsa FNT Windows 2.00/3.00 a passo fisso di 8 pixel. Codici fuori dall'intervallo del file restano vuoti.</summary>
    public static PCFont? TryParseFnt(ReadOnlySpan<byte> data)
    {
        if (data.Length < 118) return null; int version = U16(data, 0); if (version is not (0x200 or 0x300)) return null;
        int height = U16(data, 88), first = data[95], last = data[96]; if (height is < 1 or > 32 || first > last) return null;
        int table = version == 0x300 ? 148 : 118, entrySize = version == 0x300 ? 6 : 4;
        if (table + (long)(last - first + 1) * entrySize > data.Length) return null;
        var rows = new byte[256 * height];
        for (int c = first; c <= last; c++)
        {
            int e = table + (c - first) * entrySize; long offset = entrySize == 6 ? U32(data, e + 2) : U16(data, e + 2);
            if (U16(data, e) != 8 || offset + height > data.Length) return null; // v2/v3: colonne da 8 pixel, una per glifo largo 8
            data.Slice((int)offset, height).CopyTo(rows.AsSpan(c * height, height));
        }
        return new(height, rows);
    }

    /// <summary>Carica un .fon, un dump .rom (altezza = lunghezza/256) o una risorsa FNT. Null se il file manca o non è valido.</summary>
    public static PCFont? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path)) return null; var data = File.ReadAllBytes(path);
            if (path.EndsWith(".fon", StringComparison.OrdinalIgnoreCase)) return TryParseFon(data);
            if (!path.EndsWith(".rom", StringComparison.OrdinalIgnoreCase)) return TryParseFnt(data);
            return data.Length % 256 == 0 && data.Length / 256 is >= 1 and <= 32 ? FromRom(data, data.Length / 256) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    private static readonly Dictionary<(string, int), PCFont?> Cache = [];
    /// <summary>Font IBM per celle 8, 14 o 16 righe. In ogni cartella, in ordine: ibm8xN.rom (esatto), poi i .fon OEM di Windows
    /// (cga80woa 8x8 esatto; ega80woa/vgaoem 8x12 centrati con <see cref="Padded"/>). Senza cartelle usa la cartella Fonts di Windows;
    /// fuori da Windows, senza cartelle, restituisce null. Risultati in cache per cartella.</summary>
    public static PCFont? Find(int cellHeight, params string?[] folders)
    {
        if (cellHeight is not (8 or 14 or 16)) return null;
        if (folders.Length == 0)
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (windows.Length == 0) return null; // altrimenti "Fonts" sarebbe relativo alla directory di lavoro
            folders = [Path.Combine(windows, "Fonts")];
        }
        lock (Cache)
            foreach (var folder in folders)
            {
                if (string.IsNullOrEmpty(folder)) continue;
                if (!Cache.TryGetValue((folder, cellHeight), out var font)) Cache[(folder, cellHeight)] = font = Load(folder, cellHeight);
                if (font is not null) return font;
            }
        return null;
    }
    private static PCFont? Load(string folder, int height)
    {
        if (TryLoad(Path.Combine(folder, $"ibm8x{height}.rom")) is { } exact && exact.Height == height) return exact;
        var font = TryLoad(Path.Combine(folder, height == 8 ? "cga80woa.fon" : height == 14 ? "ega80woa.fon" : "vgaoem.fon"));
        return font is null || font.Height > height ? null : font.Padded(height);
    }
    private static int U16(ReadOnlySpan<byte> d, int i) => i + 2 > d.Length ? 0 : d[i] | d[i + 1] << 8;
    private static uint U32(ReadOnlySpan<byte> d, int i) => i + 4 > d.Length ? 0 : (uint)(d[i] | d[i + 1] << 8 | d[i + 2] << 16 | d[i + 3] << 24);
}
