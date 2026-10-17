namespace AVARType.Engine;

/// <summary>
/// A stage, step by step: the scrolling, the Lancet with its orb, bits, lasers, missiles and charged beam,
/// everything the Swarm sends, the hits, and the fight at the end. Works on a <see cref="World"/>, so the computer
/// pilot can run it on copies.
/// </summary>
public static class Sim
{
    public const double PeaSpeed = 1000, HoldToCharge = 0.25;
    /// <summary>How long something a porter dropped lasts before it fades.</summary>
    public const double ItemLife = 14;
    private const double Dt = Field.Step, T = Field.Tile;

    // ───────────────────────────── starting ─────────────────────────────

    /// <summary>A stage begun at a checkpoint column, with the ship and orb carried over (none after a loss).</summary>
    public static void Start(World w, int stage, int round, int checkpoint, Ship? carry, Orb? orb, int seed)
    {
        var st = StageBook.All[stage];
        w.Stage = st;
        w.StageIndex = stage;
        w.Round = round;
        w.Grid = (Tile[,])st.Grid.Clone();
        w.Regrow.Clear();
        w.Shots.Clear();
        w.Enemies.Clear();
        w.Missiles.Clear();
        w.Items.Clear();
        w.Effects.Clear();
        w.Heard.Clear();
        w.CamX = Math.Min(checkpoint * T, st.BossCol * T - Field.Width);
        w.Clock = w.BossTime = 0;
        w.BossLock = w.BossUp = w.BossDown = w.Done = w.BossBeaten = false;
        w.Dice = new Dice(seed);
        w.Cause = "";
        w.NextSpawn = 0;
        while (w.NextSpawn < st.Spawns.Count && st.Spawns[w.NextSpawn].Col * T < w.CamX + Field.Width) w.NextSpawn++;
        var s = new Ship();
        if (carry != null)
        {
            s.Speed = carry.Speed;
            s.Bits = carry.Bits;
            s.Missile = carry.Missile;
        }
        int col = (int)((w.CamX + 160) / T);
        s.X = w.CamX + 160;
        s.Y = Math.Round((st.RoofY(col) + st.FloorY(col)) / 2);
        s.Safe = 2;
        w.Ship = s;
        w.Orb = new Orb();
        if (orb != null && orb.State != OrbState.None)
        {
            w.Orb.Level = orb.Level;
            w.Orb.Laser = orb.Laser;
            w.Orb.State = OrbState.Front;
            PlaceOrb(w);
        }
    }

    // ───────────────────────────── a step ─────────────────────────────

    public static void Step(World w, Controls c)
    {
        w.Clock += Dt;
        double scroll = 0;
        if (!w.BossLock)
        {
            scroll = w.Speed * Dt;
            w.CamX += scroll;
            double stop = w.Stage.BossCol * T - Field.Width;
            if (w.CamX >= stop)
            {
                scroll -= w.CamX - stop;
                w.CamX = stop;
                w.BossLock = true;
            }
        }
        var s = w.Ship;
        if (s.Dead) s.DeadTime += Dt;
        else MoveShip(w, c, scroll);
        MoveOrb(w, c, scroll);
        Arrivals(w);
        MoveShots(w);
        for (int i = 0, n = w.Enemies.Count; i < n; i++)
            if (!w.Enemies[i].Dead) Behave(w, w.Enemies[i]);
        foreach (var m in w.Missiles)
        {
            m.X += m.Vx * Dt + scroll;
            m.Y += m.Vy * Dt;
            if (m.X + m.Length < w.CamX - 20 || m.X > w.CamX + Field.Width + 40 || m.Y < Field.Top - 10 || m.Y > Field.Bottom + 10) m.Dead = true;
            else if (m.Length == 0 && w.AtPoint(m.X, m.Y) == Tile.Rock) m.Dead = true;
        }
        Hits(w);
        Ending(w);
        Regrowth(w);
        foreach (var it in w.Items)
        {
            it.T += Dt;
            it.X += scroll * 0.4;
            it.Y += Math.Sin(it.T * 3) * 0.4;
            if (it.X < w.CamX - 40 || it.T > ItemLife) it.Dead = true;
        }
        foreach (var e in w.Effects) e.Age += Dt;
        w.Shots.RemoveAll(x => x.Dead);
        w.Enemies.RemoveAll(x => x.Dead);
        w.Missiles.RemoveAll(x => x.Dead);
        w.Items.RemoveAll(x => x.Dead);
        w.Effects.RemoveAll(x => x.Done);
    }

    // ───────────────────────────── the Lancet ─────────────────────────────

    private static void MoveShip(World w, Controls c, double scroll)
    {
        var s = w.Ship;
        double v = s.Speed0;
        s.X += scroll + c.Dx * v * Dt;
        s.Y += c.Dy * v * Dt;
        s.X = Math.Clamp(s.X, w.CamX + 30, w.CamX + Field.Width - 60);
        s.Y = Math.Clamp(s.Y, Field.Top + 10, Field.Bottom - 10);
        s.Bank += (c.Dy - s.Bank) * Math.Min(1, Dt * 10);
        s.Safe -= Dt;
        s.FireCool -= Dt;
        s.MissileCool -= Dt;
        // Fire: a tap shoots; held, the beam charges, and it goes on letting go.
        if (c.Fire)
        {
            if (!s.FirePrev) Volley(w);
            s.Held += Dt;
            if (s.Held > HoldToCharge)
            {
                if (s.Charge == 0) w.Heard.Add(Sfx.Charge);
                s.Charge = Math.Min(Ship.FullCharge + 0.4, s.Charge + Dt);
            }
        }
        else
        {
            if (s.FirePrev && s.ChargeLevel > 0) Beam(w, s.ChargeLevel);
            s.Held = 0;
            s.Charge = 0;
        }
        s.FirePrev = c.Fire;
        foreach (var (px, py) in new[] { (s.X + Ship.HalfWidth, s.Y), (s.X - Ship.HalfWidth * 0.8, s.Y - Ship.HalfHeight), (s.X - Ship.HalfWidth * 0.8, s.Y + Ship.HalfHeight), (s.X, s.Y - Ship.HalfHeight * 0.7), (s.X, s.Y + Ship.HalfHeight * 0.7) })
            if (w.AtPoint(px, py) != Tile.Empty)
            {
                Kill(w, "the wall");
                return;
            }
    }

