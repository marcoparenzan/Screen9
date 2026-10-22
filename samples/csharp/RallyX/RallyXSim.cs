namespace RallyX;

public enum RallyXSound { Start, Flag, Smoke, Crash, RoundClear, GameOver }

public enum RallyXPhase { Playing, Crashed, RoundClear, GameOver }

/// <summary>A flag; the tenth (special) one doubles the value of the following ones. Once caught, it shows its score for 3 seconds.</summary>
public sealed class RallyXFlag(int tileX, int tileY, bool special)
{
    public int TileX { get; } = tileX;
    public int TileY { get; } = tileY;
    public bool Special { get; } = special;
    public bool Caught { get; private set; }
    /// <summary>Index in the flag sheet: 0 normal, 1 special; after being caught, the score card.</summary>
    public int Frame { get; private set; } = special ? 1 : 0;
    public double X => RallyXWorld.CenterOf(TileX);
    public double Y => RallyXWorld.CenterOf(TileY);

    internal void Catch(int value, bool doubled)
    {
        Caught = true;
        Frame = value >= 1000 ? 29 : (doubled ? 20 : 10) + value / 100 - 1;
    }
}

/// <summary>A fixed object on the tile (rock or smoke cloud).</summary>
public readonly record struct RallyXSpot(int TileX, int TileY)
{
    public double X => RallyXWorld.CenterOf(TileX);
    public double Y => RallyXWorld.CenterOf(TileY);
}

/// <summary>The rules of a match (from solo.js): rounds, flags, rocks, smoke, fuel, lives, score and the sequences of
/// explosion, end of round and end of game. No devices: <see cref="Step"/> advances 1/60 s and it is driven with
/// <see cref="Steer"/> and <see cref="DropSmoke"/>, so it can be tested without a window.</summary>
public sealed class RallyXSim
{
    public const int Hz = 60, ViewWidth = 272, ViewHeight = 240, StartTileX = 20, StartTileY = 54, MaxEnemies = 7, WishSteps = 45;
    private static readonly int[] EnemyTiles = [20, 18, 22, 16, 24, 14, 26];

    // Delayed actions (restart after an explosion, smoke fading, etc.) are counted in simulation steps, not in
    // real time: the match does not depend on the clock and can be tested at a fixed step.
    private readonly List<(long At, Action Run)> tasks = [];
    private readonly HashSet<(int, int)> taken = [];
    // The engine delivers keys as INKEY$ (presses, no "released"): steering is a direction request applied
    // as soon as the turn is possible and expires after WishSteps steps.
    private RallyXDir wish;
    private long wishUntil, time;
    private int puffsLeft, lastTileX, lastTileY, nextFlagScore, flagMultiplier;
    private bool closing;

    public RallyXWorld World { get; }
    public Random Random { get; }
    public int Round { get; private set; }
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public RallyXPhase Phase { get; private set; }
    public RallyXCar Player { get; private set; } = null!;
    public List<RallyXCar> Enemies { get; } = [];
    public List<RallyXFlag> Flags { get; } = [];
    public List<RallyXSpot> Rocks { get; } = [];
    public List<RallyXSpot> Smokes { get; } = [];
    public double CameraX { get; private set; }
    public double CameraY { get; private set; }
    /// <summary>First row covered by the black end-of-round curtain (ViewHeight = open).</summary>
    public int ClosingY { get; private set; } = ViewHeight;
    /// <summary>The match is over and one can go back to the title.</summary>
    public bool Finished { get; private set; }
    public long Time => time;
    /// <summary>Before departure (the first two seconds of each life).</summary>
    public bool Waiting => Phase == RallyXPhase.Playing && Player.Suspended;
    public int FlagsLeft => Flags.Count(f => !f.Caught);
    public event Action<RallyXSound>? Sound;

    public RallyXSim(RallyXAssets assets, int seed)
    {
        World = new RallyXWorld(assets);
        Random = new Random(seed);
    }

