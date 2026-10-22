using Screen9;

namespace RallyX;

/// <summary>Composes a frame in an indexed buffer (320x240 at true resolution, then doubled to 640x480 for
/// mode 12). The radar panel and the large texts
/// are already drawn at 640x480. Each Render* returns the internal buffer (640x480 indices, valid until the next Render).</summary>
public sealed class RallyXRenderer(RallyXAssets assets)
{
    public const int LogicalWidth = 320, LogicalHeight = 240, Scale = 2, Width = LogicalWidth * Scale, Height = LogicalHeight * Scale;
    private const int HudX = RallyXSim.ViewWidth, CarSize = 16, TileSize = RallyXWorld.TileSize;
    // 5x6 digits in the same style as the HUD lettering.
    private static readonly string[] Digits =
    [
        "01110 10001 10001 10001 10001 01110", "00100 01100 00100 00100 00100 01110", "01110 10001 00010 00100 01000 11111",
        "11110 00001 01110 00001 00001 11110", "00010 00110 01010 10010 11111 00010", "11111 10000 11110 00001 10001 01110",
        "01110 10000 11110 10001 10001 01110", "11111 00001 00010 00100 01000 01000", "01110 10001 01110 10001 10001 01110",
        "01110 10001 01111 00001 00001 01110"
    ];

    private readonly byte[] logical = new byte[LogicalWidth * LogicalHeight];
    private readonly byte[] frame = new byte[Width * Height];
    private readonly PCFont font = PCFont.Vga8x16;

    /// <summary>The match: world with the camera, sprites, HUD, radar, end-of-round curtain and status banners.</summary>
    public byte[] RenderGame(RallyXSim sim, int highScore, bool paused)
    {
        Array.Fill(logical, (byte)RallyXAssets.Road); // the road is sand: wall and grass tiles are drawn on top of it
        DrawWorld(sim);
        DrawHud(sim, highScore);
        if (sim.ClosingY < LogicalHeight) Fill(0, Math.Max(sim.ClosingY, 0), LogicalWidth, LogicalHeight, RallyXAssets.Black);
        Upscale(logical, LogicalWidth, LogicalHeight);
        DrawRadar(sim);

        int centerX = HudX * Scale / 2, centerY = 120; // above the car, which sits at the centre of the field
        if (paused) Banner("PAUSE", centerX, centerY);
        else if (sim.Phase == RallyXPhase.GameOver) Banner("GAME OVER", centerX, centerY);
        else if (sim.Waiting) Banner($"ROUND {sim.Round}", centerX, centerY);
        return frame;
    }

    /// <summary>The title (own palette): the game image, without logo, with instructions and best score.</summary>
    public byte[] RenderTitle(int highScore, bool showPrompt)
    {
        Upscale(assets.Title.Pixels, LogicalWidth, LogicalHeight);
        int black = assets.TitleBlack, accent = assets.TitleAccent;
        Text("SCREEN9 - C# EDITION", Width / 2, 24, black, 2, center: true);
        Text("ARROWS / WASD   DRIVE", Width / 2, 84, black, 1, center: true);
        Text("SPACE           SMOKE", Width / 2, 106, black, 1, center: true);
        Text("P  PAUSE     ESC  QUIT", Width / 2, 128, black, 1, center: true);
        Text($"HI-SCORE {highScore}", Width / 2, 164, accent, 1, center: true);
        if (showPrompt) Text("PRESS SPACE TO START", Width / 2, 204, accent, 2, center: true);
        return frame;
    }

