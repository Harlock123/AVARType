using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVARType.Engine;
using AVARType.Views;
using Controls = AVARType.Engine.Controls;

namespace AVARType.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set RTYPE_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avartype-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("RTYPE_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVARType-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.6);
        Shot(w, c, "title");
        g.HighScores.Insert("LNC", 288400, 8);
        g.HighScores.Insert("AVA", 96200, 3);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    private static void Play(int stage, string name, Func<GameEngine, bool> moment, int seconds = 200, Action<World>? arm = null, int checkpoint = 0)
    {
        var (w, c, g) = Open();
        g.Go(stage, checkpoint: checkpoint);
        g.SetLives(20);
        arm?.Invoke(g.World);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    private static Action<World> Armed(Loot crystal) => w =>
    {
        Sim.Take(w, crystal);
        Sim.Take(w, crystal);
        Sim.Take(w, crystal);
        Sim.Take(w, Loot.Bit);
        Sim.Take(w, Loot.Bit);
        Sim.Take(w, Loot.Missile);
        Sim.Take(w, Loot.Speed);
    };

    [AvaloniaFact]
    public void A_porter() => Play(0, "porter", g => g.World.Enemies.Any(e => e.Kind == Foe.Porter && e.X < g.World.CamX + Field.Width - 80) && g.World.Shots.Count >= 2);

    [AvaloniaFact]
    public void The_outer_wall() => Play(0, "outer-wall", g => g.World.CamX > 74 * Field.Tile && g.World.Enemies.Any(e => e.Kind is Foe.Strider or Foe.Gun) && g.World.Shots.Any(s => s.Kind == ShotKind.Ring), arm: Armed(Loot.RedCrystal));

    [AvaloniaFact]
    public void The_beam() => Play(0, "beam", g => g.World.Shots.Any(s => s.Kind == ShotKind.Beam && s.X - g.World.Ship.X > 200 && s.X - g.World.Ship.X < 420), arm: Armed(Loot.RedCrystal), checkpoint: 132);

    [AvaloniaFact]
    public void The_Hollow_Mother() => Play(0, "mother", g => g.World.Enemies.Any(e => e.Kind == Foe.Mother && e.X < g.World.CamX + 860) && g.World.Missiles.Count >= 2, arm: Armed(Loot.BlueCrystal), checkpoint: 202);

    [AvaloniaFact]
    public void The_caves() => Play(1, "caves", g => g.World.CamX > 30 * Field.Tile && g.World.Shots.Any(s => s.Kind == ShotKind.Crawl && s.Running) && g.World.Missiles.Count >= 1, arm: Armed(Loot.YellowCrystal));

    [AvaloniaFact]
    public void The_Serpent() => Play(1, "serpent", g => g.World.Enemies.Any(e => e.Kind == Foe.Serpent && e.T > 6), arm: Armed(Loot.RedCrystal), checkpoint: 200);

    [AvaloniaFact]
    public void The_dreadnought() => Play(2, "dreadnought", g => g.World.CamX > 40 * Field.Tile && g.World.Enemies.Count(e => e.Kind == Foe.Battery) >= 2 && g.World.Shots.Any(s => s.Kind == ShotKind.Reflect), arm: Armed(Loot.BlueCrystal));

    [AvaloniaFact]
    public void The_engines() => Play(2, "engines", g => g.World.Enemies.Count(e => e.Kind == Foe.Engine && e.X < g.World.CamX + 880) == 3 && g.World.Missiles.Any(m => m.Length > 0), arm: Armed(Loot.RedCrystal), checkpoint: 196);

    [AvaloniaFact]
    public void The_Overmind() => Play(3, "overmind", g => g.World.Enemies.Any(e => e.Kind == Foe.Overmind && Sim.Open(e) && e.T % 6 > 3.6) && g.World.Missiles.Count >= 4, arm: Armed(Loot.YellowCrystal), checkpoint: 189);

    [AvaloniaFact]
    public void The_orb_out_on_its_own()
    {
        var (w, c, g) = Open();
        g.Go(0, checkpoint: 72);
        g.SetLives(20);
        Armed(Loot.RedCrystal)(g.World);
        g.Pilot.Reset();
        g.Tick(new Controls(0, 0, true));
        for (int i = 0; i < 60 * 3; i++) { g.Tick(g.Pilot.Decide(g) with { Orb = false }); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "orb");
    }
}