    /// <summary>Where each bit is: above the ship, then below.</summary>
    public static IEnumerable<(double X, double Y)> Bits(World w)
    {
        var s = w.Ship;
        if (s.Bits >= 1) yield return (s.X - 4, s.Y - 36 + Math.Sin(w.Clock * 4) * 3);
        if (s.Bits >= 2) yield return (s.X - 4, s.Y + 36 - Math.Sin(w.Clock * 4) * 3);
    }

    /// <summary>A volley: the laser of the orb's crystal if it's fixed to the ship, else peas; the bits and a free orb fire too.</summary>
    private static void Volley(World w)
    {
        var s = w.Ship;
        var o = w.Orb;
        if (s.FireCool > 0) return;
        s.FireCool = 0.1;
        bool fixedOrb = o.State is OrbState.Front or OrbState.Back;
        int dir = o.State == OrbState.Back ? -1 : 1;
        if (fixedOrb && o.Level >= 2)
        {
            switch (o.Laser)
            {
                case Laser.Red:
                    w.Shots.Add(new Shot { X = o.X + dir * 20, Y = o.Y, Vx = dir * 1300, Length = 70, Height = 6 + 6 * o.Level, Damage = o.Level == 3 ? 2 : 1.4, Kind = ShotKind.Ring, Struck = [] });
                    break;
                case Laser.Blue:
                    foreach (double a in o.Level == 3 ? new[] { -0.6, -0.25, 0.25, 0.6 } : new[] { -0.5, 0.5 })
                        w.Shots.Add(new Shot { X = o.X + dir * 16, Y = o.Y, Vx = dir * Math.Cos(a) * 1000, Vy = Math.Sin(a) * 1000, Length = 40, Damage = 1.2, Kind = ShotKind.Reflect });
                    break;
                default:
                    foreach (bool up in new[] { true, false })
                        w.Shots.Add(new Shot { X = o.X + dir * 10, Y = o.Y, Vx = dir * 240, Vy = up ? -620 : 620, Up = up, Damage = o.Level == 3 ? 2.5 : 1.8, Kind = ShotKind.Crawl });
                    break;
            }
            if (dir < 0) Pea(w, s.X + 22, s.Y, 1);
            w.Heard.Add(Sfx.Laser);
        }
        else
        {
            Pea(w, s.X + 22, s.Y, 1);
            if (fixedOrb)
            {
                Pea(w, o.X + dir * 18, o.Y - 6, dir);
                Pea(w, o.X + dir * 18, o.Y + 6, dir);
            }
            w.Heard.Add(Sfx.Pea);
        }
        if (o.State is OrbState.Free) foreach (int k in new[] { -1, 0, 1 }) w.Shots.Add(new Shot { X = o.X + 18, Y = o.Y + k * 8, Vx = 900, Vy = k * 160, Kind = ShotKind.Pellet, Damage = 0.7, Life = 1.2 });
        foreach (var (bx, by) in Bits(w)) Pea(w, bx + 12, by, 1);
        if (s.Missile && s.MissileCool <= 0)
        {
            s.MissileCool = 0.9;
            foreach (int k in new[] { -1, 1 })
                w.Shots.Add(new Shot { X = s.X, Y = s.Y + k * 10, Vx = 300, Vy = k * 260, Kind = ShotKind.Missile, Damage = 2, Life = 2.2 });
            w.Heard.Add(Sfx.MissileOut);
        }
    }

    private static void Pea(World w, double x, double y, int dir)
    {
        int n = 0;
        foreach (var sh in w.Shots) if (sh.Kind == ShotKind.Pea) n++;
        if (n >= 9) return;
        w.Shots.Add(new Shot { X = x, Y = y, Vx = dir * PeaSpeed, Kind = ShotKind.Pea });
    }

    /// <summary>The charged beam: longer, taller and far stronger the longer the charge, through everything in its way.</summary>
    public static void Beam(World w, int level)
    {
        var s = w.Ship;
        w.Shots.Add(new Shot { X = s.X + 30, Y = s.Y, Vx = 1600, Length = 80 + 70 * level, Height = 8 + 10 * level, Damage = level switch { 1 => 2, 2 => 5, _ => 10 }, Kind = ShotKind.Beam, Struck = [], Life = 1.2 });
        w.Heard.Add(Sfx.Beam);
    }

    // ───────────────────────────── the orb ─────────────────────────────

    private static void PlaceOrb(World w)
    {
        var o = w.Orb;
        var s = w.Ship;
        if (o.State == OrbState.Front) { o.X = s.X + 32; o.Y = s.Y; }
        else if (o.State == OrbState.Back) { o.X = s.X - 30; o.Y = s.Y; }
    }

    private static void MoveOrb(World w, Controls c, double scroll)
    {
        var o = w.Orb;
        var s = w.Ship;
        bool press = c.Orb && !s.OrbPrev;
        s.OrbPrev = c.Orb;
        o.FireCool -= Dt;
        if (o.State == OrbState.None) return;
        if (s.Dead) { o.State = OrbState.None; return; }
        switch (o.State)
        {
            case OrbState.Front or OrbState.Back:
                PlaceOrb(w);
                if (press)
                {
                    // Launched: away from the side it was on, to hang near the edge of the screen.
                    o.Vx = o.State == OrbState.Front ? 900 : -700;
                    o.State = OrbState.Free;
                    w.Heard.Add(Sfx.OrbLaunch);
                }
                break;
            case OrbState.Free:
            {
                // Out to near the edge of the screen it was launched toward, and it stays there as the stage goes by.
                double target = o.Vx > 0 ? w.CamX + 860 : w.CamX + 70;
                o.X += scroll + Math.Clamp(target - o.X, -Math.Abs(o.Vx) * Dt, Math.Abs(o.Vx) * Dt);
                // It drifts toward the ship's height.
                o.Y += Math.Clamp(s.Y - o.Y, -1, 1) * Math.Min(Math.Abs(s.Y - o.Y), 130 * Dt);
                if (press) { o.State = OrbState.Recall; w.Heard.Add(Sfx.OrbRecall); }
                else if (Touching(o, s)) Attach(w);
                break;
            }
            case OrbState.Recall:
            {
                double dx = s.X - o.X, dy = s.Y - o.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                o.X += scroll + dx / d * Math.Min(d, 760 * Dt);
                o.Y += dy / d * Math.Min(d, 760 * Dt);
                if (Touching(o, s)) Attach(w);
                break;
            }
        }
    }

