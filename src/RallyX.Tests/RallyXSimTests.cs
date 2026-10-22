using RallyX;

namespace RallyX.Tests;

public class RallyXSimTests
{
    private static readonly RallyXAssets Assets = RallyXAssets.Load();

    private static RallyXSim NewSim(int seed = 1, int round = 1, int lives = 3)
    {
        var sim = new RallyXSim(Assets, seed);
        sim.StartRound(round, 0, lives);
        return sim;
    }

    private static void Run(RallyXSim sim, int steps) { for (int i = 0; i < steps; i++) sim.Step(); }

    private static void RunUntil(RallyXSim sim, Func<bool> done, int maxSteps, string what)
    {
        for (int i = 0; i < maxSteps && !done(); i++) sim.Step();
        Assert.True(done(), $"{what}: did not happen within {maxSteps} steps");
    }

    /// <summary>A straight stretch of road above the start, to put flags and rocks on.</summary>
    private static List<(int X, int Y)> RoadAboveStart(RallyXWorld world)
    {
        var road = new List<(int, int)>();
        for (int y = RallyXSim.StartTileY - 1; world.IsRoad(RallyXSim.StartTileX, y); y--) road.Add((RallyXSim.StartTileX, y));
        return road;
    }

    [Fact]
    public void ARoundStartsWithTenFlagsFiveRocksAndEnemiesPerRound()
    {
        var sim = NewSim();
        Assert.Equal((10, 5, 2, 3, 100), (sim.Flags.Count, sim.Rocks.Count, sim.Enemies.Count, sim.Lives, sim.Player.Fuel));
        Assert.True(sim.Flags[^1].Special);
        Assert.Single(sim.Flags, f => f.Special);
        Assert.Equal(4, NewSim(1, 3).Enemies.Count);
        Assert.Equal(7, NewSim(1, 20).Enemies.Count); // there are 7 starting positions
        Assert.True(sim.Waiting);
    }

    [Fact]
    public void FlagsAndRocksAreOnDistinctRoadTilesInThePlayZoneOutsideTheStartingArea()
    {
        for (int seed = 1; seed <= 20; seed++)
        {
            var sim = NewSim(seed);
            var tiles = sim.Flags.Select(f => (f.TileX, f.TileY)).Concat(sim.Rocks.Select(r => (r.TileX, r.TileY))).ToList();
            Assert.Equal(15, tiles.Distinct().Count());
            foreach (var (x, y) in tiles)
            {
                Assert.True(sim.World.IsRoad(x, y));
                Assert.InRange(x, 5, 36); Assert.InRange(y, 4, 59);
                Assert.False(x >= 13 && x <= 28 && y >= 49);
            }
        }
    }

    [Fact]
    public void TheSameSeedMakesTheSameRoundAndAnotherSeedMakesAnother()
    {
        string Layout(RallyXSim s) => string.Join(";", s.Flags.Select(f => $"{f.TileX},{f.TileY}").Concat(s.Rocks.Select(r => $"{r.TileX},{r.TileY}")));
        Assert.Equal(Layout(NewSim(7)), Layout(NewSim(7)));
        Assert.NotEqual(Layout(NewSim(7)), Layout(NewSim(8)));
    }

    [Fact]
    public void ThePlayerLeavesAfterTwoSecondsAndTheEnemiesTwoSecondsLater()
    {
        var sim = NewSim();
        double startY = sim.Player.Y;
        Run(sim, 110);
        Assert.True(sim.Waiting); Assert.Equal(startY, sim.Player.Y);
        Run(sim, 20);
        Assert.False(sim.Waiting); Assert.True(sim.Player.Y < startY);
        Assert.All(sim.Enemies, e => Assert.True(e.Suspended));
        Run(sim, 120);
        Assert.All(sim.Enemies, e => Assert.False(e.Suspended));
    }

