namespace Screen9;

/// <summary>Destinazione di un punto già ritagliato. Implementata da struct: niente closure né allocazioni per pixel.</summary>
internal interface IPlotter
{
    void Plot(int x, int y);
}

/// <summary>Rasterizzatori condivisi da Canvas e PCCanvas. Ogni punto emesso è dentro canvasWidth × canvasHeight.</summary>
internal static class Raster
{
    public static void FillRectangle<T>(int canvasWidth, int canvasHeight, int x, int y, int width, int height, ref T plot)
        where T : struct, IPlotter
    {
        ValidateSize(width, height);
        int left = (int)Math.Clamp((long)x, 0, canvasWidth);
        int top = (int)Math.Clamp((long)y, 0, canvasHeight);
        int right = (int)Math.Clamp((long)x + width, 0, canvasWidth);
        int bottom = (int)Math.Clamp((long)y + height, 0, canvasHeight);
        for (int row = top; row < bottom; row++)
            for (int column = left; column < right; column++) plot.Plot(column, row);
    }

    public static void Rectangle<T>(int canvasWidth, int canvasHeight, int x, int y, int width, int height, ref T plot)
        where T : struct, IPlotter
    {
        ValidateSize(width, height);
        if (width == 0 || height == 0) return;
        double right = (long)x + width - 1, bottom = (long)y + height - 1;
        Line(canvasWidth, canvasHeight, x, y, right, y, ref plot);
        Line(canvasWidth, canvasHeight, x, bottom, right, bottom, ref plot);
        Line(canvasWidth, canvasHeight, x, y, x, bottom, ref plot);
        Line(canvasWidth, canvasHeight, right, y, right, bottom, ref plot);
    }

    private static void ValidateSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
    }

    // Liang–Barsky prima del raster Bresenham: anche coordinate estreme hanno costo limitato.
    public static void Line<T>(int canvasWidth, int canvasHeight, double x1, double y1, double x2, double y2, ref T plot)
        where T : struct, IPlotter
    {
        double dx = x2 - x1, dy = y2 - y1, start = 0, end = 1;
        bool Clip(double p, double q)
        {
            if (p == 0) return q >= 0;
            double t = q / p;
            if (p < 0) { if (t > end) return false; start = Math.Max(start, t); }
            else { if (t < start) return false; end = Math.Min(end, t); }
            return true;
        }
        if (!Clip(-dx, x1) || !Clip(dx, canvasWidth - 1 - x1) ||
            !Clip(-dy, y1) || !Clip(dy, canvasHeight - 1 - y1)) return;
        int ax = (int)Math.Clamp(Math.Round(x1 + start * dx), 0, canvasWidth - 1);
        int ay = (int)Math.Clamp(Math.Round(y1 + start * dy), 0, canvasHeight - 1);
        int bx = (int)Math.Clamp(Math.Round(x1 + end * dx), 0, canvasWidth - 1);
        int by = (int)Math.Clamp(Math.Round(y1 + end * dy), 0, canvasHeight - 1);
        long deltaX = Math.Abs((long)bx - ax), deltaY = -Math.Abs((long)by - ay);
        int sx = ax < bx ? 1 : -1, sy = ay < by ? 1 : -1;
        long error = deltaX + deltaY;
        while (true)
        {
            plot.Plot(ax, ay);
            if (ax == bx && ay == by) break;
            long twice = 2 * error;
            if (twice >= deltaY) { error += deltaY; ax += sx; }
            if (twice <= deltaX) { error += deltaX; ay += sy; }
        }
    }

    /// <summary>Ellisse limitata al viewport: piena o contorno interno di una cella.</summary>
    public static void Ellipse<T>(int canvasWidth, int canvasHeight, int centerX, int centerY, int radiusX, int radiusY, bool filled, ref T plot)
        where T : struct, IPlotter
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radiusX); ArgumentOutOfRangeException.ThrowIfNegative(radiusY);
        if (radiusX == 0) { Line(canvasWidth, canvasHeight, centerX, (long)centerY - radiusY, centerX, (long)centerY + radiusY, ref plot); return; }
        if (radiusY == 0) { Line(canvasWidth, canvasHeight, (long)centerX - radiusX, centerY, (long)centerX + radiusX, centerY, ref plot); return; }
        int left = (int)Math.Clamp((long)centerX - radiusX, 0, canvasWidth), right = (int)Math.Clamp((long)centerX + radiusX + 1, 0, canvasWidth);
        int top = (int)Math.Clamp((long)centerY - radiusY, 0, canvasHeight), bottom = (int)Math.Clamp((long)centerY + radiusY + 1, 0, canvasHeight);
        bool Inside(long x, long y) { double dx = ((double)x - centerX) / radiusX, dy = ((double)y - centerY) / radiusY; return dx * dx + dy * dy <= 1.000000000001; }
        for (int y = top; y < bottom; y++) for (int x = left; x < right; x++)
            if (Inside(x, y) && (filled || !Inside((long)x - 1, y) || !Inside((long)x + 1, y) || !Inside(x, (long)y - 1) || !Inside(x, (long)y + 1))) plot.Plot(x, y);
    }
}