    private static bool Touching(Orb o, Ship s) => Math.Abs(o.X - s.X) < 40 && Math.Abs(o.Y - s.Y) < 26;

    /// <summary>The orb fixes itself to the side of the ship it meets.</summary>
    private static void Attach(World w)
    {
        var o = w.Orb;
        o.State = o.X >= w.Ship.X ? OrbState.Front : OrbState.Back;
        PlaceOrb(w);
        w.Heard.Add(Sfx.OrbAttach);
    }

    // ───────────────────────────── shots ─────────────────────────────

    private static void MoveShots(World w)
    {
        foreach (var x in w.Shots)
        {
            x.Life -= Dt;
            switch (x.Kind)
            {
                case ShotKind.Crawl: Crawl(w, x); break;
                case ShotKind.Missile: Home(w, x); break;
                case ShotKind.Reflect:
                {
                    double nx = x.X + x.Vx * Dt, ny = x.Y + x.Vy * Dt;
                    if (w.AtPoint(nx, ny) != Tile.Empty || ny < Field.Top + 2 || ny > Field.Bottom - 2)
                    {
                        if (w.AtPoint(nx, ny) is Tile.Crystal or Tile.Cell) HitTerrain(w, nx, ny);
                        if (++x.Bounces > 3) { x.Dead = true; break; }
                        // Off a floor or roof, the climb turns over; off a wall, it turns back.
                        if (w.AtPoint(x.X, ny) != Tile.Empty || ny < Field.Top + 2 || ny > Field.Bottom - 2) x.Vy = -x.Vy;
                        else x.Vx = -x.Vx;
                        break;
                    }
                    x.X = nx;
                    x.Y = ny;
                    break;
                }
                default:
                    x.X += x.Vx * Dt;
                    x.Y += x.Vy * Dt;
                    if (x.Kind is ShotKind.Beam or ShotKind.Ring)
                    {
                        // Through tissue and armour; stopped by hull and rock.
                        for (double dy = -x.Height / 2; dy <= x.Height / 2; dy += 8)
                            if (w.AtPoint(x.X, x.Y + dy) is Tile.Crystal or Tile.Cell) HitTerrain(w, x.X, x.Y + dy);
                        if (w.AtPoint(x.X, x.Y) == Tile.Rock) x.Dead = true;
                    }
                    else if (HitTerrain(w, x.X, x.Y)) x.Dead = true;
                    break;
            }
            if (x.Life <= 0 || x.X - x.Length > w.CamX + Field.Width + 20 || x.X + x.Length < w.CamX - 40 || x.Y < Field.Top - 20 || x.Y > Field.Bottom + 20) x.Dead = true;
        }
    }

    /// <summary>A yellow laser: up (or down) to the roof (or floor), then along it.</summary>
    private static void Crawl(World w, Shot x)
    {
        if (!x.Running)
        {
            x.X += x.Vx * Dt;
            x.Y += x.Vy * Dt;
            double edge = x.Up ? x.Y - 6 : x.Y + 6;
            if (w.AtPoint(x.X, edge) != Tile.Empty || edge < Field.Top || edge > Field.Bottom)
            {
                if (w.AtPoint(x.X, edge) is Tile.Crystal or Tile.Cell) { HitTerrain(w, x.X, edge); x.Dead = true; return; }
                x.Running = true;
                x.Vx = Math.Sign(x.Vx) * 900;
            }
            return;
        }
        x.X += x.Vx * Dt;
        if (w.AtPoint(x.X + Math.Sign(x.Vx) * 8, x.Y) != Tile.Empty) { HitTerrain(w, x.X + Math.Sign(x.Vx) * 8, x.Y); x.Dead = true; }
    }

    /// <summary>A missile turns toward the nearest thing to hit.</summary>
    private static void Home(World w, Shot x)
    {
        Enemy? target = null;
        double best = 500;
        foreach (var e in w.Enemies)
        {
            if (e.Dead) continue;
            double d = Math.Abs(e.X - x.X) + Math.Abs(e.Y - x.Y);
            if (d < best && e.X > x.X - 40) { best = d; target = e; }
        }
        double tx = target?.X ?? x.X + 400, ty = target?.Y ?? x.Y;
        double a = Math.Atan2(ty - x.Y, tx - x.X), cur = Math.Atan2(x.Vy, x.Vx);
        double turn = Math.Clamp(Math.IEEERemainder(a - cur, Math.PI * 2), -5 * Dt, 5 * Dt);
        double speed = Math.Min(620, Math.Sqrt(x.Vx * x.Vx + x.Vy * x.Vy) + 900 * Dt);
        x.Vx = Math.Cos(cur + turn) * speed;
        x.Vy = Math.Sin(cur + turn) * speed;
        x.X += x.Vx * Dt;
        x.Y += x.Vy * Dt;
        if (HitTerrain(w, x.X, x.Y)) x.Dead = true;
    }

    private static bool HitTerrain(World w, double x, double y)
    {
        int c = (int)Math.Floor(x / T), r = (int)Math.Floor((y - Field.Top) / T);
        var t = w.At(c, r);
        if (t == Tile.Empty) return false;
        if (t != Tile.Rock) Break(w, c, r);
        return true;
    }

