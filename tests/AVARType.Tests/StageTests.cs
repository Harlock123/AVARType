using AVARType.Engine;

namespace AVARType.Tests;

public class StageTests
{
    /// <summary>The rows of a column that aren't rock (glass and cells can be shot away).</summary>
    private static (int From, int To) Open(StageDef s, int c)
    {
        int from = 0, to = Field.Rows - 1;
        while (from < Field.Rows && s.Grid[c, from] == Tile.Rock) from++;
        while (to >= 0 && s.Grid[c, to] == Tile.Rock) to--;
        return (from, to);
    }

    [Fact]
    public void Every_stage_has_a_way_through_wide_enough_all_along()
    {
        foreach (var s in StageBook.All)
            for (int c = 0; c < s.Cols; c++)
            {
                var (a, b) = Open(s, c);
                for (int r = a; r <= b; r++) Assert.True(s.Grid[c, r] != Tile.Rock, $"{s.Name}: rock in the middle of the way at column {c}");
                Assert.True(b - a + 1 >= 6, $"{s.Name}: only {b - a + 1} rows open at column {c}");
                if (c > 0)
                {
                    var (pa, pb) = Open(s, c - 1);
                    Assert.True(Math.Min(b, pb) - Math.Max(a, pa) + 1 >= 4, $"{s.Name}: the way jumps at column {c}");
                }
            }
    }

    [Fact]
    public void The_checkpoints_are_open_and_in_order_and_the_stage_ends_in_an_open_arena()
    {
        foreach (var s in StageBook.All)
        {
            Assert.Equal(0, s.Checkpoints[0]);
            for (int i = 1; i < s.Checkpoints.Length; i++) Assert.True(s.Checkpoints[i] > s.Checkpoints[i - 1]);
            foreach (int cp in s.Checkpoints)
            {
                int c = cp + 5;
                for (int r = 0; r < Field.Rows; r++)
                    if (s.Grid[c, r] is Tile.Crystal or Tile.Cell) Assert.Fail($"{s.Name}: a wall where a ship starts again at {cp}");
                var (a, b) = Open(s, c);
                Assert.True(b - a + 1 >= 8, $"{s.Name}: tight start at {cp}");
            }
            for (int c = s.BossCol - Field.ViewCols + 1; c < s.BossCol; c++)
                Assert.True(Open(s, c).To - Open(s, c).From + 1 >= 14, $"{s.Name}: the arena is tight at column {c}");
        }
    }

    [Fact]
    public void Every_stage_has_porters_with_crystals_and_something_on_the_ground_and_the_roof()
    {
        foreach (var s in StageBook.All)
        {
            Assert.True(s.Spawns.Count(p => p.Kind == Foe.Porter) >= 4, $"{s.Name}: too few porters");
            Assert.Contains(s.Spawns, p => p.Loot is Loot.RedCrystal or Loot.BlueCrystal or Loot.YellowCrystal);
            Assert.Contains(s.Spawns, p => p.Roof);
            Assert.Contains(s.Spawns, p => p.Kind is Foe.Gun or Foe.Strider or Foe.Hatch or Foe.Battery or Foe.Pod && !p.Roof);
            Assert.True(s.Spawns.All(p => p.Col < s.BossCol), $"{s.Name}: something placed past the end");
        }
    }

    [Fact]
    public void The_computer_pilot_clears_every_stage()
    {
        for (int stage = 0; stage < StageBook.All.Length; stage++)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "rtype-stage-" + Guid.NewGuid().ToString("N"))), 5) { Silent = true };
            g.Go(stage);
            g.SetLives(30);
            g.Pilot.Reset();
            int deaths = 0;
            var last = g.Phase;
            for (int t = 0; t < 60 * 400 && g.Cleared == 0; t++)
            {
                g.Tick(g.Pilot.Decide(g));
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths++;
                last = g.Phase;
            }
            Assert.True(g.Cleared == 1, $"{StageBook.All[stage].Name}: not cleared");
            Assert.True(deaths <= 2, $"{StageBook.All[stage].Name}: {deaths} ships lost");
        }
    }
}