    [Fact]
    public void TheCarDrivesUpTheCorridorHitsTheWallAndTurnsAsCarJsPrescribes()
    {
        var sim = NewSim();
        sim.Enemies.Clear(); sim.Flags.Clear(); sim.Rocks.Clear();
        sim.Flags.Add(new RallyXFlag(5, 5, false)); sim.Flags.Add(new RallyXFlag(6, 5, false)); // far away: the round does not end
        var visited = new HashSet<(int, int)>();
        for (int i = 0; i < 60 * 12; i++)
        {
            sim.Step();
            visited.Add((sim.Player.TileX, sim.Player.TileY));
            Assert.True(sim.World.IsRoad(sim.Player.TileX, sim.Player.TileY), $"the car entered a wall at {sim.Player.X:F1},{sim.Player.Y:F1}");
            Assert.False(HitsWall(sim));
        }
        Assert.True(visited.Count > 12, "the car had to drive along the road, not stand still");
        Assert.False(sim.Player.Suspended);
        Assert.NotEqual(0, Math.Abs(sim.Player.Vx) + Math.Abs(sim.Player.Vy)); // it does not stop in a dead end
    }

    private static bool HitsWall(RallyXSim sim)
    {
        var p = sim.Player; const double h = RallyXCar.Size / 2;
        foreach (var (x, y) in new[] { (p.X - h, p.Y - h), (p.X + h - 1e-6, p.Y - h), (p.X - h, p.Y + h - 1e-6), (p.X + h - 1e-6, p.Y + h - 1e-6) })
            if (sim.World.IsSolid(RallyXWorld.TileOf(x), RallyXWorld.TileOf(y))) return true;
        return false;
    }

    [Fact]
    public void FlagsScoreOneHundredMoreEachAndTheSpecialOneDoublesTheRest()
    {
        var sim = NewSim();
        sim.Enemies.Clear(); sim.Rocks.Clear();
        var road = RoadAboveStart(sim.World);
        Assert.True(road.Count >= 6, "this test needs a straight road above the start");
        sim.Flags.Clear();
        sim.Flags.Add(new RallyXFlag(road[1].X, road[1].Y, false));
        sim.Flags.Add(new RallyXFlag(road[3].X, road[3].Y, true));
        sim.Flags.Add(new RallyXFlag(road[5].X, road[5].Y, false));
        sim.Flags.Add(new RallyXFlag(road[0].X + 10 > 36 ? 5 : 6, 5, false)); // a far one: the round stays open
        RunUntil(sim, () => sim.Score >= 100, 600, "first flag");
        Assert.Equal(100, sim.Score);
        RunUntil(sim, () => sim.Score >= 300, 600, "special flag");
        Assert.Equal(100 + 200 * 2, sim.Score); // the special one is worth double
        RunUntil(sim, () => sim.Score >= 700, 600, "third flag");
        Assert.Equal(100 + 400 + 300 * 2, sim.Score); // e raddoppia anche le successive
        Assert.Equal(1, sim.FlagsLeft);
    }

    [Fact]
    public void ACaughtFlagShowsItsScoreThenDisappearsAfterThreeSeconds()
    {
        var sim = NewSim();
        sim.Enemies.Clear(); sim.Rocks.Clear();
        var road = RoadAboveStart(sim.World);
        sim.Flags.Clear();
        var near = new RallyXFlag(road[1].X, road[1].Y, false);
        sim.Flags.Add(near); sim.Flags.Add(new RallyXFlag(5, 5, false));
        RunUntil(sim, () => near.Caught, 600, "caught");
        Assert.Equal(10, near.Frame); // cartellino "100"
        Assert.Contains(near, sim.Flags);
        Run(sim, 60 * 3 + 2);
        Assert.DoesNotContain(near, sim.Flags);
    }