    public static void Break(World w, int c, int r)
    {
        var t = w.Grid[c, r];
        w.Grid[c, r] = Tile.Empty;
        w.Score += 10;
        if (t == Tile.Cell) w.Regrow.Add((c, r, 6));
        w.Effects.Add(new Effect { Kind = EffectKind.Spark, X = (c + 0.5) * T, Y = Field.Top + (r + 0.5) * T, Life = 0.3, Colour = t == Tile.Cell ? 0xFFE070A0 : 0xFFC0C8D0 });
        w.Heard.Add(Sfx.Crack);
    }

    private static void Regrowth(World w)
    {
        for (int i = 0; i < w.Regrow.Count; i++)
        {
            var (c, r, t) = w.Regrow[i];
            t -= Dt;
            if (t <= 0)
            {
                double x0 = c * T, y0 = Field.Top + r * T;
                var s = w.Ship;
                if (!s.Dead && s.X + Ship.HalfWidth > x0 && s.X - Ship.HalfWidth < x0 + T && s.Y + Ship.HalfHeight > y0 && s.Y - Ship.HalfHeight < y0 + T) t = 0.5;
                else
                {
                    w.Grid[c, r] = Tile.Cell;
                    w.Regrow.RemoveAt(i--);
                    continue;
                }
            }
            w.Regrow[i] = (c, r, t);
        }
    }

    public static void Kill(World w, string cause)
    {
        var s = w.Ship;
        if (s.Dead) return;
        s.Dead = true;
        s.DeadTime = 0;
        w.Cause = cause;
        w.Orb.State = OrbState.None;
        w.Effects.Add(new Effect { Kind = EffectKind.Blast, X = s.X, Y = s.Y, Life = 1.2, Colour = 0xFFFFC040 });
        w.Heard.Add(Sfx.Die);
    }

    // ───────────────────────────── what comes ─────────────────────────────

    private static void Arrivals(World w)
    {
        var sp = w.Stage.Spawns;
        while (w.NextSpawn < sp.Count && sp[w.NextSpawn].Col * T <= w.CamX + Field.Width + 64)
            Create(w, sp[w.NextSpawn++]);
    }

    private static Enemy New(World w, Foe kind, double x, double y, int hp = 1)
    {
        var e = new Enemy { Kind = kind, X = x, Y = y, HomeY = y, Hp = hp, Id = w.NextId++ };
        w.Enemies.Add(e);
        return e;
    }

    private static void Create(World w, Spawn sp)
    {
        double y = Field.Top + (sp.Row + 0.5) * T;
        switch (sp.Kind)
        {
            case Foe.Flitter:
                for (int i = 0; i < sp.Count; i++)
                {
                    bool behind = sp.Pattern == 2;
                    var e = New(w, Foe.Flitter, behind ? w.CamX - 40 - i * 46 : w.CamX + Field.Width + 30 + i * 46, y);
                    e.Pattern = sp.Pattern;
                    e.Vx = behind ? 330 : -230;
                    e.FireIn = w.Dice.Between(1, 3);
                }
                break;
            case Foe.Gyro:
                for (int i = 0; i < sp.Count; i++)
                {
                    var e = New(w, Foe.Gyro, w.CamX + Field.Width + 30 + i * 60, y);
                    e.Pattern = sp.Pattern;
                    e.Index = i;
                }
                break;
            case Foe.Porter:
            {
                var e = New(w, Foe.Porter, w.CamX + Field.Width + 40, y, 4);
                e.Loot = sp.Loot;
                e.FireIn = 2;
                break;
            }
            default:
            {
                double x = (sp.Col + 0.5) * T;
                var (_, hh) = new Enemy { Kind = sp.Kind }.Size;
                double gy = sp.Roof ? w.Stage.RoofY(sp.Col) + hh : w.Stage.FloorY(sp.Col) - hh;
                var e = New(w, sp.Kind, x, gy, sp.Kind switch { Foe.Hatch => 6, Foe.Gun => 2, Foe.Strider => 3, Foe.Battery => 12, Foe.Pod => 4, _ => 1 });
                e.Roof = sp.Roof;
                e.FireIn = w.Dice.Between(0.6, 1.8);
                break;
            }
        }
    }

    /// <summary>How much fiercer things are: a little more each stage, and more again each round.</summary>
    private static double Rf(World w) => 1 + 0.08 * w.StageIndex + 0.3 * (w.Round - 1);