    private void DrawWorld(RallyXSim sim)
    {
        int cameraX = (int)Math.Round(sim.CameraX), cameraY = (int)Math.Round(sim.CameraY);
        for (int ty = cameraY / TileSize; ty <= (cameraY + LogicalHeight - 1) / TileSize; ty++)
            for (int tx = cameraX / TileSize; tx <= (cameraX + RallyXSim.ViewWidth - 1) / TileSize; tx++)
            {
                int gid = sim.World.Gid(tx, ty);
                if (gid <= 1) continue; // road: already in the background
                int tile = gid - 1, columns = assets.Tiles.Width / TileSize;
                Blit(assets.Tiles, tile % columns * TileSize, tile / columns * TileSize, TileSize, TileSize, tx * TileSize - cameraX, ty * TileSize - cameraY, RallyXSim.ViewWidth);
            }

        void Sprite(RallyXSheet sheet, int index, double x, double y)
        {
            int columns = sheet.Width / CarSize;
            Blit(sheet, index % columns * CarSize, index / columns * CarSize, CarSize, CarSize, (int)Math.Round(x - CarSize / 2.0) - cameraX, (int)Math.Round(y - CarSize / 2.0) - cameraY, RallyXSim.ViewWidth);
        }
        foreach (var smoke in sim.Smokes) Sprite(assets.Smoke, 0, smoke.X, smoke.Y);
        foreach (var rock in sim.Rocks) Sprite(assets.Rocks, 0, rock.X, rock.Y);
        foreach (var flag in sim.Flags) Sprite(assets.Flags, flag.Frame, flag.X, flag.Y);
        foreach (var enemy in sim.Enemies) Sprite(assets.Cars, enemy.Frame, enemy.X, enemy.Y);
        Sprite(assets.Cars, sim.Player.Frame, sim.Player.X, sim.Player.Y);
    }

    /// <summary>The HUD is the background image plus some dynamic parts. Coordinates are those of Hud.js in the original divided by 4
    /// (it worked at 4x): record at row 7, score at 18, fuel bar rows 49-53, lives at 144, round at 155.</summary>
    private void DrawHud(RallyXSim sim, int highScore)
    {
        Blit(assets.Hud, 0, 0, assets.Hud.Width, assets.Hud.Height, HudX, 0, LogicalWidth, opaque: true);
        Number(highScore, 7, RallyXAssets.Red);
        Fill(HudX, 24, LogicalWidth, 32 + 1, RallyXAssets.Black); // there is no 2UP: the row stays black
        Number(sim.Score, 18, RallyXAssets.Cyan);
        int fuel = (int)Math.Round(Math.Clamp(sim.Player.Fuel, 0, 100) * (LogicalWidth - HudX) / 100.0);
        Fill(LogicalWidth - fuel, 49, LogicalWidth, 54, RallyXAssets.Yellow);
        int lives = Math.Clamp(sim.Lives, 0, 4);
        Fill(HudX + lives * 12, 144, LogicalWidth, 153, RallyXAssets.Black);
        Number(sim.Round, 155, RallyXAssets.White);
    }

    /// <summary>Radar: one map tile is 3x3 pixels of mode 12; the blue panel has exactly the room (32x56 tiles).</summary>
    private void DrawRadar(RallyXSim sim)
    {
        int left = HudX * Scale, top = 120, limit = Math.Min(Height, Math.Max(sim.ClosingY, 0) * Scale);
        void Dot(int tileX, int tileY, int color)
        {
            int x = left + (tileX - 5) * 3, y = top + (tileY - 4) * 3;
            if (tileX < 5 || tileX > 36 || tileY < 4 || tileY > 59 || y + 3 > limit) return;
            for (int dy = 0; dy < 3; dy++) Array.Fill(frame, (byte)color, (y + dy) * Width + x, 3);
        }
        foreach (var flag in sim.Flags) if (!flag.Caught) Dot(flag.TileX, flag.TileY, RallyXAssets.Yellow);
        foreach (var enemy in sim.Enemies) Dot(enemy.TileX, enemy.TileY, RallyXAssets.Red);
        Dot(sim.Player.TileX, sim.Player.TileY, RallyXAssets.White);
    }