    [Fact]
    public void HittingARockDestroysTheCarCostsALifeAndTheRoundRestartsAfterThreeSeconds()
    {
        var sim = NewSim();
        sim.Enemies.Clear(); sim.Flags.Clear(); sim.Flags.Add(new RallyXFlag(5, 5, false));
        var road = RoadAboveStart(sim.World);
        sim.Rocks.Clear(); sim.Rocks.Add(new RallyXSpot(road[2].X, road[2].Y));
        var sounds = new List<RallyXSound>(); sim.Sound += sounds.Add;
        RunUntil(sim, () => sim.Phase == RallyXPhase.Crashed, 600, "explosion");
        Assert.Equal(2, sim.Lives);
        Assert.Equal(48, sim.Player.Frame); // BANG
        Assert.Contains(RallyXSound.Crash, sounds);
        Run(sim, 60 * 3 - 5);
        Assert.Equal(RallyXPhase.Crashed, sim.Phase);
        Run(sim, 10);
        Assert.Equal(RallyXPhase.Playing, sim.Phase);
        Assert.True(sim.Waiting);
        Assert.Equal((RallyXWorld.CenterOf(RallyXSim.StartTileX), RallyXWorld.CenterOf(RallyXSim.StartTileY)), (sim.Player.X, sim.Player.Y));
        Assert.Equal(100, sim.Player.Fuel);
    }

    [Fact]
    public void RunningOutOfFuelDestroysTheCar()
    {
        var sim = NewSim();
        sim.Enemies.Clear(); sim.Rocks.Clear(); sim.Flags.Clear(); sim.Flags.Add(new RallyXFlag(5, 5, false));
        Run(sim, 130);
        sim.Player.Fuel = 0.01;
        RunUntil(sim, () => sim.Phase == RallyXPhase.Crashed, 10, "explosion for lack of fuel");
        Assert.Equal(2, sim.Lives);
    }

    [Fact]
    public void CatchingTheLastFlagTurnsFuelIntoPointsClosesTheCurtainAndStartsTheNextRoundKeepingScoreAndLives()
    {
        var sim = NewSim(lives: 2);
        sim.Enemies.Clear(); sim.Rocks.Clear();
        var road = RoadAboveStart(sim.World);
        sim.Flags.Clear(); sim.Flags.Add(new RallyXFlag(road[1].X, road[1].Y, false));
        var sounds = new List<RallyXSound>(); sim.Sound += sounds.Add;
        RunUntil(sim, () => sim.Phase == RallyXPhase.RoundClear, 600, "end of round");
        Assert.Contains(RallyXSound.RoundClear, sounds);
        int fuel = (int)Math.Ceiling(sim.Player.Fuel), caught = sim.Score;
        RunUntil(sim, () => sim.ClosingY < 0, 600, "curtain");
        Assert.True(sim.Score >= caught + fuel * 10 - 10, "the remaining fuel becomes points, 10 per unit");
        int score = sim.Score;
        RunUntil(sim, () => sim.Round == 2, 60 * 3, "round 2");
        Assert.Equal((score, 2, RallyXSim.ViewHeight, 3), (sim.Score, sim.Lives, sim.ClosingY, sim.Enemies.Count)); // round 2: three enemies
        Assert.Equal(10, sim.Flags.Count);
    }

    [Fact]
    public void LosingTheLastLifeEndsTheGameAfterTheCurtainAndTwoSeconds()
    {
        var sim = NewSim(lives: 1);
        sim.Enemies.Clear(); sim.Flags.Clear(); sim.Flags.Add(new RallyXFlag(5, 5, false));
        var road = RoadAboveStart(sim.World);
        sim.Rocks.Clear(); sim.Rocks.Add(new RallyXSpot(road[2].X, road[2].Y));
        RunUntil(sim, () => sim.Phase == RallyXPhase.GameOver, 600, "game over");
        Assert.Equal(0, sim.Lives);
        Assert.False(sim.Finished);
        RunUntil(sim, () => sim.Finished, 60 * 6, "back to the title");
        Assert.True(sim.ClosingY < 0);
    }

    [Fact]
    public void SmokeDropsThreePuffsBehindTheCarEachCostingFuelAndFadingAfterThreeSeconds()
    {
        var sim = NewSim();
        sim.Enemies.Clear(); sim.Rocks.Clear(); sim.Flags.Clear(); sim.Flags.Add(new RallyXFlag(5, 5, false));
        Run(sim, 125);
        double fuel = sim.Player.Fuel;
        sim.DropSmoke();
        RunUntil(sim, () => sim.Smokes.Count == 3, 60 * 4, "three clouds");
        Assert.True(fuel - sim.Player.Fuel >= 3, "each cloud costs one unit of fuel");
        Assert.Equal(3, sim.Smokes.Distinct().Count());
        Run(sim, 60 * 3 + 5);
        Assert.Empty(sim.Smokes);
    }