    private static void Behave(World w, Enemy e)
    {
        e.T += Dt;
        e.Hurt -= Dt;
        e.ChewCool -= Dt;
        var s = w.Ship;
        double sx = s.X, sy = s.Y, rf = Rf(w);
        bool onScreen = e.X > w.CamX - 10 && e.X < w.CamX + Field.Width + 10;
        if (onScreen) e.Entered = true;
        switch (e.Kind)
        {
            case Foe.Flitter:
                switch (e.Pattern)
                {
                    case 0:
                        e.Vx = -200 * rf;
                        e.Y = e.HomeY + 60 * Math.Sin(e.T * 3);
                        break;
                    case 1:
                        if (e.State == 0 && e.X < sx + 260 && e.X < w.CamX + Field.Width - 80)
                        {
                            e.State = 1;
                            e.Vx = -250 * rf;
                            e.Vy = Math.Clamp((sy - e.Y) * 1.8, -210, 210) * rf;
                        }
                        break;
                    case 2:
                        if (e.State == 0 && e.X > sx + 170)
                        {
                            e.State = 1;
                            e.Vx = -270 * rf;
                            e.Vy = Math.Clamp((sy - e.Y) * 1.5, -170, 170) * rf;
                        }
                        break;
                    case 3:
                        e.Vx = -w.Speed;
                        if (e.T > 0.45 && e.State == 0)
                        {
                            e.State = 1;
                            double dx = sx - e.X, dy = sy - e.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                            e.Vx = dx / d * 220 * rf;
                            e.Vy = dy / d * 220 * rf;
                        }
                        break;
                }
                e.X += e.Vx * Dt;
                if (e.Pattern != 0) e.Y += e.Vy * Dt;
                break;
            case Foe.Gyro:
                // Wheeling in on a curve, each a little behind the one before.
                e.X -= 170 * rf * Dt;
                e.Y = e.HomeY + (e.Pattern == 0 ? 1 : -1) * 110 * Math.Sin(e.T * 1.7);
                break;
            case Foe.Porter:
                e.X -= 50 * Dt;
                e.Y = e.HomeY + 30 * Math.Sin(e.T * 1.4);
                FireAt(w, e, 200, 2.5, 3.5);
                break;
            case Foe.Hatch:
                if (!onScreen) break;
                e.FireIn -= Dt;
                if (e.FireIn <= 0)
                {
                    var d = New(w, Foe.Flitter, e.X, e.Y + (e.Roof ? 10 : -10));
                    d.Pattern = 3;
                    d.Vy = e.Roof ? 200 : -200;
                    d.Entered = true;
                    e.State++;
                    e.FireIn = e.State % 4 == 0 ? 3.5 / rf : 0.55;
                }
                break;
            case Foe.Strider:
            {
                double dir = Math.Sign(sx - e.X);
                double nx = e.X + dir * 60 * Dt;
                int nc = (int)(nx / T), oc = (int)(e.X / T);
                double ny = e.Roof ? w.Stage.RoofY(nc) + 22 : w.Stage.FloorY(nc) - 22;
                if (nc == oc || Math.Abs(ny - e.Y) <= T + 1) { e.X = nx; e.Y = ny; }
                FireAt(w, e, 220, 1.8, 2.8);
                break;
            }
            case Foe.Gun:
                if (s.Dead || (e.Roof ? sy < e.Y : sy > e.Y)) break;
                FireAt(w, e, 240, 2.0, 3.0);
                break;
            case Foe.Battery:
            {
                if (!onScreen || s.Dead || (e.Roof ? sy < e.Y : sy > e.Y)) break;
                e.FireIn -= Dt;
                if (e.FireIn > 0) break;
                e.FireIn = w.Dice.Between(2.0, 2.8) / rf;
                double a = Math.Atan2(sy - e.Y, sx - e.X);
                foreach (double k in new[] { -0.22, 0, 0.22 })
                    w.Missiles.Add(new Missile { X = e.X, Y = e.Y, Vx = Math.Cos(a + k) * 230 * rf, Vy = Math.Sin(a + k) * 230 * rf });
                w.Heard.Add(Sfx.EnemyFire);
                break;
            }
            case Foe.Pod:
                if (!onScreen || s.Dead) break;
                e.FireIn -= Dt;
                if (e.FireIn <= 0)
                {
                    double a = Math.Atan2(sy - e.Y, sx - e.X);
                    foreach (int k in new[] { -1, 0, 1 })
                        w.Missiles.Add(new Missile { X = e.X, Y = e.Y, Vx = Math.Cos(a + k * 0.3) * 190 * rf, Vy = Math.Sin(a + k * 0.3) * 190 * rf });
                    e.FireIn = 3.2 / rf;
                    w.Heard.Add(Sfx.Spit);
                }
                break;
            case Foe.Embryo:
            {
                double speed = 100 * rf;
                double dx = sx - e.X, dy = sy - e.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                e.Vx += (dx / d * speed - e.Vx) * Dt * 0.9;
                e.Vy += (dy / d * speed - e.Vy) * Dt * 0.9;
                e.X += e.Vx * Dt;
                e.Y += e.Vy * Dt;
                if (e.T > 8) e.Dead = true;
                break;
            }
            case Foe.Mother: Mother(w, e); break;
            case Foe.Serpent: Serpent(w, e); break;
            case Foe.Engine: Engine(w, e); break;
            case Foe.Overmind: Overmind(w, e); break;
        }
        if (!e.Boss && e.Entered && (e.X < w.CamX - 80 || e.X > w.CamX + Field.Width + 160 || e.Y < Field.Top - 80 || e.Y > Field.Bottom + 80)) e.Dead = true;
    }

    private static void FireAt(World w, Enemy e, double speed, double min, double max)
    {
        if (e.X > w.CamX + Field.Width - 20 || e.X < w.CamX + 20) return;
        e.FireIn -= Dt;
        if (e.FireIn > 0) return;
        double rf = Rf(w);
        e.FireIn = w.Dice.Between(min, max) / rf;
        if (w.Ship.Dead) return;
        double dx = w.Ship.X - e.X, dy = w.Ship.Y - e.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        if (d < 90) return;
        w.Missiles.Add(new Missile { X = e.X, Y = e.Y, Vx = dx / d * speed * rf, Vy = dy / d * speed * rf });
        w.Heard.Add(Sfx.EnemyFire);
    }

    // ───────────────────────────── the ends of the stages ─────────────────────────────

    /// <summary>The time a boss is given before it withdraws (the stage ends all the same, without its points).</summary>
    public const double BossPatience = 75;

    private static void Ending(World w)
    {
        if (!w.BossLock || w.Done) return;
        w.BossTime += Dt;
        if (w.BossDown)
        {
            if (w.BossTime > 3) w.Done = true;
            return;
        }
        if (!w.BossUp && w.BossTime > 1.2)
        {
            w.BossUp = true;
            w.BossTime = 0;
            double mid = (Field.Top + Field.Bottom) / 2;
            int bonus = 10 * (w.Round - 1);
            switch (w.Stage.Boss)
            {
                case Foe.Mother:
                {
                    var e = New(w, Foe.Mother, w.CamX + Field.Width + 160, mid - 20, 60 + bonus);
                    e.Boss = true;
                    break;
                }
                case Foe.Serpent:
                {
                    var e = New(w, Foe.Serpent, w.CamX + Field.Width + 80, mid, 80 + bonus);
                    e.Boss = true;
                    e.Trail = Enumerable.Repeat((e.X, e.Y), 160).ToArray();
                    break;
                }
                case Foe.Engine:
                    for (int i = 0; i < 3; i++)
                    {
                        var e = New(w, Foe.Engine, w.CamX + Field.Width + 120, mid + (i - 1) * 150, 25 + bonus / 2);
                        e.Boss = true;
                        e.Index = i;
                        e.FireIn = 1.5 + i * 0.8;
                    }
                    break;
                default:
                {
                    var e = New(w, Foe.Overmind, w.CamX + Field.Width + 120, mid, 90 + bonus);
                    e.Boss = true;
                    break;
                }
            }
            foreach (var x in w.Enemies) if (x.Boss) x.Entered = true;
            w.Heard.Add(Sfx.BossCome);
            w.Heard.Add(Sfx.SayWarning);
            w.Heard.Add(Sfx.BossMusic);
        }
        if (w.BossUp && w.BossTime > BossPatience)
            foreach (var x in w.Enemies)
                if (x.Boss && x.State != 9) { x.State = 9; w.Heard.Add(Sfx.BossLeave); }
    }

