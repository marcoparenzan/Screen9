namespace Screen9;

/// <summary>Testo nei modi grafici come il BIOS video: glifi del font copiati in celle 8 x altezza font.
/// Cursore, a capo e interpretazione dei codici di controllo restano al chiamante.</summary>
public sealed partial class PCCanvas
{
    /// <summary>Disegna il glifo <paramref name="code"/> (0..255, CP437) nella cella column,row (base 0) della pagina attiva,
    /// pixel accesi in foreground e spenti in background. Celle fuori schermo vengono tagliate.</summary>
    public void DrawGlyph(int column, int row, int code, int foreground, int background, PCFont font)
    {
        ArgumentNullException.ThrowIfNull(font); CheckColor(foreground); CheckColor(background);
        if ((uint)code > 255) throw new ArgumentOutOfRangeException(nameof(code));
        // In long: colonne o righe enormi non devono tornare dentro lo schermo per overflow.
        long cellLeft = column * 8L, cellTop = (long)row * font.Height;
        if (cellLeft >= Width || cellTop >= Height || cellLeft + 8 <= 0 || cellTop + font.Height <= 0) return;
        int left = (int)cellLeft, top = (int)cellTop;
        for (int y = 0; y < font.Height; y++)
        {
            int py = top + y; if ((uint)py >= (uint)Height) continue; byte bits = font.Row(code, y);
            for (int x = 0; x < 8; x++) { int px = left + x; if ((uint)px < (uint)Width) Store(px, py, (bits & (0x80 >> x)) != 0 ? foreground : background); }
        }
    }
    /// <summary>Scorre in alto di <paramref name="lines"/> pixel la fascia di righe [top, top+height) della pagina attiva,
    /// riempiendo il fondo con <paramref name="fill"/>. Le righe fuori dalla fascia non cambiano (VIEW PRINT).</summary>
    public void ScrollUp(int top, int height, int lines, int fill)
    {
        CheckColor(fill);
        if (top < 0 || height < 0 || lines < 0 || (long)top + height > Height) throw new ArgumentOutOfRangeException(nameof(top));
        lines = Math.Min(lines, height); if (lines == 0) return;
        int kept = height - lines, bottom = top + kept;
        // Indici: le righe sono contigue, Array.Copy gestisce la sovrapposizione.
        var page = pixels[activePage];
        Array.Copy(page, (top + lines) * Width, page, top * Width, kept * Width);
        Array.Fill(page, (byte)fill, bottom * Width, lines * Width);
        // VRAM: stessa operazione per righe di byte, nel layout del modo.
        if (VideoMode.Interleaved)
        {
            // CGA: le righe alternano due banchi; copia in avanti, la sorgente (più in basso) non è ancora sovrascritta.
            byte pattern = (byte)(ColorCount == 4 ? fill * 0x55 : fill * 0xFF);
            for (int y = top; y < bottom; y++) Array.Copy(memory[0], CgaRow(y + lines), memory[0], CgaRow(y), BytesPerRow);
            for (int y = bottom; y < top + height; y++) Array.Fill(memory[0], pattern, CgaRow(y), BytesPerRow);
            return;
        }
        int origin = VideoMode.Linear ? 0 : activePage * PageBytes;
        for (int p = 0; p < PlaneCount; p++)
        {
            byte pattern = VideoMode.Linear ? (byte)fill : (fill >> p & 1) != 0 ? (byte)0xFF : (byte)0;
            Array.Copy(memory[p], origin + (top + lines) * BytesPerRow, memory[p], origin + top * BytesPerRow, kept * BytesPerRow);
            Array.Fill(memory[p], pattern, origin + bottom * BytesPerRow, lines * BytesPerRow);
        }
    }
    private int CgaRow(int y) => (y & 1) * 8192 + y / 2 * BytesPerRow;
}