    /// <summary>Copies a rectangle of the sheet into the logical buffer, clipped on the right at <paramref name="clipRight"/> (the field
    /// ends where the HUD begins). Transparent pixels (Clear) are skipped, unless <paramref name="opaque"/>.</summary>
    private void Blit(RallyXSheet sheet, int sx, int sy, int width, int height, int dx, int dy, int clipRight, bool opaque = false)
    {
        for (int y = 0; y < height; y++)
        {
            int ty = dy + y;
            if ((uint)ty >= LogicalHeight) continue;
            for (int x = 0; x < width; x++)
            {
                int tx = dx + x;
                if ((uint)tx >= (uint)clipRight || tx < 0) continue;
                byte pixel = sheet[sx + x, sy + y];
                if (pixel == RallyXSheet.Clear && !opaque) continue;
                logical[ty * LogicalWidth + tx] = pixel == RallyXSheet.Clear ? (byte)RallyXAssets.Black : pixel;
            }
        }
    }

    private void Fill(int left, int top, int right, int bottom, int color)
    {
        for (int y = Math.Max(top, 0); y < Math.Min(bottom, LogicalHeight); y++)
            Array.Fill(logical, (byte)color, y * LogicalWidth + Math.Max(left, 0), Math.Min(right, LogicalWidth) - Math.Max(left, 0));
    }

    /// <summary>Number right-aligned to the panel, 5x6 digits with a space.</summary>
    private void Number(int value, int top, int color)
    {
        string digits = Math.Max(value, 0).ToString();
        int x = LogicalWidth - (digits.Length * 6 - 1);
        foreach (char digit in digits)
        {
            var rows = Digits[digit - '0'].Split(' ');
            for (int y = 0; y < 6; y++)
                for (int column = 0; column < 5; column++)
                    if (rows[y][column] == '1') logical[(top + y) * LogicalWidth + x + column] = (byte)color;
            x += 6;
        }
    }

    /// <summary>Doubles every pixel horizontally and vertically (320x240 -> 640x480): a doubled row is written first, then it is
    /// copied below.</summary>
    private void Upscale(byte[] source, int width, int height)
    {
        for (int y = 0; y < height; y++)
        {
            int row = y * Scale * Width;
            for (int x = 0; x < width; x++)
            {
                byte pixel = source[y * width + x];
                frame[row + x * 2] = pixel; frame[row + x * 2 + 1] = pixel;
            }
            Array.Copy(frame, row, frame, row + Width, Width);
        }
    }

    private void Banner(string text, int centerX, int centerY)
    {
        int width = text.Length * 16 + 24, left = centerX - width / 2, top = centerY - 12;
        for (int y = top; y < top + 56; y++) Array.Fill(frame, (byte)RallyXAssets.Black, y * Width + left, width);
        for (int x = left; x < left + width; x++) { frame[top * Width + x] = RallyXAssets.White; frame[(top + 55) * Width + x] = RallyXAssets.White; }
        for (int y = top; y < top + 56; y++) { frame[y * Width + left] = RallyXAssets.White; frame[y * Width + left + width - 1] = RallyXAssets.White; }
        Text(text, centerX, top + 12, RallyXAssets.Yellow, 2, center: true);
    }

    /// <summary>Text with the engine 8x16 ROM font, enlarged by <paramref name="scale"/>.</summary>
    private void Text(string text, int x, int y, int color, int scale, bool center = false)
    {
        int advance = 8 * scale;
        if (center) x -= text.Length * advance / 2;
        foreach (char c in text)
        {
            for (int row = 0; row < 16; row++)
            {
                byte bits = font.Row(c, row);
                for (int column = 0; column < 8; column++)
                {
                    if ((bits & (0x80 >> column)) == 0) continue;
                    for (int dy = 0; dy < scale; dy++)
                        for (int dx = 0; dx < scale; dx++)
                        {
                            int px = x + column * scale + dx, py = y + row * scale + dy;
                            if ((uint)px < Width && (uint)py < Height) frame[py * Width + px] = (byte)color;
                        }
                }
            }
            x += advance;
        }
    }
}