    /// <summary>Withdrawing, after too long: off to the right, and the stage is over.</summary>
    private static bool Leaving(World w, Enemy e)
    {
        if (e.State != 9) return false;
        e.X += 200 * Dt;
        if (e.X > w.CamX + Field.Width + 200)
        {
            e.Dead = true;
            if (!w.Enemies.Any(x => x.Boss && !x.Dead)) { w.BossDown = true; w.BossTime = 0; }
        }
        return true;
    }

    /// <summary>Where the Hollow Mother's tail is: ten segments swinging from under her.</summary>
    public static IEnumerable<(double X, double Y)> Tail(Enemy e)
    {
        for (int i = 1; i <= 10; i++)
            yield return (e.X - 40 - i * 30, e.Y + 150 - i * 6 + Math.Sin(e.T * 1.3 - i * 0.45) * i * 16);
    }

    /// <summary>The Hollow Mother's core, low in her belly: the only place she can be hurt.</summary>
    public static (double X, double Y) MotherCore(Enemy e) => (e.X - 60, e.Y + 40);

    private static void Mother(World w, Enemy e)
    {
        if (Leaving(w, e)) return;
        double home = w.CamX + 830;
        if (e.X > home) { e.X = Math.Max(home, e.X - 160 * Dt); return; }
        e.Y = e.HomeY + 20 * Math.Sin(e.T * 0.6);
        e.FireIn -= Dt;
        if (e.FireIn <= 0)
        {
            e.FireIn = 2.2 / Rf(w);
            e.Pattern++;
            if (e.Pattern % 2 == 0)
            {
                var b = New(w, Foe.Embryo, e.X - 100, e.Y - 70);
                b.Entered = true;
                b.Vx = -80;
            }
            else
            {
                // A fan out from the core to either side of its line (the line itself is clear).
                var (cx, cy) = MotherCore(e);
                foreach (double k in new[] { -0.6, -0.35, 0.35, 0.6 })
                    w.Missiles.Add(new Missile { X = cx, Y = cy, Vx = -Math.Cos(k) * 230 * Rf(w), Vy = Math.Sin(k) * 230 * Rf(w) });
                w.Heard.Add(Sfx.Spit);
            }
        }
    }

    private static void Serpent(World w, Enemy e)
    {
        if (Leaving(w, e)) { Shift(e); return; }
        double mid = (Field.Top + Field.Bottom) / 2;
        // Coiling round the lair, a figure of eight.
        double tx = w.CamX + 600 + 300 * Math.Cos(e.T * 0.5), ty = mid + 230 * Math.Sin(e.T * 1.0);
        e.X += Math.Clamp(tx - e.X, -260 * Dt, 260 * Dt);
        e.Y += Math.Clamp(ty - e.Y, -260 * Dt, 260 * Dt);
        Shift(e);
        e.FireIn -= Dt;
        if (e.FireIn <= 0 && !w.Ship.Dead)
        {
            e.FireIn = 2.2 / Rf(w);
            double a = Math.Atan2(w.Ship.Y - e.Y, w.Ship.X - e.X);
            foreach (double k in new[] { -0.25, 0, 0.25 })
                w.Missiles.Add(new Missile { X = e.X, Y = e.Y, Vx = Math.Cos(a + k) * 240 * Rf(w), Vy = Math.Sin(a + k) * 240 * Rf(w) });
            w.Heard.Add(Sfx.Spit);
        }
    }

    private static void Shift(Enemy e)
    {
        for (int i = e.Trail.Length - 1; i > 0; i--) e.Trail[i] = e.Trail[i - 1];
        if (e.Trail.Length > 0) e.Trail[0] = (e.X, e.Y);
    }

    /// <summary>The Serpent's body segments: every tenth place its head has been.</summary>
    public static IEnumerable<(double X, double Y)> Body(Enemy e)
    {
        for (int i = 10; i < e.Trail.Length; i += 10) yield return e.Trail[i];
    }

    private static void Engine(World w, Enemy e)
    {
        if (Leaving(w, e)) return;
        double home = w.CamX + 870;
        if (e.X > home) { e.X = Math.Max(home, e.X - 160 * Dt); return; }
        e.FireIn -= Dt;
        if (e.FireIn <= 0)
        {
            // A jet of flame straight out.
            e.FireIn = 2.6 / Rf(w);
            w.Missiles.Add(new Missile { X = e.X - 60, Y = e.Y, Vx = -560 * Rf(w), Length = 110 });
            w.Heard.Add(Sfx.Flame);
        }
    }

    /// <summary>The Overmind's shell is open (and it can be hurt) for three seconds of every six.</summary>
    public static bool Open(Enemy e) => e.T % 6 > 3;