    [Fact]
    public void AnEnemyThatDrivesIntoSmokeStopsSpinsAndLeavesAgainAfterTwoAndAHalfSeconds()
    {
        var sim = NewSim();
        sim.Rocks.Clear(); sim.Flags.Clear(); sim.Flags.Add(new RallyXFlag(5, 5, false));
        var enemy = sim.Enemies[0];
        Run(sim, 250); // tutti in marcia
        sim.Enemies.Clear(); sim.Enemies.Add(enemy);
        sim.Smokes.Add(new RallyXSpot(enemy.TileX, enemy.TileY - 4));
        enemy.Continue(RallyXDir.Up);
        var heading = enemy.Facing;
        RunUntil(sim, () => enemy.Suspended, 600, "enemy stopped by the smoke");
        Assert.Equal(RallyXDir.Up, heading);
        sim.Smokes.Clear(); // the real one fades by itself after 3 seconds: this way the enemy does not enter it again
        Run(sim, 60 * 2);
        Assert.True(enemy.Suspended);
        Run(sim, 40);
        Assert.False(enemy.Suspended);
    }

    [Fact]
    public void AnEnemyReversesAtARockWithoutExploding()
    {
        var sim = NewSim();
        sim.Flags.Clear(); sim.Flags.Add(new RallyXFlag(5, 5, false)); sim.Rocks.Clear();
        var enemy = sim.Enemies[0];
        Run(sim, 250);
        sim.Enemies.Clear(); sim.Enemies.Add(enemy);
        sim.Rocks.Add(new RallyXSpot(enemy.TileX, enemy.TileY - 3));
        enemy.Continue(RallyXDir.Up);
        RunUntil(sim, () => enemy.Facing == RallyXDir.Down, 600, "reversal");
        Assert.False(enemy.Suspended);
    }

    [Fact]
    public void ASteeringRequestWaitsForTheTurnAndExpires()
    {
        var sim = NewSim();
        sim.Enemies.Clear(); sim.Rocks.Clear(); sim.Flags.Clear(); sim.Flags.Add(new RallyXFlag(5, 5, false));
        Run(sim, 125);
        Assert.Equal(RallyXDir.Up, sim.Player.Facing);
        sim.Steer(RallyXDir.Down); // it can always reverse
        sim.Step();
        Assert.Equal(RallyXDir.Down, sim.Player.Facing);
    }

    [Fact]
    public void TheAutopilotClearsARoundWithoutEnemiesWhichProvesTheWholePhysicsAndTurningChain()
    {
        var sim = NewSim(3);
        sim.Enemies.Clear();
        var bot = new RallyXAutopilot();
        RunUntil(sim, () => { bot.Drive(sim); return sim.Phase == RallyXPhase.RoundClear; }, 60 * 100, "round completed by the autopilot");
        Assert.Equal(0, sim.FlagsLeft);
        Assert.True(sim.Score >= 100 + 200 + 300 + 400 + 500 + 600 + 700 + 800 + 900 + 1000, $"score {sim.Score}");
    }

    [Fact]
    public void AnEnemyChasesThePlayerAndCanCatchAnIdleCar()
    {
        var sim = NewSim(2);
        sim.Rocks.Clear(); sim.Flags.Clear(); sim.Flags.Add(new RallyXFlag(5, 5, false));
        // the player car is not driven: it wanders the road by itself and sooner or later an enemy catches it
        RunUntil(sim, () => sim.Phase == RallyXPhase.Crashed, 60 * 120, "the enemy reaches the player");
        Assert.Equal(2, sim.Lives);
        Assert.Contains(sim.Enemies, e => sim.Player.Overlaps(e.X, e.Y)); // it was an enemy, not a wall or fuel
        Assert.True(sim.Player.Fuel > 0);
        Assert.True(sim.Time > 60 * 4, "the enemies leave only four seconds after the start");
    }
}
