using Avalonia.Input;
using AVARType.Engine;

namespace AVARType.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avartype-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A stage at a column with nothing coming, and the ship safe to hit.</summary>
    private static World Empty(int stage = 0, int col = 0)
    {
        var w = new World();
        Sim.Start(w, stage, 1, col, null, null, 1);
        w.NextSpawn = w.Stage.Spawns.Count;
        w.Ship.Safe = 0;
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    private static Enemy Put(World w, Foe kind, double dx, double dy, int hp = 1)
    {
        var e = new Enemy { Kind = kind, X = w.Ship.X + dx, Y = w.Ship.Y + dy, Hp = hp, Id = w.NextId++, Entered = true, FireIn = 99, HomeY = w.Ship.Y + dy };
        w.Enemies.Add(e);
        return e;
    }

    /// <summary>Taps of fire: three steps on, three off.</summary>
    private static void Tap(World w, int steps, int dy = 0)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, new Controls(0, dy, false, i / 3 % 2 == 0));
    }

    // ───────────────────────────── flying and shooting ─────────────────────────────

    [Fact]
    public void The_stage_carries_the_ship_and_walls_are_the_end_of_it()
    {
        var w = Empty();
        double x = w.Ship.X - w.CamX;
        Run(w, 60);
        Assert.Equal(x, w.Ship.X - w.CamX, 1);
        var v = Empty(0, 72);
        Run(v, 300, new Controls(0, 1));
        Assert.True(v.Ship.Dead);
        Assert.Equal("the wall", v.Cause);
    }

    [Fact]
    public void A_tap_shoots_and_a_held_button_charges_the_beam_and_letting_go_looses_it()
    {
        var w = Empty();
        Sim.Step(w, new Controls(0, 0, false, true));
        Assert.Contains(w.Shots, s => s.Kind == ShotKind.Pea);
        Run(w, 60 * 2, new Controls(0, 0, false, true));
        Assert.Equal(3, w.Ship.ChargeLevel);
        w.Shots.Clear();
        Sim.Step(w, default);
        var beam = Assert.Single(w.Shots, s => s.Kind == ShotKind.Beam);
        Assert.Equal(0, w.Ship.Charge);
        // The beam goes through one thing into the next.
        var a = Put(w, Foe.Flitter, 200, 0, 5);
        var b = Put(w, Foe.Flitter, 260, 0, 5);
        Run(w, 10);
        Assert.True(a.Dead && b.Dead);
    }

    [Fact]
    public void A_short_charge_is_a_weak_beam()
    {
        var w = Empty();
        Run(w, (int)(60 * (Sim.HoldToCharge + 0.5)), new Controls(0, 0, false, true));
        Assert.Equal(1, w.Ship.ChargeLevel);
        w.Shots.Clear();
        Sim.Step(w, default);
        var beam = Assert.Single(w.Shots, s => s.Kind == ShotKind.Beam);
        Assert.True(beam.Damage < 5);
    }

    // ───────────────────────────── the porters and the orb ─────────────────────────────

    [Fact]
    public void A_porter_shot_down_drops_what_it_carries_and_the_first_crystal_brings_the_orb()
    {
        var w = Empty();
        var p = Put(w, Foe.Porter, 260, 0, 4);
        p.Loot = Loot.RedCrystal;
        w.Ship.Safe = 99;
        for (int i = 0; i < 240 && !p.Dead; i++) { w.Ship.Y = p.Y; Sim.Step(w, new Controls(0, 0, false, i / 3 % 2 == 0)); }
        Assert.True(p.Dead);
        var item = Assert.Single(w.Items);
        w.Ship.X = item.X;
        w.Ship.Y = item.Y;
        Sim.Step(w, default);
        Assert.Equal(OrbState.Front, w.Orb.State);
        Assert.Equal(Laser.Red, w.Orb.Laser);
        Assert.Equal(1, w.Orb.Level);
        // More crystals: a stronger orb, in the new colour.
        Sim.Take(w, Loot.BlueCrystal);
        Sim.Take(w, Loot.BlueCrystal);
        Sim.Take(w, Loot.BlueCrystal);
        Assert.Equal(3, w.Orb.Level);
        Assert.Equal(Laser.Blue, w.Orb.Laser);
    }

    [Fact]
    public void The_orb_soaks_up_shots_and_chews_up_what_it_touches()
    {
        var w = Empty();
        Sim.Take(w, Loot.RedCrystal);
        Sim.Step(w, default);
        // A shot straight at the nose: the orb takes it.
        w.Missiles.Add(new Missile { X = w.Orb.X + 30, Y = w.Ship.Y, Vx = -300 });
        Run(w, 20);
        Assert.False(w.Ship.Dead);
        Assert.Empty(w.Missiles);
        var e = Put(w, Foe.Battery, 0, 0, 5);
        e.X = w.Orb.X;
        w.Ship.Safe = 99;
        Run(w, 40);
        Assert.True(e.Dead);
    }

    [Fact]
    public void The_orb_launches_hangs_off_on_its_own_and_comes_back_to_whichever_side_it_meets()
    {
        var w = Empty();
        Sim.Take(w, Loot.RedCrystal);
        Sim.Step(w, default);
        Sim.Step(w, new Controls(0, 0, true));
        Assert.Equal(OrbState.Free, w.Orb.State);
        Run(w, 90);
        Assert.True(w.Orb.X - w.CamX > 700, "out near the far edge");
        Sim.Step(w, new Controls(0, 0, true));
        Assert.Equal(OrbState.Recall, w.Orb.State);
        Run(w, 120);
        Assert.Equal(OrbState.Front, w.Orb.State);
        // Called back from behind the ship, it fixes to the tail.
        Sim.Step(w, new Controls(0, 0, true));
        Sim.Step(w, default);
        w.Orb.X = w.CamX + 40;
        Sim.Step(w, new Controls(0, 0, true));
        Assert.Equal(OrbState.Recall, w.Orb.State);
        Run(w, 120);
        Assert.Equal(OrbState.Back, w.Orb.State);
    }

    [Fact]
    public void Each_crystal_colour_fires_its_own_laser_from_the_orb()
    {
        foreach (var (loot, kind) in new[] { (Loot.RedCrystal, ShotKind.Ring), (Loot.BlueCrystal, ShotKind.Reflect), (Loot.YellowCrystal, ShotKind.Crawl) })
        {
            var w = Empty();
            Sim.Take(w, loot);
            Sim.Take(w, loot);
            Sim.Step(w, default);
            Sim.Step(w, new Controls(0, 0, false, true));
            Assert.Contains(w.Shots, s => s.Kind == kind);
        }
    }

    [Fact]
    public void Speed_bits_and_missiles()
    {
        var w = Empty();
        double y = w.Ship.Y;
        Run(w, 20, new Controls(0, 1));
        double slow = w.Ship.Y - y;
        var v = Empty();
        Sim.Take(v, Loot.Speed);
        Sim.Take(v, Loot.Speed);
        Run(v, 20, new Controls(0, 1));
        Assert.True(v.Ship.Y - y > slow * 1.4);
        Sim.Take(v, Loot.Bit);
        Sim.Take(v, Loot.Bit);
        Sim.Take(v, Loot.Bit);
        Assert.Equal(2, v.Ship.Bits);
        Sim.Take(v, Loot.Missile);
        Sim.Step(v, new Controls(0, 0, false, true));
        Assert.Equal(2, v.Shots.Count(s => s.Kind == ShotKind.Missile));
        Assert.True(v.Shots.Count(s => s.Kind == ShotKind.Pea) >= 3, "the bits fire too");
    }

    [Fact]
    public void Losing_the_ship_loses_the_orb()
    {
        var w = Empty();
        Sim.Take(w, Loot.RedCrystal);
        Sim.Kill(w, "test");
        Sim.Step(w, default);
        Assert.Equal(OrbState.None, w.Orb.State);
    }

    // ───────────────────────────── the Swarm ─────────────────────────────

    [Fact]
    public void Guns_fire_at_the_ship_with_shots_that_go_with_the_screen()
    {
        var w = Empty(0, 30);
        var gun = Put(w, Foe.Gun, 300, 200, 2);
        gun.FireIn = 0.1;
        for (int i = 0; i < 160 && !w.Ship.Dead; i++) Sim.Step(w, default);
        Assert.True(w.Ship.Dead);
        Assert.Equal("a shot", w.Cause);
    }

    [Fact]
    public void A_battery_takes_a_lot_of_shots()
    {
        var w = Empty();
        var b = Put(w, Foe.Battery, 200, 0, 12);
        Sim.Step(w, new Controls(0, 0, false, true));
        Run(w, 10);
        Assert.False(b.Dead);
        Tap(w, 300);
        Assert.True(b.Dead);
        Assert.True(w.Score >= Sim.Points(Foe.Battery));
    }

    [Fact]
    public void Tissue_shot_away_grows_back()
    {
        var w = Empty(1, 20);
        Sim.Break(w, 28, 8);
        Assert.Equal(Tile.Empty, w.Grid[28, 8]);
        Run(w, 60 * 7);
        Assert.Equal(Tile.Cell, w.Grid[28, 8]);
    }

    // ───────────────────────────── the bosses ─────────────────────────────

    private static (World W, List<Enemy> Bosses) Boss(int stage)
    {
        var w = Empty(stage, StageBook.All[stage].Checkpoints[^1]);
        w.CamX = w.Stage.BossCol * Field.Tile - Field.Width - 1;
        for (int i = 0; i < 600 && !(w.BossUp && w.Enemies.Where(e => e.Boss).All(e => e.X < w.CamX + Field.Width - 30)); i++) Sim.Step(w, default);
        w.Ship.Safe = 999;
        return (w, w.Enemies.Where(e => e.Boss).ToList());
    }

    [Fact]
    public void The_Hollow_Mother_is_hurt_only_in_her_core()
    {
        var (w, bosses) = Boss(0);
        var m = Assert.Single(bosses);
        int hp = m.Hp;
        w.Shots.Add(new Shot { X = m.X - 100, Y = m.Y - 150, Vx = Sim.PeaSpeed, Kind = ShotKind.Pea });
        Run(w, 10);
        Assert.Equal(hp, m.Hp);
        var (cx, cy) = Sim.MotherCore(m);
        w.Shots.Add(new Shot { X = cx - 120, Y = cy, Vx = Sim.PeaSpeed, Kind = ShotKind.Pea, Damage = 1 });
        Run(w, 10);
        Assert.Equal(hp - 1, m.Hp);
    }

    [Fact]
    public void The_Serpent_is_hurt_in_its_head_and_its_body_stops_shots()
    {
        var (w, bosses) = Boss(1);
        var s = Assert.Single(bosses);
        Run(w, 120);
        int hp = s.Hp;
        // A shot right on a body segment is stopped, and does no harm.
        var (bx, by) = Sim.Body(s).Skip(6).First();
        var shot = new Shot { X = bx, Y = by, Kind = ShotKind.Pea };
        w.Shots.Add(shot);
        Sim.Step(w, default);
        Assert.True(shot.Dead);
        Assert.Equal(hp, s.Hp);
        // One on its head hurts it.
        shot = new Shot { X = s.X, Y = s.Y, Kind = ShotKind.Pea };
        w.Shots.Add(shot);
        Sim.Step(w, default);
        Assert.Equal(hp - 1, s.Hp);
    }

    [Fact]
    public void The_engines_are_three_and_all_must_go()
    {
        var (w, bosses) = Boss(2);
        Assert.Equal(3, bosses.Count);
        Sim.Destroy(w, bosses[0]);
        Sim.Destroy(w, bosses[1]);
        Assert.False(w.BossDown);
        Sim.Destroy(w, bosses[2]);
        Assert.True(w.BossBeaten);
        Run(w, 60 * 4);
        Assert.True(w.Done);
    }

    [Fact]
    public void The_Overmind_is_hurt_only_with_its_shell_open()
    {
        var (w, bosses) = Boss(3);
        var o = Assert.Single(bosses);
        Assert.Null(Sim.WeakSpot(new Enemy { Kind = Foe.Overmind, T = 1 }));
        Assert.NotNull(Sim.WeakSpot(new Enemy { Kind = Foe.Overmind, T = 4 }));
    }

    [Fact]
    public void A_boss_not_beaten_in_time_withdraws_and_the_stage_ends_without_its_points()
    {
        var (w, bosses) = Boss(0);
        Run(w, (int)(60 * (Sim.BossPatience + 10)));
        Assert.True(w.Done);
        Assert.False(w.BossBeaten);
    }

    // ───────────────────────────── copies and the game ─────────────────────────────

    [Fact]
    public void A_copy_of_the_world_plays_out_exactly_as_the_world_does()
    {
        var a = new World();
        Sim.Start(a, 0, 1, 64, null, null, 7);
        Tap(a, 120);
        var b = a.Copy();
        var c = new Controls(0, -1, false, true);
        Run(a, 400, c);
        Run(b, 400, c);
        Assert.Equal(a.Ship.X, b.Ship.X);
        Assert.Equal(a.Ship.Y, b.Ship.Y);
        Assert.Equal(a.Score, b.Score);
        Assert.Equal(a.Enemies.Count, b.Enemies.Count);
    }

    [Fact]
    public void A_ship_lost_starts_again_at_the_last_checkpoint_with_nothing()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go(0, checkpoint: 72);
        Sim.Take(g.World, Loot.RedCrystal);
        Sim.Take(g.World, Loot.Bit);
        g.Tick(default);
        Sim.Kill(g.World, "test");
        g.Run((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        Assert.Equal(72 * Field.Tile, g.World.CamX, 1);
        Assert.Equal(OrbState.None, g.World.Orb.State);
        Assert.Equal(0, g.World.Ship.Bits);
    }

    [Fact]
    public void A_stage_cleared_goes_on_to_the_next_with_the_orb_and_the_rest()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go(0);
        Sim.Take(g.World, Loot.YellowCrystal);
        Sim.Take(g.World, Loot.YellowCrystal);
        Sim.Take(g.World, Loot.Bit);
        g.World.Done = true;
        g.Tick(default);
        Assert.Equal(Phase.Clear, g.Phase);
        g.Run((int)(60 * GameEngine.ClearSeconds) + 5);
        Assert.Equal(1, g.World.StageIndex);
        Assert.Equal(OrbState.Front, g.World.Orb.State);
        Assert.Equal(2, g.World.Orb.Level);
        Assert.Equal(Laser.Yellow, g.World.Orb.Laser);
        Assert.Equal(1, g.World.Ship.Bits);
    }

    [Fact]
    public void The_last_ship_lost_ends_the_game_and_a_good_score_goes_on_the_table()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go(0);
        g.SetLives(1);
        g.World.Score = 90000;
        Sim.Kill(g.World, "test");
        g.Run((int)(60 * (GameEngine.DyingSeconds + 4.5)));
        Assert.Equal(GameState.EnterInitials, g.State);
    }

    [Fact]
    public void The_computer_pilot_flies_the_whole_game_round_to_the_start_again()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 4) { Silent = true };
        g.Go(0);
        g.SetLives(20);
        g.Pilot.Reset();
        for (int t = 0; t < 60 * 900 && g.World.Round == 1 && g.State == GameState.Playing; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(2, g.World.Round);
        Assert.True(g.Lives >= 16, $"{20 - g.Lives} ships lost");
    }

    [Fact]
    public void Left_alone_the_title_runs_the_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Run((int)(60 * GameEngine.AttractSeconds) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Run((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_keys_fly_fire_and_work_the_orb()
    {
        var input = new InputHandler();
        input.OnKeyDown(Key.Up);
        Assert.Equal(-1, input.Player.Dy);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.Right);
        Assert.Equal(1, input.Player.Dx);
        input.OnKeyDown(Key.Z);
        Assert.True(input.Player.Orb);
        Assert.False(input.Player.Fire);
        input.OnKeyDown(Key.X);
        Assert.True(input.Player.Fire);
        input.Dispose();
        var w = Empty();
        double y = w.Ship.Y;
        Run(w, 20, new Controls(0, -1));
        Assert.True(w.Ship.Y < y);
    }
}
