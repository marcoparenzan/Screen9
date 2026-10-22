namespace RallyX;

/// <summary>An autopilot for the demo and for tests: a breadth-first search finds the nearest flag, avoiding
/// rocks and tiles next to enemies, and steers towards the first tile of the path. It drops smoke when an enemy is close.</summary>
public sealed class RallyXAutopilot
{
    private static readonly (int Dx, int Dy, RallyXDir Direction)[] Moves =
        [(0, -1, RallyXDir.Up), (1, 0, RallyXDir.Right), (0, 1, RallyXDir.Down), (-1, 0, RallyXDir.Left)];

    /// <summary>Call at every step: looks at the match and steers. It first looks for a path that keeps away from enemies, and if
    /// there is none it accepts a riskier one.</summary>
    public void Drive(RallyXSim sim)
    {
        if (sim.Phase != RallyXPhase.Playing || sim.Player.Suspended) return;
        if (sim.Enemies.Any(e => Math.Abs(e.TileX - sim.Player.TileX) + Math.Abs(e.TileY - sim.Player.TileY) <= 3)) sim.DropSmoke();
        var direction = FirstStep(sim, avoidEnemies: true) ?? FirstStep(sim, avoidEnemies: false);
        if (direction is { } d) sim.Steer(d);
    }

    private static RallyXDir? FirstStep(RallyXSim sim, bool avoidEnemies)
    {
        var world = sim.World;
        var blocked = new bool[RallyXWorld.Columns, RallyXWorld.Rows];
        foreach (var rock in sim.Rocks) blocked[rock.TileX, rock.TileY] = true;
        if (avoidEnemies)
            foreach (var enemy in sim.Enemies)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                        if (world.Exists(enemy.TileX + dx, enemy.TileY + dy)) blocked[enemy.TileX + dx, enemy.TileY + dy] = true;
        var goals = sim.Flags.Where(f => !f.Caught).Select(f => (f.TileX, f.TileY)).ToHashSet();
        if (goals.Count == 0) return null;

        // Breadth-first search from the player tile; for each tile reached only the first move of the path is remembered,
        // which is all that is needed to steer. It stops at the first flag found, that is the nearest one in tiles.
        var first = new RallyXDir?[RallyXWorld.Columns, RallyXWorld.Rows];
        var seen = new bool[RallyXWorld.Columns, RallyXWorld.Rows];
        var queue = new Queue<(int X, int Y)>();
        var start = (sim.Player.TileX, sim.Player.TileY);
        seen[start.Item1, start.Item2] = true;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var (dx, dy, direction) in Moves)
            {
                int nx = x + dx, ny = y + dy;
                if (!world.Exists(nx, ny) || seen[nx, ny] || !world.IsRoad(nx, ny) || blocked[nx, ny]) continue;
                seen[nx, ny] = true;
                first[nx, ny] = (x, y) == start ? direction : first[x, y];
                if (goals.Contains((nx, ny))) return first[nx, ny];
                queue.Enqueue((nx, ny));
            }
        }
        return null;
    }
}