    private static void Overmind(World w, Enemy e)
    {
        if (Leaving(w, e)) return;
        double home = w.CamX + 850;
        if (e.X > home) { e.X = Math.Max(home, e.X - 160 * Dt); e.T = 0; return; }
        e.Y = e.HomeY + 80 * Math.Sin(e.T * 0.6);
        e.FireIn -= Dt;
        if (e.FireIn > 0) return;
        if (Open(e))
        {
            e.FireIn = 1.1;
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4 + e.T;
                w.Missiles.Add(new Missile { X = e.X - 40, Y = e.Y, Vx = Math.Cos(a) * 200 * Rf(w) - 60, Vy = Math.Sin(a) * 200 * Rf(w) });
            }
            w.Heard.Add(Sfx.Spit);
        }
        else
        {
            e.FireIn = 1.4;
            foreach (double oy in new[] { -90.0, 90.0 })
            {
                var b = New(w, Foe.Embryo, e.X - 50, e.Y + oy);
                b.Entered = true;
                b.Vx = -60;
            }
        }
    }

    // ───────────────────────────── hits ─────────────────────────────

    private static (double X0, double Y0, double X1, double Y1) Box(Shot x) => x.Kind switch
    {
        ShotKind.Beam or ShotKind.Ring => (x.X - x.Length, x.Y - x.Height / 2, x.X, x.Y + x.Height / 2),
        ShotKind.Reflect => (x.X - 10, x.Y - 6, x.X + 10, x.Y + 6),
        ShotKind.Crawl or ShotKind.Missile => (x.X - 8, x.Y - 6, x.X + 8, x.Y + 6),
        _ => (x.X - 9, x.Y - 4, x.X + 9, x.Y + 4),
    };

    /// <summary>Where a boss can be hurt (or nowhere just now).</summary>
    public static (double X, double Y, double W, double H)? WeakSpot(Enemy e) => e.Kind switch
    {
        Foe.Mother => (e.X - 60, e.Y + 40, 30, 30),
        Foe.Serpent => (e.X, e.Y, 26, 26),
        Foe.Engine => (e.X - 20, e.Y, 30, 30),
        Foe.Overmind => Open(e) ? (e.X - 40, e.Y, 40, 40) : null,
        _ => null,
    };

    /// <summary>The parts of a boss that stop shots (and the ship) but can't be hurt.</summary>
    public static IEnumerable<(double X, double Y, double W, double H)> Armour(Enemy e)
    {
        switch (e.Kind)
        {
            case Foe.Mother:
                yield return (e.X + 20, e.Y - 20, 100, 170);
                foreach (var (x, y) in Tail(e)) yield return (x, y, 16, 16);
                break;
            case Foe.Serpent:
                foreach (var (x, y) in Body(e)) yield return (x, y, 20, 20);
                break;
            case Foe.Engine:
                yield return (e.X + 40, e.Y, 60, 60);
                break;
            case Foe.Overmind:
                yield return (e.X + 20, e.Y, 90, 110);
                break;
        }
    }

    private static bool Overlap((double X0, double Y0, double X1, double Y1) b, (double X, double Y, double W, double H) r) =>
        b.X1 >= r.X - r.W && b.X0 <= r.X + r.W && b.Y1 >= r.Y - r.H && b.Y0 <= r.Y + r.H;

    private static void Hits(World w)
    {
        foreach (var x in w.Shots)
        {
            if (x.Dead) continue;
            var box = Box(x);
            foreach (var e in w.Enemies)
            {
                if (e.Dead) continue;
                bool pierce = x.Struck != null;
                if (pierce && x.Struck!.Contains(e.Id)) continue;
                if (e.Boss)
                {
                    if (e.State != 9 && WeakSpot(e) is { } spot && Overlap(box, spot))
                    {
                        if (pierce) x.Struck!.Add(e.Id); else x.Dead = true;
                        Damage(w, e, x.Damage);
                        if (x.Dead) break;
                        continue;
                    }
                    if (Armour(e).Any(a => Overlap(box, a)))
                    {
                        if (x.Kind != ShotKind.Beam) { x.Dead = true; w.Heard.Add(Sfx.Clink); break; }
                    }
                    continue;
                }
                var (hw, hh) = e.Size;
                if (!Overlap(box, (e.X, e.Y, hw, hh))) continue;
                if (pierce) x.Struck!.Add(e.Id); else x.Dead = true;
                Damage(w, e, x.Damage);
                if (x.Dead) break;
            }
        }
        // The orb and the bits: they chew up what they touch, and soak up shots.
        var o = w.Orb;
        var guards = new List<(double X, double Y, double R, bool Orb)>();
        if (o.State != OrbState.None) guards.Add((o.X, o.Y, Orb.Radius, true));
        if (!w.Ship.Dead) foreach (var (bx, by) in Bits(w)) guards.Add((bx, by, 9, false));
        foreach (var (gx, gy, gr, isOrb) in guards)
        {
            foreach (var m in w.Missiles)
                if (!m.Dead && m.Length == 0 && Math.Abs(m.X - gx) < gr + 4 && Math.Abs(m.Y - gy) < gr + 4) m.Dead = true;
            foreach (var e in w.Enemies)
            {
                if (e.Dead || e.ChewCool > 0) continue;
                var target = e.Boss ? WeakSpot(e) : (e.X, e.Y, e.Size.W, e.Size.H);
                if (target is not { } t || !Overlap((gx - gr, gy - gr, gx + gr, gy + gr), t)) continue;
                e.ChewCool = e.Boss ? 0.3 : 0.1;
                Damage(w, e, isOrb ? 1 : 0.5);
                w.Heard.Add(Sfx.Chew);
            }
        }
        var s = w.Ship;
        if (s.Dead) return;
        bool safe = s.Safe > 0;
        var ship = (s.X - Ship.HalfWidth * 0.7, s.Y - Ship.HalfHeight * 0.8, s.X + Ship.HalfWidth * 0.7, s.Y + Ship.HalfHeight * 0.8);
        foreach (var e in w.Enemies)
        {
            if (e.Dead || safe && !e.Boss) continue;
            if (e.Boss)
            {
                if (Armour(e).Any(a => Overlap(ship, a)) || WeakSpot(e) is { } ws && Overlap(ship, ws))
                {
                    Kill(w, e.Kind switch { Foe.Mother => "the Hollow Mother", Foe.Serpent => "the Serpent", Foe.Engine => "the engines", _ => "the Overmind" });
                    return;
                }
                continue;
            }
            if (!Overlap(ship, (e.X, e.Y, e.Size.W, e.Size.H))) continue;
            Kill(w, Name(e.Kind));
            return;
        }
        foreach (var m in w.Missiles)
        {
            if (m.Dead || safe) continue;
            bool hit = m.Length > 0
                ? s.Y + Ship.HalfHeight * 0.7 > m.Y - 6 && s.Y - Ship.HalfHeight * 0.7 < m.Y + 6 && s.X + Ship.HalfWidth * 0.8 > m.X && s.X - Ship.HalfWidth * 0.8 < m.X + m.Length
                : Math.Abs(m.X - s.X) < Ship.HalfWidth * 0.6 + 4 && Math.Abs(m.Y - s.Y) < Ship.HalfHeight * 0.7 + 4;
            if (!hit) continue;
            Kill(w, m.Length > 0 ? "a jet of flame" : "a shot");
            return;
        }
        foreach (var it in w.Items)
        {
            if (it.Dead || Math.Abs(it.X - s.X) > 32 || Math.Abs(it.Y - s.Y) > 26) continue;
            it.Dead = true;
            Take(w, it.Loot);
        }
    }

    /// <summary>What a porter dropped, picked up.</summary>
    public static void Take(World w, Loot loot)
    {
        var s = w.Ship;
        var o = w.Orb;
        w.Score += 500;
        switch (loot)
        {
            case Loot.RedCrystal or Loot.BlueCrystal or Loot.YellowCrystal:
            {
                var colour = loot switch { Loot.RedCrystal => Laser.Red, Loot.BlueCrystal => Laser.Blue, _ => Laser.Yellow };
                if (o.State == OrbState.None)
                {
                    // The orb arrives, from behind, and fixes itself to the ship's nose.
                    o.State = OrbState.Front;
                    o.Level = 1;
                    o.Laser = colour;
                    PlaceOrb(w);
                    w.Heard.Add(Sfx.OrbAttach);
                }
                else
                {
                    o.Level = Math.Min(3, o.Level + 1);
                    o.Laser = colour;
                }
                w.Heard.Add(Sfx.PowerUp);
                break;
            }
            case Loot.Speed: s.Speed = Math.Min(3, s.Speed + 1); w.Heard.Add(Sfx.PowerUp); break;
            case Loot.Bit: s.Bits = Math.Min(2, s.Bits + 1); w.Heard.Add(Sfx.PowerUp); break;
            case Loot.Missile: s.Missile = true; w.Heard.Add(Sfx.PowerUp); break;
        }
    }

    private static void Damage(World w, Enemy e, double amount)
    {
        // Fractions count by chance: a weak shot still tells in the end.
        int whole = (int)Math.Floor(amount);
        if (w.Dice.Next() < amount - whole) whole++;
        if (whole <= 0) return;
        e.Hp -= whole;
        e.Hurt = 0.1;
        if (e.Hp <= 0) Destroy(w, e);
        else w.Heard.Add(e.Boss ? Sfx.BossHit : Sfx.Hit);
    }

    public static string Name(Foe f) => f switch
    {
        Foe.Flitter => "a flitter",
        Foe.Gyro => "a gyro",
        Foe.Hatch => "a hatch",
        Foe.Strider => "a strider",
        Foe.Gun => "a gun",
        Foe.Porter => "a porter",
        Foe.Battery => "a battery",
        Foe.Pod => "a pod",
        Foe.Embryo => "an embryo",
        _ => f.ToString(),
    };

    public static int Points(Foe f) => f switch
    {
        Foe.Flitter => 100,
        Foe.Gyro => 150,
        Foe.Hatch => 800,
        Foe.Strider => 300,
        Foe.Gun => 300,
        Foe.Porter => 400,
        Foe.Battery => 1200,
        Foe.Pod => 500,
        Foe.Embryo => 50,
        Foe.Engine => 5000,
        Foe.Overmind => 50000,
        _ => 15000,
    };

    public static void Destroy(World w, Enemy e)
    {
        if (e.Dead) return;
        e.Dead = true;
        int pts = Points(e.Kind);
        w.Score += pts;
        if (e.Boss)
        {
            for (int i = 0; i < 6; i++)
                w.Effects.Add(new Effect { Kind = EffectKind.Blast, X = e.X + w.Dice.Between(-90, 90), Y = e.Y + w.Dice.Between(-70, 70), Life = 0.9 + i * 0.25, Colour = 0xFFFFB040 });
            if (!w.Enemies.Any(x => x.Boss && !x.Dead))
            {
                w.BossDown = w.BossBeaten = true;
                w.BossTime = 0;
                foreach (var o in w.Enemies) if (!o.Dead) { o.Dead = true; Burst(w, o.X, o.Y, 0xFFFFD080, 8); }
                foreach (var m in w.Missiles) m.Dead = true;
                w.Heard.Add(Sfx.BossDie);
                w.Heard.Add(Sfx.MusicStop);
            }
            else w.Heard.Add(Sfx.BigBoom);
        }
        else
        {
            Burst(w, e.X, e.Y, 0xFFFFD080, e.Kind is Foe.Hatch or Foe.Battery or Foe.Porter ? 14 : 9);
            w.Heard.Add(e.Kind is Foe.Hatch or Foe.Battery or Foe.Pod or Foe.Porter ? Sfx.BigBoom : Sfx.Boom);
        }
        if (pts >= 300) w.Effects.Add(new Effect { Kind = EffectKind.Score, X = e.X, Y = e.Y - 20, Text = pts.ToString(), Colour = 0xFFFFE070 });
        if (e.Loot != Loot.None) w.Items.Add(new Item { X = e.X, Y = e.Y, Loot = e.Loot });
    }

    private static void Burst(World w, double x, double y, uint colour, int n) =>
        w.Effects.Add(new Effect { Kind = EffectKind.Burst, X = x, Y = y, Text = n.ToString(), Life = 0.5, Colour = colour });
}
