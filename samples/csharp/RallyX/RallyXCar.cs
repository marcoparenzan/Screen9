namespace RallyX;

public enum RallyXDir { Stop, Up, Right, Down, Left }

/// <summary>A car (player or enemy), from the logic of Car.js: constant speed along the axes, it turns only if the
/// 16 pixel box fits in the neighbouring corridor, and on hitting a wall it picks another direction. Units: 1x pixels and
/// steps of 1/60 s; the car centre is (X, Y). The animation follows the 12 rotation frames of the sheet.</summary>
public sealed class RallyXCar
{
    // Speed is 100 px/s: the 400 px/s of the original project, which worked at 4x. Step is the simulation step (60 per second).
    // FuelPerStep = 1/50 at 60 steps per second is 1.2 units per second: 100 fuel lasts about 83 seconds.
    public const double Size = 16, Speed = 100, Step = 1.0 / 60, FuelPerStep = 1.0 / 50;
    // Epsilon: a car touching a wall edge without entering it does not count as a hit (comparisons on the exact edge).
    private const double Half = Size / 2, Epsilon = 1e-9;
    private const int FramesPerType = 12, ExplodeFrame = 48, StepsPerFrame = 3; // the animation runs at 20 frames per second

    // The sheet has 12 frames per row (one every 30 degrees of rotation, 0 = up, 3 = right, 6 = down, 9 = left) and one row per car
    // type; typeBase is the first frame of the car row. The explosion frame (48) lies outside the rows.
    private readonly int typeBase;
    private readonly double homeX, homeY, initialFuel;
    private int[] frames = [0];
    private int frameIndex, frameSteps;
    private bool animating, looping;

    public double X { get; private set; }
    public double Y { get; private set; }
    public double Vx { get; private set; }
    public double Vy { get; private set; }
    public RallyXDir Facing { get; private set; }
    public bool Suspended { get; private set; } = true;
    public bool HasFuel { get; }
    public double Fuel { get; set; }
    /// <summary>Frame index in the car sheet (12 per row: up, 30 deg, 60 deg, right, ...; 48 is the explosion).</summary>
    public int Frame => frames[frameIndex];

    public RallyXCar(int type, int tileX, int tileY, double? fuel = null)
    {
        typeBase = type * FramesPerType;
        homeX = RallyXWorld.CenterOf(tileX); homeY = RallyXWorld.CenterOf(tileY);
        HasFuel = fuel is not null; initialFuel = fuel ?? 0;
        Reset();
    }

    public int TileX => RallyXWorld.TileOf(X);
    public int TileY => RallyXWorld.TileOf(Y);

    public void Reset()
    {
        X = homeX; Y = homeY; Vx = Vy = 0; Facing = RallyXDir.Stop;
        Fuel = initialFuel;
        Play([Heading(RallyXDir.Up)], false);
        animating = false;
    }

    /// <summary>Stops the car and excludes it from physics and fuel use until <see cref="Continue"/> is called.</summary>
    public void Suspend() { Stop(); Suspended = true; }

    /// <summary>Resumes in the given direction, or in the one it was going.</summary>
    public void Continue(RallyXDir? direction = null)
    {
        Suspended = false;
        var d = direction ?? Facing;
        if (d == RallyXDir.Stop) Stop(); else Go(d);
    }

    public void Stop() { Vx = Vy = 0; animating = false; }

    public void Reverse()
    {
        switch (Facing)
        {
            case RallyXDir.Right: Go(RallyXDir.Left); break;
            case RallyXDir.Left: Go(RallyXDir.Right); break;
            case RallyXDir.Down: Go(RallyXDir.Up); break;
            case RallyXDir.Up: Go(RallyXDir.Down); break;
        }
    }

