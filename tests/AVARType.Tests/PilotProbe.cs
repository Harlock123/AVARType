using AVARType.Engine;

namespace AVARType.Tests;

/// <summary>
/// How the computer pilot gets on in each stage (set RTYPE_PROBE to run it, for tuning: "1" for the pilot,
/// "lazy" for a pilot who only holds the middle and fires).
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_pilot_does()
    {
        string? mode = Environment.GetEnvironmentVariable("RTYPE_PROBE");
        if (mode is null) return;
        var seeds = (Environment.GetEnvironmentVariable("RTYPE_SEEDS") ?? "1,2,3").Split(',').Select(int.Parse);
        var stages = (Environment.GetEnvironmentVariable("RTYPE_STAGES") ?? "0,1,2,3").Split(',').Select(int.Parse);
        foreach (int seed in seeds)
        foreach (int stage in stages)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "rtype-probe-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
            g.Go(stage);
            g.SetLives(30);
            g.Pilot.Reset();
            var deaths = new Dictionary<string, int>();
            int t = 0, capsules = 0, items = 0;
            var last = g.Phase;
            bool beaten = false;
            double bossAt = 0, bossFor = 0;
            for (; t < 60 * 600 && g.State == GameState.Playing && g.Cleared == 0 && g.World.StageIndex == stage; t++)
            {
                var c = mode == "lazy" ? new Controls(0, Math.Sign((Field.Top + Field.Bottom) / 2 - g.World.Ship.Y), false, t % 6 < 3) : g.Pilot.Decide(g);
                g.Tick(c);
                if (g.World.Items.Count == 0 && items > 0) capsules++;
                items = g.World.Items.Count;
                if (g.Phase == Phase.Dying && last != Phase.Dying) { var key = $"{g.World.Cause} at {(int)(g.World.Ship.X / Field.Tile)}"; deaths[key] = deaths.GetValueOrDefault(key) + 1; }
                last = g.Phase;
                if (g.World.BossUp && bossAt == 0) bossAt = t / 60.0;
                if (g.World.BossDown && bossFor == 0) bossFor = t / 60.0 - bossAt;
                beaten |= g.World.BossBeaten;
            }
            var s = g.World.Ship;
            output.WriteLine($"seed {seed} {StageBook.All[stage].Name}: {(g.Cleared > 0 ? "CLEARED" : "not cleared")} after {t / 60}s, {deaths.Values.Sum()} deaths, boss {(beaten ? "beaten" : "escaped")} in {bossFor:0}s, score {g.Score}, items {capsules}, speed {s.Speed} missile {s.Missile} bits {s.Bits} orb {g.World.Orb.State} {g.World.Orb.Level} {g.World.Orb.Laser}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.OrderByDescending(d => d.Value).Take(10).Select(d => $"{d.Key} x{d.Value}")));
        }
    }
}
