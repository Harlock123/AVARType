namespace AVARType.Engine;

/// <summary>
/// The computer pilot, for the demonstration and the tests. Every few steps it tries each of the nine ways to
/// steer, each followed by the best of a few ways on, on copies of the world, firing all the while, and takes the
/// one that keeps the ship alive, clear of the walls and of what's flying, lined up with something to shoot, and
/// near what the porters drop. It taps fire for a stream of shots, and against a boss holds it to charge the beam.
/// It keeps the orb on the ship's nose.
/// </summary>
public sealed class Pilot
{
    private (int Dx, int Dy) _move;
    private int _held, _tick, _charge;
    private static readonly (int Dx, int Dy)[] Moves = [(0, 0), (1, 0), (-1, 0), (0, -1), (0, 1), (1, -1), (1, 1), (-1, -1), (-1, 1)];
    private static readonly (int Dx, int Dy)[] Then = [(0, 0), (0, -1), (0, 1), (-1, 0)];
    public const int Hold = 8, Ahead = 26;

    public string Doing { get; private set; } = "";

    public void Reset()
    {
        _held = 0;
        _move = default;
        _charge = 0;
        Doing = "";
    }

    /// <summary>
    /// Fire: tapped (three steps on, three off); or, with a boss to hit and nothing small close by, held to charge
    /// the beam, then let go.
    /// </summary>
    private bool Trigger(World w)
    {
        if (Charging(w))
        {
            _charge++;
            if (_charge >= 122) { _charge = 0; return false; }
            return true;
        }
        _charge = 0;
        return _tick / 3 % 2 == 0;
    }

    private static bool Charging(World w)
    {
        var s = w.Ship;
        if (!w.BossUp || !w.Enemies.Any(e => e.Boss && Sim.WeakSpot(e) != null && e.X < w.CamX + Field.Width - 40)) return false;
        return !w.Enemies.Any(e => !e.Boss && Math.Abs(e.X - s.X) < 360 && Math.Abs(e.Y - s.Y) < 260);
    }

    public Controls Decide(GameEngine g)
    {
        _tick++;
        var w = g.World;
        if (g.Phase != Phase.Play || w.Ship.Dead) { _held = 0; return default; }
        bool fire = Trigger(w);
        if (_held > 0)
        {
            _held--;
            return new Controls(_move.Dx, _move.Dy, false, fire);
        }
        double best = double.MinValue;
        var pick = (0, 0);
        foreach (var m in Moves)
        {
            double value = Try(w, m, _charge);
            if (value > best) { best = value; pick = m; }
        }
        _move = pick;
        _held = 3;
        Doing = pick switch { (0, 0) => "hold", (1, 0) => "on", (-1, 0) => "back", (0, -1) => "up", (0, 1) => "down", (1, _) => "on and over", _ => "back and over" };
        return new Controls(pick.Item1, pick.Item2, false, fire);
    }

    private static double Try(World start, (int Dx, int Dy) m, int charge)
    {
        var w = start.Copy();
        Run(w, m, Hold, charge);
        if (w.Ship.Dead) return Value(start, w);
        // Where the move itself leaves the ship counts too (or it would put off a move it could make later).
        double now = Value(start, w) * 0.5;
        double best = double.MinValue;
        foreach (var next in Then)
        {
            var w2 = w.Copy();
            Run(w2, next, Ahead, charge + Hold);
            best = Math.Max(best, Value(start, w2));
        }
        var w3 = w.Copy();
        Run(w3, m, Ahead, charge + Hold);
        return Math.Max(best, Value(start, w3)) + now;
    }

    private static void Run(World w, (int Dx, int Dy) m, int steps, int charge)
    {
        // Firing as the pilot will: charging the beam and letting it go on the same count, else tapping.
        bool hold = Charging(w);
        for (int i = 0; i < steps && !w.Ship.Dead; i++)
            Sim.Step(w, new Controls(m.Dx, m.Dy, false, hold ? (charge + i + 1) % 123 != 0 : i / 3 % 2 == 0));
    }

    private static double Value(World before, World after)
    {
        var s = after.Ship;
        if (s.Dead) return -1e6 + after.Clock * 1000;
        double v = (after.Score - before.Score) * 1.0;
        // Hurting a boss counts for a lot.
        v += (BossHp(before) - BossHp(after)) * 300;
        if (after.Items.Count < before.Items.Count) v += 2500;
        v += Math.Min(Clearance(after, s.X, s.Y, -1), 100) * 2.5 + Math.Min(Clearance(after, s.X, s.Y, 1), 100) * 2.5;
        v += Math.Min(Clearance(after, s.X + 60, s.Y, -1), 80) + Math.Min(Clearance(after, s.X + 60, s.Y, 1), 80);
        foreach (var m in after.Missiles)
        {
            double d = m.Length > 0 ? Math.Abs(m.Y - s.Y) + Math.Max(0, m.X - s.X - 200) : Math.Sqrt((m.X - s.X) * (m.X - s.X) + (m.Y - s.Y) * (m.Y - s.Y));
            if (d < 60) v -= (60 - d) * 10;
        }
        Enemy? target = null;
        double near = double.MaxValue, bossY = double.NaN, bossX = 0;
        foreach (var e in after.Enemies)
        {
            if (e.Boss)
            {
                if (Sim.WeakSpot(e) is { } spot && double.IsNaN(bossY)) { bossY = spot.Y; bossX = spot.X; }
                foreach (var a in Sim.Armour(e))
                {
                    double d = Math.Max(Math.Abs(a.X - s.X) - a.W, 0) + Math.Max(Math.Abs(a.Y - s.Y) - a.H, 0);
                    if (d < 60) v -= (60 - d) * 10;
                }
                continue;
            }
            double dx = e.X - s.X, dy = e.Y - s.Y, dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 90) v -= (90 - dist) * 6;
            if (dx > 30 && dx < 650 && dx < near) { near = dx; target = e; }
        }
        double sx = s.X - after.CamX;
        if (!double.IsNaN(bossY))
        {
            v -= Math.Abs(s.Y - bossY) * 12;
            v -= Math.Abs(sx - 240) * 1.2;
        }
        else
        {
            v -= Math.Abs(sx - 250) * 1.5;
            if (target != null) v -= Math.Min(Math.Abs(target.Y - s.Y), 220) * 0.8;
        }
        foreach (var it in after.Items)
            if (it.X < after.CamX + 720)
                v -= Math.Min(600, Math.Sqrt((it.X - s.X) * (it.X - s.X) + (it.Y - s.Y) * (it.Y - s.Y))) * 2;
        return v;
    }

    private static int BossHp(World w)
    {
        int hp = 0;
        foreach (var e in w.Enemies) if (e.Boss) hp += e.Hp;
        return hp;
    }

    /// <summary>How far from a point to anything solid, straight up or down, in pixels.</summary>
    private static double Clearance(World w, double x, double y, int dir)
    {
        for (double d = 0; d < 160; d += 8)
        {
            double yy = y + dir * (Ship.HalfHeight + d);
            if (yy < Field.Top || yy > Field.Bottom) return d;
            if (w.AtPoint(x, yy) != Tile.Empty || w.AtPoint(x + Ship.HalfWidth, yy) != Tile.Empty) return d;
        }
        return 160;
    }
}