    /// <summary>Sets the driving direction, with the turn animation from the previous direction.</summary>
    public void Go(RallyXDir direction)
    {
        if (direction == RallyXDir.Stop) { Stop(); return; }
        var source = Facing;
        if (source == direction && MovingTowards(direction)) return; // already moving this way: no animation to restart
        Play(source == RallyXDir.Stop || source == direction ? [Heading(direction)] : Turn(source, direction), false);
        Vx = direction switch { RallyXDir.Right => Speed, RallyXDir.Left => -Speed, _ => 0 };
        Vy = direction switch { RallyXDir.Down => Speed, RallyXDir.Up => -Speed, _ => 0 };
        Facing = direction;
    }

    private bool MovingTowards(RallyXDir direction) => direction switch
    {
        RallyXDir.Right => Vx > 0, RallyXDir.Left => Vx < 0, RallyXDir.Down => Vy > 0, RallyXDir.Up => Vy < 0, _ => false
    };

    /// <summary>Whether it can turn: the neighbouring tile is road and the car box fits entirely in its corridor.</summary>
    public bool CanTurn(RallyXWorld world, RallyXDir direction)
    {
        int dx = direction switch { RallyXDir.Right => 1, RallyXDir.Left => -1, _ => 0 };
        int dy = direction switch { RallyXDir.Down => 1, RallyXDir.Up => -1, _ => 0 };
        if (dx == 0 && dy == 0) return false;
        int tx = TileX + dx, ty = TileY + dy;
        if (!world.Exists(tx, ty) || world.IsSolid(tx, ty)) return false;
        if (dx != 0) { double top = ty * RallyXWorld.TileSize; return Y - Half >= top && Y + Half <= top + RallyXWorld.TileSize; }
        double left = tx * RallyXWorld.TileSize;
        return X - Half >= left && X + Half <= left + RallyXWorld.TileSize;
    }