    public void StartRound(int round, int score, int lives)
    {
        Round = round; Score = score; Lives = lives;
        ClosingY = ViewHeight; closing = false; Finished = false;
        tasks.Clear(); Enemies.Clear(); Flags.Clear(); Rocks.Clear(); Smokes.Clear(); taken.Clear();

        Player = new RallyXCar(2, StartTileX, StartTileY, fuel: 100);
        // One more enemy each round (round + 1), up to seven: that is how many starting tiles there are.
        for (int i = 0; i <= Math.Min(round, MaxEnemies - 1); i++) Enemies.Add(new RallyXCar(3, EnemyTiles[i], 57));
        for (int i = 1; i <= 10; i++) { var (x, y) = FreeTile(); Flags.Add(new RallyXFlag(x, y, special: i == 10)); }
        for (int i = 1; i <= 5; i++) { var (x, y) = FreeTile(); Rocks.Add(new RallyXSpot(x, y)); }
        UpdateCamera(snap: true);
        Restart();
    }

    /// <summary>A random road tile, in the play area but outside the starting rectangle, not already taken.</summary>
    private (int X, int Y) FreeTile()
    {
        while (true)
        {
            // Outside x 5..36 and y 4..59 the map is border; the 13..28 x 49.. rectangle is the starting area of player and enemies.
            int x = Random.Next(5, 37), y = Random.Next(4, 60);
            if (x >= 13 && x <= 28 && y >= 49) continue;
            if (!World.IsRoad(x, y) || !taken.Add((x, y))) continue;
            return (x, y);
        }
    }

    private void Restart()
    {
        Phase = RallyXPhase.Playing;
        wish = RallyXDir.Stop; puffsLeft = 0;
        nextFlagScore = 100; flagMultiplier = 1;
        Smokes.Clear();
        Player.Reset();
        foreach (var enemy in Enemies) enemy.Reset();
        lastTileX = Player.TileX; lastTileY = Player.TileY;
        // After two seconds the player leaves (upwards), two more seconds later the enemies.
        Schedule(2000, () =>
        {
            Player.Continue();
            Schedule(2000, () => { foreach (var enemy in Enemies) enemy.Continue(); });
            Player.Go(RallyXDir.Up);
            Sound?.Invoke(RallyXSound.Start);
        });
    }

    /// <summary>Turn as soon as possible: the request lasts half a second (key repeat renews it).</summary>
    public void Steer(RallyXDir direction) { wish = direction; wishUntil = time + WishSteps; }

    /// <summary>Three smoke clouds, one for each tile the car leaves.</summary>
    public void DropSmoke() { if (puffsLeft == 0 && Phase == RallyXPhase.Playing && !Player.Suspended) puffsLeft = 3; }

    /// <summary>One step of 1/60 s. Order, as in <c>update</c> of solo.js: delayed actions, camera, animations; then (only while
    /// playing) physics of player and enemies, steering, chasing, smoke, and finally collisions.</summary>
    public void Step()
    {
        time++;
        UpdateTasks();
        UpdateCamera(snap: false);
        Player.Tick();
        foreach (var enemy in Enemies) enemy.Tick();
        switch (Phase)
        {
            case RallyXPhase.Crashed: return;
            case RallyXPhase.RoundClear: Conclude(next: true); return;
            case RallyXPhase.GameOver: Conclude(next: false); return;
        }

        Player.Update(World);
        if (Player.Fuel <= 0) { Explode(); return; }
        foreach (var enemy in Enemies) enemy.Update(World);

        ApplyWish();
        foreach (var enemy in Enemies) enemy.Follow(World, Player.TileX, Player.TileY);
        DropPuffs();

        // Car, rock or enemy (even one stopped in smoke) destroy the player; the flag is caught.
        if (Enemies.Any(e => Player.Overlaps(e.X, e.Y)) || Rocks.Any(r => Player.Overlaps(r.X, r.Y))) { Explode(); return; }
        var flag = Flags.FirstOrDefault(f => !f.Caught && Player.Overlaps(f.X, f.Y));
        if (flag is not null) Catch(flag);

        foreach (var enemy in Enemies)
        {
            // Rocks and smoke are fixed: the enemy is pushed out and then reverses (rock) or stays still for 2.5 s (smoke).
            // An enemy already stopped in smoke does not collide with it again at every step.
            foreach (var rock in Rocks)
                if (enemy.Overlaps(rock.X, rock.Y)) { enemy.Separate(rock.X, rock.Y); enemy.Reverse(); break; }
            if (enemy.Suspended) continue;
            foreach (var smoke in Smokes)
                if (enemy.Overlaps(smoke.X, smoke.Y))
                {
                    enemy.Separate(smoke.X, smoke.Y);
                    var heading = enemy.Facing;
                    enemy.Smoked();
                    Schedule(2500, () => enemy.Continue(heading));
                    break;
                }
        }
    }

