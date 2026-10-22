namespace RallyX;

/// <summary>The map: 42x64 tiles of 24 pixels. Gid 1 is road, above 1 is wall; outside the map counts as wall.</summary>
public sealed class RallyXWorld(RallyXAssets assets)
{
    public const int TileSize = 24, Columns = 42, Rows = 64, PixelWidth = Columns * TileSize, PixelHeight = Rows * TileSize;

    private readonly RallyXSheet map = assets.Map;

    public bool Exists(int tileX, int tileY) => (uint)tileX < Columns && (uint)tileY < Rows;
    public int Gid(int tileX, int tileY) => Exists(tileX, tileY) ? map[tileX, tileY] : 0;
    public bool IsRoad(int tileX, int tileY) => Gid(tileX, tileY) == 1;
    public bool IsSolid(int tileX, int tileY) => !Exists(tileX, tileY) || Gid(tileX, tileY) > 1;

    // Pixel <-> tile conversions: tiles are 24x24 pixels at true resolution (96 in the original project, which worked at 4x).
    public static int TileOf(double pixel) => (int)Math.Floor(pixel / TileSize);
    public static double CenterOf(int tile) => tile * TileSize + TileSize / 2.0;
}
