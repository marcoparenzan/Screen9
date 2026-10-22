namespace Screen9;

public sealed partial class PCCanvas
{
    /// <summary>Circle/ellipse outline with optional counterclockwise arc in radians. No QBasic negative-angle radial lines.</summary>
    public void Circle(int x, int y, double radius, int color, double start = 0, double end = Math.PI * 2, double aspect = 1)
    {
        CheckColor(color);
        if (!double.IsFinite(radius) || radius < 0 || radius > int.MaxValue || !double.IsFinite(aspect) || aspect <= 0 || radius * aspect > int.MaxValue
            || !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end < 0 || start > Math.PI * 2 || end > Math.PI * 2) throw new ArgumentOutOfRangeException(nameof(radius));
        int rx = (int)Math.Round(radius), ry = (int)Math.Round(radius * aspect);
        if (Math.Abs(end - start) >= Math.PI * 2 - 1e-12) { Ellipse(x, y, rx, ry, color); return; }
        if (rx == 0 || ry == 0) { throw new ArgumentException("Degenerate ellipse arcs are not supported; use a full ellipse or line."); }
        int left = (int)Math.Clamp((long)x - rx, 0, Width), right = (int)Math.Clamp((long)x + rx + 1, 0, Width);
        int top = (int)Math.Clamp((long)y - ry, 0, Height), bottom = (int)Math.Clamp((long)y + ry + 1, 0, Height);
        bool Inside(long px, long py) { double dx = ((double)px - x) / rx, dy = ((double)py - y) / ry; return dx * dx + dy * dy <= 1.000000000001; }
        for (int py = top; py < bottom; py++) for (int px = left; px < right; px++)
        {
            if (!Inside(px, py) || Inside((long)px - 1, py) && Inside((long)px + 1, py) && Inside(px, (long)py - 1) && Inside(px, (long)py + 1)) continue;
            double angle = Math.Atan2((double)y - py, (double)px - x); if (angle < 0) angle += Math.PI * 2;
            if (start <= end ? angle >= start - 1e-12 && angle <= end + 1e-12 : angle >= start - 1e-12 || angle <= end + 1e-12) PSet(px, py, color);
        }
    }
    /// <summary>Bounded four-connected scanline fill. Without border, replaces the seed color; with border, fills nonborder/nonfill pixels.
    /// Filled pixels never match again, so no visited map is needed.</summary>
    public int Paint(int x, int y, int color, int? border = null, CancellationToken token = default)
    {
        CheckColor(color); if (border is { } b) CheckColor(b); token.ThrowIfCancellationRequested();
        int seed = Point(x, y); if (seed < 0 || seed == color || seed == border) return 0;
        var page = pixels[activePage]; var pending = new Stack<int>(); pending.Push(y * Width + x); int changed = 0;
        bool Fillable(int position) => border is { } edge ? page[position] != edge && page[position] != color : page[position] == seed;
        void PushSpans(int from, int to)
        {
            bool inSpan = false;
            for (int i = from; i <= to; i++) { bool fill = Fillable(i); if (fill && !inSpan) pending.Push(i); inSpan = fill; }
        }
        while (pending.TryPop(out int position))
        {
            token.ThrowIfCancellationRequested(); if (!Fillable(position)) continue;
            int row = position / Width, start = row * Width, left = position, right = position;
            while (left > start && Fillable(left - 1)) left--;
            while (right < start + Width - 1 && Fillable(right + 1)) right++;
            for (int i = left; i <= right; i++) Store(i - start, row, color);
            changed += right - left + 1;
            if (row > 0) PushSpans(left - Width, right - Width);
            if (row + 1 < Height) PushSpans(left + Width, right + Width);
        }
        return changed;
    }
    public PCImage GetImage(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width < 1 || height < 1 || (long)x + width > Width || (long)y + height > Height) throw new ArgumentOutOfRangeException(nameof(x));
        var result = new byte[checked(width * height)];
        for (int row = 0; row < height; row++) Array.Copy(pixels[activePage], (y + row) * Width + x, result, row * width, width);
        return new(width, height, ColorCount, result, trusted: true);
    }
    /// <summary>Scrive in un colpo un rettangolo di indici di colore (righe consecutive in <paramref name="indices"/>) nella pagina
    /// attiva, con la stessa VRAM di <see cref="PSet"/>; ritaglia ciò che esce dallo schermo. È il percorso veloce per chi compone
    /// il fotogramma da sé (PutImage passa da PSet pixel per pixel).</summary>
    public void PutPixels(ReadOnlySpan<byte> indices, int x, int y, int width, int height)
    {
        if (width < 0 || height < 0 || (long)width * height != indices.Length) throw new ArgumentException("indices must hold width * height colors.", nameof(indices));
        foreach (byte index in indices) if (index >= ColorCount) throw new ArgumentOutOfRangeException(nameof(indices), "Color index out of range.");
        int left = (int)Math.Clamp(-(long)x, 0, width), right = (int)Math.Clamp((long)Width - x, 0, width);
        int top = (int)Math.Clamp(-(long)y, 0, height), bottom = (int)Math.Clamp((long)Height - y, 0, height);
        if (left >= right) return;
        var target = pixels[activePage];
        for (int row = top; row < bottom; row++)
        {
            int py = y + row, start = py * Width + x + left;
            indices.Slice(row * width + left, right - left).CopyTo(target.AsSpan(start));
            for (int column = left; column < right; column++) EncodeMemory(x + column, py, indices[row * width + column]);
        }
    }
    public void PutImage(PCImage image, int x, int y, PCPixelOperation operation = PCPixelOperation.Copy, int? transparent = null, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(image); if (image.Colors != ColorCount) throw new ArgumentException("Sprite color depth differs from canvas.");
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation)); if (transparent is { } color) CheckColor(color);
        token.ThrowIfCancellationRequested(); int mask = ColorCount - 1;
        int left = (int)Math.Clamp(-(long)x, 0, image.Width), right = (int)Math.Clamp((long)Width - x, 0, image.Width);
        int top = (int)Math.Clamp(-(long)y, 0, image.Height), bottom = (int)Math.Clamp((long)Height - y, 0, image.Height);
        for (int row = top; row < bottom; row++)
        {
            token.ThrowIfCancellationRequested();
            for (int column = left; column < right; column++)
            {
                int source = image[column, row]; if (source == transparent) continue;
                int px = (int)((long)x + column), py = (int)((long)y + row), destination = Point(px, py);
                int result = operation switch { PCPixelOperation.Copy => source, PCPixelOperation.Preset => source ^ mask, PCPixelOperation.Xor => source ^ destination, PCPixelOperation.And => source & destination, PCPixelOperation.Or => source | destination, _ => throw new ArgumentOutOfRangeException(nameof(operation)) };
                PSet(px, py, result);
            }
        }
    }
}