    private void ApplyWish()
    {
        if (wish == RallyXDir.Stop) return;
        if (time > wishUntil) { wish = RallyXDir.Stop; return; }
        if (Player.Suspended || !Player.CanTurn(World, wish)) return;
        Player.Go(wish);
        wish = RallyXDir.Stop;
    }

    /// <summary>When the car changes tile, the cloud stays on the one it left; it costs one unit of fuel.</summary>
    private void DropPuffs()
    {
        int x = Player.TileX, y = Player.TileY;
        if (x == lastTileX && y == lastTileY) return;
        if (puffsLeft > 0)
        {
            var puff = new RallyXSpot(lastTileX, lastTileY);
            Smokes.Add(puff);
            Schedule(3000, () => Smokes.Remove(puff));
            Player.Fuel--; puffsLeft--;
            Sound?.Invoke(RallyXSound.Smoke);
        }
        lastTileX = x; lastTileY = y;
    }

    private void Catch(RallyXFlag flag)
    {
        if (flag.Special) flagMultiplier = 2;
        Score += nextFlagScore * flagMultiplier;
        if (FlagsLeft == 1) { Finish(flag); return; } // it was the last one: the round ends at once, without waiting for the 3 seconds of the score card
        flag.Catch(nextFlagScore, flagMultiplier == 2);
        Schedule(3000, () => Flags.Remove(flag));
        nextFlagScore += 100;
        Sound?.Invoke(RallyXSound.Flag);
    }

    private void Finish(RallyXFlag last)
    {
        last.Catch(nextFlagScore, flagMultiplier == 2);
        SuspendAll(); CompleteTasks();
        Phase = RallyXPhase.RoundClear;
        Sound?.Invoke(RallyXSound.RoundClear);
    }

    private void Explode()
    {
        // First run the pending actions at once (like completeTasks of solo.js): they must not fire in the middle of the explosion.
        CompleteTasks();
        SuspendAll();
        Player.Explode();
        Lives--;
        if (Lives == 0)
        {
            Phase = RallyXPhase.GameOver;
            Sound?.Invoke(RallyXSound.GameOver);
        }
        else
        {
            Phase = RallyXPhase.Crashed;
            Sound?.Invoke(RallyXSound.Crash);
            Schedule(3000, Restart);
        }
    }

    private void SuspendAll() { Player.Suspend(); foreach (var enemy in Enemies) enemy.Suspend(); }

    /// <summary>End of round or end of game: the remaining fuel becomes points, then the black curtain rises from the bottom.</summary>
    private void Conclude(bool next)
    {
        if (Player.Fuel > 0) { Score += 10; Player.Fuel--; return; }
        if (ClosingY >= 0) { ClosingY -= 5; return; }
        if (closing) return;
        closing = true;
        if (next) Schedule(2000, () => StartRound(Round + 1, Score, Lives));
        else Schedule(2000, () => Finished = true);
    }

    /// <summary>The camera chases the player with a soft follow (10% of the distance at each step, like FOLLOW_LOCKON
    /// of Phaser) and does not leave the map; <paramref name="snap"/> puts it on the target at once (round start).</summary>
    private void UpdateCamera(bool snap)
    {
        double targetX = Math.Clamp(Player.X - ViewWidth / 2.0, 0, RallyXWorld.PixelWidth - ViewWidth);
        double targetY = Math.Clamp(Player.Y - ViewHeight / 2.0, 0, RallyXWorld.PixelHeight - ViewHeight);
        if (snap) { CameraX = targetX; CameraY = targetY; return; }
        CameraX += (targetX - CameraX) * 0.1; CameraY += (targetY - CameraY) * 0.1;
    }

    private void Schedule(int milliseconds, Action task) => tasks.Add((time + (long)Math.Ceiling(milliseconds * Hz / 1000.0), task));

    private void UpdateTasks()
    {
        // From last to first, like solo.js: so an action can queue others (which fire at the next step) while scanning.
        for (int i = tasks.Count - 1; i >= 0; i--)
        {
            if (i >= tasks.Count) continue;
            var (at, run) = tasks[i];
            if (time < at) continue;
            tasks.RemoveAt(i);
            run();
        }
    }

    private void CompleteTasks()
    {
        while (tasks.Count > 0)
        {
            var (_, run) = tasks[0];
            tasks.RemoveAt(0);
            run();
        }
    }
}