    /// <summary>Enemy chasing: towards the given tile along the axis with the larger distance, if it can turn.</summary>
    public void Follow(RallyXWorld world, int tileX, int tileY)
    {
        if (Suspended) return;
        double dx = RallyXWorld.CenterOf(tileX) - X, dy = RallyXWorld.CenterOf(tileY) - Y;
        RallyXDir wanted = Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? RallyXDir.Right : RallyXDir.Left) : (dy > 0 ? RallyXDir.Down : RallyXDir.Up);
        if (CanTurn(world, wanted)) Go(wanted);
    }

    /// <summary>One physics step and, on hitting a wall, the choice of the new direction as in Car.js.</summary>
    public void Update(RallyXWorld world)
    {
        if (Suspended) return;
        if (HasFuel) Fuel -= FuelPerStep;
        // Motion is always on a single axis. Advance one step; if the box enters a wall it is put back on the tile edge
        // and the velocity is cancelled, then Deflect chooses where to go (like the collision callback of Car.js).
        var blocked = RallyXDir.Stop;
        if (Vx != 0)
        {
            double next = X + Vx * Step;
            if (HitsWall(world, next, Y))
            {
                blocked = Vx > 0 ? RallyXDir.Right : RallyXDir.Left;
                next = Vx > 0 ? Math.Floor((next + Half - Epsilon) / RallyXWorld.TileSize) * RallyXWorld.TileSize - Half
                              : (Math.Floor((next - Half) / RallyXWorld.TileSize) + 1) * RallyXWorld.TileSize + Half;
                Vx = 0;
            }
            X = next;
        }
        else if (Vy != 0)
        {
            double next = Y + Vy * Step;
            if (HitsWall(world, X, next))
            {
                blocked = Vy > 0 ? RallyXDir.Down : RallyXDir.Up;
                next = Vy > 0 ? Math.Floor((next + Half - Epsilon) / RallyXWorld.TileSize) * RallyXWorld.TileSize - Half
                              : (Math.Floor((next - Half) / RallyXWorld.TileSize) + 1) * RallyXWorld.TileSize + Half;
                Vy = 0;
            }
            Y = next;
        }
        if (blocked != RallyXDir.Stop) Deflect(world, blocked);
    }

    /// <summary>After a hit it tries the other directions in the order of Car.js: against a wall above, first right, then left, then
    /// back; the order differs for each side and makes cars turn predictably (and identically to the original).</summary>
    private void Deflect(RallyXWorld world, RallyXDir blocked)
    {
        RallyXDir[] order = blocked switch
        {
            RallyXDir.Up => [RallyXDir.Right, RallyXDir.Left, RallyXDir.Down],
            RallyXDir.Left => [RallyXDir.Up, RallyXDir.Down, RallyXDir.Right],
            RallyXDir.Down => [RallyXDir.Left, RallyXDir.Right, RallyXDir.Up],
            _ => [RallyXDir.Down, RallyXDir.Up, RallyXDir.Left]
        };
        foreach (var direction in order)
            if (CanTurn(world, direction)) { Go(direction); return; }
    }

    /// <summary>The car box (16x16 centred at centerX, centerY) touches at least one wall tile. It checks the tiles that
    /// cover it: at most 2x2.</summary>
    private static bool HitsWall(RallyXWorld world, double centerX, double centerY)
    {
        int x0 = RallyXWorld.TileOf(centerX - Half), x1 = RallyXWorld.TileOf(centerX + Half - Epsilon);
        int y0 = RallyXWorld.TileOf(centerY - Half), y1 = RallyXWorld.TileOf(centerY + Half - Epsilon);
        for (int ty = y0; ty <= y1; ty++)
            for (int tx = x0; tx <= x1; tx++)
                if (world.IsSolid(tx, ty)) return true;
        return false;
    }

    /// <summary>Strict overlap of two 16x16 boxes (same size for cars, flags, rocks and smoke).</summary>
    public bool Overlaps(double otherX, double otherY) => Math.Abs(X - otherX) < Size && Math.Abs(Y - otherY) < Size;

    /// <summary>Pushes the car out of a fixed object (rock, smoke): along the axis of least overlap.</summary>
    public void Separate(double otherX, double otherY)
    {
        double dx = X - otherX, dy = Y - otherY, overlapX = Size - Math.Abs(dx), overlapY = Size - Math.Abs(dy);
        if (overlapX <= 0 || overlapY <= 0) return;
        if (overlapX < overlapY) X += dx >= 0 ? overlapX : -overlapX; else Y += dy >= 0 ? overlapY : -overlapY;
    }

    public void Explode() { Play([ExplodeFrame], false, absolute: true); animating = false; }

    /// <summary>An enemy car in smoke: it stops and spins in place.</summary>
    public void Smoked() { Suspend(); Play([0, 1, 2, 3, 4, 5, 6], true); }

    /// <summary>One animation step (20 frames per second).</summary>
    public void Tick()
    {
        if (!animating || ++frameSteps < StepsPerFrame) return;
        frameSteps = 0;
        if (frameIndex + 1 < frames.Length) frameIndex++;
        else if (looping) frameIndex = 0;
        else animating = false;
    }

    private void Play(int[] relative, bool loop, bool absolute = false)
    {
        frames = absolute ? relative : relative.Select(f => f + typeBase).ToArray();
        frameIndex = 0; frameSteps = 0; looping = loop; animating = true;
    }

    private static int Heading(RallyXDir d) => d switch { RallyXDir.Up => 0, RallyXDir.Right => 3, RallyXDir.Down => 6, _ => 9 };

    /// <summary>The frames of a turn: 90 degrees along the shortest arc, 180 always clockwise (from left to right through up).</summary>
    private static int[] Turn(RallyXDir from, RallyXDir to)
    {
        int start = Heading(from), difference = (Heading(to) - start + FramesPerType) % FramesPerType;
        int step = difference == 9 ? -1 : 1, count = difference == 9 ? 3 : difference;
        return Enumerable.Range(0, count + 1).Select(i => (start + step * i + FramesPerType) % FramesPerType).ToArray();
    }
}
