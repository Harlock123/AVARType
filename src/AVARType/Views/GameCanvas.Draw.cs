using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVARType.Engine;
using Effect = AVARType.Engine.Effect;

namespace AVARType.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVARType/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused or GameState.GameOver;

    private const double T = Field.Tile;

    /// <summary>Each stage's colours: the space behind, the rock, its lit edge, and a glow.</summary>
    private static readonly (uint Space, uint Haze, uint Rock, uint Edge, uint Glow)[] Themes =
    [
        (0xFF04060E, 0xFF101830, 0xFF3A4458, 0xFFB8A888, 0xFF60C0FF),   // the outer wall: steel and bone
        (0xFF0A0408, 0xFF2A0A18, 0xFF5A1E30, 0xFFD06080, 0xFFFF80B0),   // the spawning caves: flesh
        (0xFF040608, 0xFF141A22, 0xFF4A525E, 0xFF9AA6B8, 0xFFFFC040),   // the dreadnought: hull plate
        (0xFF080206, 0xFF2A0610, 0xFF4A1020, 0xFFA03050, 0xFFFF6060),   // the heart: dark, living red
    ];

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame) DrawStage(dc);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(512, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, Field.Bottom - 16), 24, Yellow, center: true);
    }

    // ───────────────────────────── the stage ─────────────────────────────

    private void DrawStage(DrawingContext dc)
    {
        var w = Engine.World;
        var th = Themes[w.Stage.Theme];
        using (dc.PushClip(new Rect(0, Field.Top, Field.Width, Field.Bottom - Field.Top)))
        {
            DrawSpace(dc, w.CamX, w.Stage.Theme, th);
            using (dc.PushTransform(Matrix.CreateTranslation(-Math.Round(w.CamX), 0)))
            {
                int c0 = (int)(w.CamX / T) - 1, c1 = c0 + Field.ViewCols + 3;
                for (int c = Math.Max(0, c0); c < Math.Min(w.Stage.Cols, c1); c++)
                for (int r = 0; r < Field.Rows; r++)
                    DrawTile(dc, w, c, r, th);
                foreach (var it in w.Items) DrawItem(dc, it);
                foreach (var e in w.Enemies) DrawEnemy(dc, e, w);
                foreach (var m in w.Missiles) DrawMissile(dc, m);
                foreach (var s in w.Shots) DrawShot(dc, s);
                DrawShip(dc, w);
                foreach (var e in w.Effects) DrawEffect(dc, e);
            }
        }
    }

    /// <summary>Stars drifting at three depths, and something of each stronghold far off.</summary>
    private void DrawSpace(DrawingContext dc, double camX, int theme, (uint Space, uint Haze, uint Rock, uint Edge, uint Glow) th)
    {
        // Haze toward the ground and the roof, dark in the middle.
        for (int i = 0; i < 40; i++)
        {
            double f = Math.Pow(Math.Abs(i / 39.0 - 0.5) * 2, 2);
            uint a = th.Space, b = th.Haze;
            uint Mix(int shift) => (uint)((((a >> shift) & 0xFF) * (1 - f) + ((b >> shift) & 0xFF) * f)) << shift;
            dc.FillRectangle(Brush(0xFF000000 | Mix(16) | Mix(8) | Mix(0)), new Rect(0, Field.Top + i * 16, Field.Width, 17));
        }
        for (int layer = 0; layer < 3; layer++)
        {
            double speed = 0.08 + layer * 0.14;
            for (int i = 0; i < 40; i++)
            {
                uint h = Hash(i, layer, 7);
                double x = ((h % 1100) - camX * speed) % 1100;
                if (x < 0) x += 1100;
                double y = Field.Top + (h >> 12) % 640;
                double tw = 0.5 + 0.5 * Math.Sin(_clock * (2 + h % 5) + i);
                dc.FillRectangle(Brush(Alpha(layer == 2 ? 0xFFFFFFFF : 0xFFA0B0FF, 0.35 + layer * 0.25 + tw * 0.2)), new Rect(x - 20, y, 1 + layer, 1 + layer));
            }
        }
        double far = -camX * 0.12;
        switch (theme)
        {
            case 0:
                // A red giant sun, low.
                dc.FillEllipse(760, 560, 120, 0x40FF4020);
                dc.FillEllipse(760, 560, 80, 0x60FF6030);
                break;
            case 1:
                for (int i = 0; i < 5; i++)
                {
                    double x = ((far * 2 + i * 260) % 1300 + 1300) % 1300 - 140;
                    Poly(dc, 0x2060E0FF, null, 0, new Point(x, Field.Bottom), new Point(x + 40, Field.Top + 200 + i * 30), new Point(x + 80, Field.Bottom));
                }
                break;
            case 2:
                for (int i = 0; i < 6; i++)
                {
                    double x = ((far * 1.5 + i * 220) % 1320 + 1320) % 1320 - 150;
                    dc.FillEllipse(x, Field.Top + 120 + (Hash(i, 2, 2) % 400), 60 + Hash(i, 3, 3) % 50, 0x30C04080);
                }
                break;
            case 3:
                for (int i = 0; i < 14; i++)
                {
                    double x = ((far * 2 + i * 80) % 1120 + 1120) % 1120 - 40;
                    dc.DrawLine(Pen(0x203A5A8A, 2), new Point(x, Field.Top), new Point(x, Field.Bottom));
                }
                for (int j = 0; j < 8; j++) dc.DrawLine(Pen(0x183A5A8A, 2), new Point(0, Field.Top + 40 + j * 80), new Point(Field.Width, Field.Top + 40 + j * 80));
                break;
        }
    }

    private void DrawTile(DrawingContext dc, World w, int c, int r, (uint Space, uint Haze, uint Rock, uint Edge, uint Glow) th)
    {
        var t = w.At(c, r);
        if (t == Tile.Empty) return;
        double x = c * T, y = Field.Top + r * T;
        uint h = Hash(c, r, w.Stage.Theme);
        switch (t)
        {
            case Tile.Rock:
            {
                dc.FillRectangle(Brush(th.Rock), new Rect(x, y, T + 0.5, T + 0.5));
                if (w.Stage.Theme == 3)
                {
                    // Steel panels and rivets.
                    dc.DrawRectangle(null, Pen(0xFF20262E, 2), new Rect(x + 1, y + 1, T - 2, T - 2));
                    dc.FillEllipse(x + 5, y + 5, 1.5, 0xFF6A7488);
                    dc.FillEllipse(x + T - 5, y + T - 5, 1.5, 0xFF6A7488);
                }
                else
                {
                    for (int k = 0; k < 3; k++)
                    {
                        uint hk = h >> (k * 8);
                        dc.FillEllipse(x + 4 + hk % 24, y + 4 + (hk >> 4) % 24, 2 + hk % 3, Alpha(0xFF000000, 0.22));
                    }
                }
                // The lit edge where it meets the open.
                bool up = w.At(c, r - 1) == Tile.Empty && r > 0, down = w.At(c, r + 1) == Tile.Empty && r < Field.Rows - 1;
                bool left = w.At(c - 1, r) == Tile.Empty, right = w.At(c + 1, r) == Tile.Empty;
                if (up) dc.FillRectangle(Brush(th.Edge), new Rect(x, y, T + 0.5, 4));
                if (down) dc.FillRectangle(Brush(th.Edge), new Rect(x, y + T - 4, T + 0.5, 4));
                if (left) dc.FillRectangle(Brush(Alpha(th.Edge, 0.7)), new Rect(x, y, 3, T));
                if (right) dc.FillRectangle(Brush(Alpha(th.Edge, 0.7)), new Rect(x + T - 3, y, 3, T));
                if (w.Stage.Theme == 0 && up && h % 5 == 0) dc.FillEllipse(x + 8 + h % 16, y + 2, 3, 0xFFFFA040);
                if (w.Stage.Theme == 2 && (up || down) && h % 3 == 0) dc.FillEllipse(x + 6 + h % 20, up ? y + 3 : y + T - 3, 4, 0xFFE070A0);
                break;
            }
            case Tile.Crystal when w.Stage.Theme == 3:
                // A shutter: hazard stripes.
                dc.FillRectangle(Brush(0xFF2A2A1A), new Rect(x + 2, y, T - 4, T + 0.5));
                for (int k = 0; k < 4; k++)
                    Poly(dc, 0xFFE0B020, null, 0, new Point(x + 2, y + k * 8), new Point(x + 2, y + k * 8 + 4), new Point(x + T - 2, y + k * 8 + 8), new Point(x + T - 2, y + k * 8 + 4));
                dc.DrawRectangle(null, Pen(0xFF8A7A30, 2), new Rect(x + 2, y, T - 4, T));
                break;
            case Tile.Crystal:
            {
                double gl = 0.55 + 0.25 * Math.Sin(_clock * 3 + c * 0.7 + r);
                Poly(dc, Alpha(0xFF60D8F0, gl), 0xFFC0F8FF, 1.5, new Point(x + T / 2, y + 1), new Point(x + T - 2, y + T * 0.35), new Point(x + T - 3, y + T - 2), new Point(x + 3, y + T - 2), new Point(x + 2, y + T * 0.35));
                dc.DrawLine(Pen(Alpha(0xFFFFFFFF, 0.6), 2), new Point(x + 9, y + 9), new Point(x + 13, y + 22));
                break;
            }
            case Tile.Cell:
            {
                double pulse = 1 + 0.06 * Math.Sin(_clock * 4 + h % 7);
                dc.FillEllipse(x + T / 2, y + T / 2, 15 * pulse, 0xFFB04A78);
                dc.FillEllipse(x + T / 2 - 3, y + T / 2 - 3, 8 * pulse, 0xFFE07AA8);
                dc.FillEllipse(x + T / 2 + 4, y + T / 2 + 3, 3, 0xFF5A1A38);
                break;
            }
        }
    }

    // ───────────────────────────── the Lancet ─────────────────────────────

    private void DrawShip(DrawingContext dc, World w)
    {
        var s = w.Ship;
        var o = w.Orb;
        if (!s.Dead)
        {
            foreach (var (bx, by) in Sim.Bits(w)) DrawBit(dc, bx, by);
            if (!(s.Safe > 0 && (int)(_clock * 14) % 2 == 0)) DrawLancet(dc, s.X, s.Y, s.Bank, s.Charge);
        }
        if (o.State != OrbState.None) DrawOrb(dc, o.X, o.Y, o.Level, o.Laser, o.State);
    }

    private void DrawLancet(DrawingContext dc, double x, double y, double bank, double charge = 0)
    {
        double b = bank * 3;
        double fl = 10 + 6 * Math.Sin(_clock * 40);
        Poly(dc, 0xFF40C0FF, null, 0, new Point(x - 18, y - 4), new Point(x - 18 - fl, y), new Point(x - 18, y + 4));
        Poly(dc, 0xFFE0F8FF, null, 0, new Point(x - 18, y - 2), new Point(x - 18 - fl * 0.5, y), new Point(x - 18, y + 2));
        // Swept fins top and bottom, the long hull, and a canopy set well back.
        Poly(dc, 0xFF3A4A6A, 0xFF1A2232, 1, new Point(x - 6, y - 4), new Point(x - 20, y - 17 + b), new Point(x - 12, y - 17 + b), new Point(x + 4, y - 4));
        Poly(dc, 0xFF3A4A6A, 0xFF1A2232, 1, new Point(x - 6, y + 4), new Point(x - 20, y + 17 + b), new Point(x - 12, y + 17 + b), new Point(x + 4, y + 4));
        Poly(dc, 0xFFD8E0F0, 0xFF4A5A7A, 1.5, new Point(x + 26, y + 1), new Point(x + 10, y - 7), new Point(x - 18, y - 7), new Point(x - 22, y), new Point(x - 18, y + 7), new Point(x + 10, y + 7));
        dc.FillRectangle(Brush(0xFF2A6AD0), new Rect(x - 16, y + 1, 30, 3));
        dc.DrawEllipse(Brush(0xFFFFB040), Pen(0xFF8A5A10, 1), new Point(x - 2, y - 4), 7, 3.5);
        // The beam gathering at the nose.
        if (charge > Sim.HoldToCharge)
        {
            double k = Math.Min(1, charge / Ship.FullCharge), r = 4 + k * 12 + Math.Sin(_clock * 40) * 2;
            dc.FillEllipse(x + 30, y, r, Alpha(0xFF80E0FF, 0.6));
            dc.FillEllipse(x + 30, y, r * 0.5, 0xFFFFFFFF);
        }
    }

    /// <summary>The orb: a glowing core in a ring of armour, with spikes; its glow the colour of its crystal.</summary>
    private void DrawOrb(DrawingContext dc, double x, double y, int level, Laser laser, OrbState state)
    {
        uint glow = laser switch { Laser.Red => 0xFFFF5030, Laser.Blue => 0xFF40A0FF, _ => 0xFFFFE040 };
        double p = 1 + 0.08 * Math.Sin(_clock * 12);
        dc.FillEllipse(x, y, (Orb.Radius + 6) * p, Alpha(glow, 0.3));
        for (int k = 0; k < 6; k++)
        {
            double a = k * Math.PI / 3 + _clock * (state is OrbState.Free or OrbState.Recall ? 6 : 1.5);
            Poly(dc, 0xFFB0B8C8, null, 0, new Point(x + Math.Cos(a - 0.2) * 14, y + Math.Sin(a - 0.2) * 14), new Point(x + Math.Cos(a) * 22, y + Math.Sin(a) * 22), new Point(x + Math.Cos(a + 0.2) * 14, y + Math.Sin(a + 0.2) * 14));
        }
        dc.DrawEllipse(Brush(0xFF6A7488), Pen(0xFF2A3040, 2), new Point(x, y), 15, 15);
        dc.FillEllipse(x, y, 9 + level, glow);
        dc.FillEllipse(x - 3, y - 3, 3.5, 0xFFFFFFFF);
    }

    private void DrawBit(DrawingContext dc, double x, double y)
    {
        dc.FillEllipse(x, y, 11, 0x50FF8020);
        dc.DrawEllipse(Brush(0xFFFF9030), Pen(0xFF8A4010, 1.5), new Point(x, y), 8, 8);
        dc.FillEllipse(x - 2, y - 2, 3, 0xFFFFE0A0);
    }

    private void DrawShot(DrawingContext dc, Shot s)
    {
        switch (s.Kind)
        {
            case ShotKind.Pea:
                dc.FillRectangle(Brush(0xFFFFF0A0), new Rect(s.X - 9, s.Y - 2, 18, 4), 2);
                break;
            case ShotKind.Pellet:
                dc.FillEllipse(s.X, s.Y, 4, 0xFFFFC060);
                break;
            case ShotKind.Ring:
            {
                // The red laser: a spiral of rings.
                for (int i = 0; i < 5; i++)
                {
                    double rx = s.X - i * s.Length / 5;
                    dc.DrawEllipse(null, Pen(Alpha(0xFFFF6040, 1 - i * 0.15), 3), new Point(rx, s.Y), 6, s.Height / 2);
                }
                dc.FillRectangle(Brush(0xFFFFC0A0), new Rect(s.X - s.Length, s.Y - 1.5, s.Length, 3));
                break;
            }
            case ShotKind.Reflect:
            {
                double a = Math.Atan2(s.Vy, s.Vx);
                dc.DrawLine(Pen(0x6040A0FF, 8), new Point(s.X - Math.Cos(a) * s.Length, s.Y - Math.Sin(a) * s.Length), new Point(s.X, s.Y));
                dc.DrawLine(Pen(0xFFA0E0FF, 3), new Point(s.X - Math.Cos(a) * s.Length, s.Y - Math.Sin(a) * s.Length), new Point(s.X, s.Y));
                break;
            }
            case ShotKind.Crawl:
                dc.FillRectangle(Brush(0x80FFE040), new Rect(s.X - 16, s.Y - 4, 32, 8), 3);
                dc.FillRectangle(Brush(0xFFFFFFA0), new Rect(s.X - 12, s.Y - 1.5, 24, 3));
                break;
            case ShotKind.Missile:
            {
                double a = Math.Atan2(s.Vy, s.Vx);
                using (dc.PushTransform(Matrix.CreateRotation(a) * Matrix.CreateTranslation(s.X, s.Y)))
                {
                    dc.FillRectangle(Brush(0xFFC0C8D8), new Rect(-7, -2.5, 12, 5), 2);
                    Poly(dc, 0xFFE04040, null, 0, new Point(5, -2.5), new Point(9, 0), new Point(5, 2.5));
                    dc.FillEllipse(-9, 0, 2.5 + Math.Sin(_clock * 50), 0xFFFFC040);
                }
                break;
            }
            case ShotKind.Beam:
            {
                double h = s.Height;
                dc.FillRectangle(Brush(0x5060C0FF), new Rect(s.X - s.Length, s.Y - h, s.Length, h * 2), (float)h);
                dc.FillRectangle(Brush(0xFF90E0FF), new Rect(s.X - s.Length, s.Y - h * 0.55, s.Length, h * 1.1), (float)(h * 0.5));
                dc.FillRectangle(Brush(0xFFFFFFFF), new Rect(s.X - s.Length * 0.9, s.Y - h * 0.2, s.Length * 0.9, h * 0.4));
                break;
            }
        }
    }

    private void DrawMissile(DrawingContext dc, Missile m)
    {
        if (m.Length > 0)
        {
            for (int i = 0; i < 6; i++)
            {
                double fx = m.X + i * m.Length / 6, r = 7 + Math.Sin(_clock * 30 + i) * 2;
                dc.FillEllipse(fx, m.Y, r, i % 2 == 0 ? 0xFFFF8030 : 0xFFFFD050);
            }
            return;
        }
        double p = 4.5 + Math.Sin(_clock * 20 + m.X) * 1.2;
        dc.FillEllipse(m.X, m.Y, p + 2, 0x60FF6080);
        dc.FillEllipse(m.X, m.Y, p, 0xFFFF7A9A);
        dc.FillEllipse(m.X, m.Y, 2, 0xFFFFFFFF);
    }

    /// <summary>What a porter dropped: a crystal in its colour, or a lettered pod.</summary>
    private void DrawItem(DrawingContext dc, Item it)
    {
        double glow = 0.5 + 0.5 * Math.Sin(_clock * 8);
        switch (it.Loot)
        {
            case Loot.RedCrystal or Loot.BlueCrystal or Loot.YellowCrystal:
            {
                uint c = it.Loot switch { Loot.RedCrystal => 0xFFFF4030, Loot.BlueCrystal => 0xFF4090FF, _ => 0xFFFFE030 };
                dc.FillEllipse(it.X, it.Y, 20, Alpha(c, 0.25 + glow * 0.3));
                Poly(dc, c, 0xFFFFFFFF, 1.5, new Point(it.X, it.Y - 14), new Point(it.X + 10, it.Y), new Point(it.X, it.Y + 14), new Point(it.X - 10, it.Y));
                break;
            }
            default:
            {
                string letter = it.Loot switch { Loot.Speed => "S", Loot.Bit => "B", _ => "M" };
                dc.FillEllipse(it.X, it.Y, 18, Alpha(0xFF60FF80, 0.25 + glow * 0.3));
                dc.DrawEllipse(Brush(0xFF2A5A3A), Pen(0xFF80FFA0, 2), new Point(it.X, it.Y), 13, 13);
                Text(dc, letter, new Point(it.X, it.Y), 24, 0xFFFFFFFF, center: true, shadow: false);
                break;
            }
        }
    }

    // ───────────────────────────── the Swarm ─────────────────────────────

    private void DrawEnemy(DrawingContext dc, Enemy e, World w)
    {
        double x = e.X, y = e.Y;
        bool flash = e.Hurt > 0 && (int)(_clock * 30) % 2 == 0;
        uint W(uint c) => flash ? 0xFFFFFFFF : c;
        double flip = e.Roof ? -1 : 1;
        switch (e.Kind)
        {
            case Foe.Flitter:
            {
                double wing = Math.Sin(e.T * 14) * 6;
                Poly(dc, W(0xFF8A5AC0), 0xFF2A1840, 1, new Point(x - 14, y), new Point(x + 2, y - 10 - wing), new Point(x + 12, y - 4), new Point(x + 12, y + 4), new Point(x + 2, y + 10 + wing));
                dc.FillEllipse(x - 4, y, 4, W(0xFFFF6080));
                break;
            }
            case Foe.Gyro:
                using (dc.PushTransform(Matrix.CreateRotation(e.T * 7) * Matrix.CreateTranslation(x, y)))
                {
                    dc.DrawEllipse(null, Pen(W(0xFFC0C8D8), 5), new Point(0, 0), 13, 13);
                    for (int k = 0; k < 4; k++) dc.FillRectangle(Brush(W(0xFF8A94A8)), new Rect(-2 + Math.Cos(k * Math.PI / 2) * 13 - 2, Math.Sin(k * Math.PI / 2) * 13 - 4, 8, 8));
                }
                dc.FillEllipse(x, y, 5, W(0xFFFF5050));
                break;
            case Foe.Porter:
            {
                double leg = Math.Sin(e.T * 4) * 5;
                dc.DrawRectangle(Brush(W(0xFF7A8A6A)), Pen(0xFF2A3220, 2), new Rect(x - 26, y - 18, 52, 32), 6, 6);
                dc.FillRectangle(Brush(0xFF3A4430), new Rect(x - 18, y - 10, 36, 14), 3);
                Text(dc, e.Loot switch { Loot.Speed => "S", Loot.Bit => "B", Loot.Missile => "M", _ => "*" }, new Point(x, y - 3), 18, 0xFFFFE060, center: true, shadow: false);
                dc.DrawLine(Pen(W(0xFF5A6A4A), 4), new Point(x - 14, y + 14), new Point(x - 18 + leg, y + 24));
                dc.DrawLine(Pen(W(0xFF5A6A4A), 4), new Point(x + 14, y + 14), new Point(x + 18 - leg, y + 24));
                break;
            }
            case Foe.Hatch:
            {
                double open = e.FireIn < 0.5 ? 1 - e.FireIn / 0.5 : 0.15;
                double baseY = y + 18 * flip;
                dc.DrawEllipse(Brush(W(0xFF6A5A4A)), Pen(0xFF2A2018, 2), new Point(x, baseY), 32, 22);
                dc.DrawEllipse(Brush(0xFF100808), null, new Point(x, baseY - 8 * flip), 18, 8 * open + 1);
                break;
            }
            case Foe.Strider:
            {
                double sw = Math.Sin(e.T * 7) * 8;
                dc.DrawLine(Pen(W(0xFFA8A090), 3), new Point(x - 4, y), new Point(x - 12 + sw, y + 22 * flip));
                dc.DrawLine(Pen(W(0xFFA8A090), 3), new Point(x + 4, y), new Point(x + 12 - sw, y + 22 * flip));
                dc.DrawEllipse(Brush(W(0xFF8A7A6A)), Pen(0xFF3A3028, 1.5), new Point(x, y - 6 * flip), 14, 10);
                dc.FillEllipse(x + 6, y - 6 * flip, 3, 0xFFFF4030);
                break;
            }
            case Foe.Gun:
            case Foe.Battery:
            {
                bool big = e.Kind == Foe.Battery;
                double size = big ? 1.6 : 1;
                double gx = w.Ship.X - x, gy = w.Ship.Y - y, d = Math.Max(1, Math.Sqrt(gx * gx + gy * gy));
                dc.DrawLine(Pen(W(0xFF2A3040), 6 * size), new Point(x, y), new Point(x + gx / d * 22 * size, y + gy / d * 22 * size));
                if (big) dc.DrawLine(Pen(W(0xFF2A3040), 6 * size), new Point(x, y + 8 * flip), new Point(x + gx / d * 18 * size, y + 8 * flip + gy / d * 18 * size));
                dc.DrawEllipse(Brush(W(big ? 0xFF7A8698 : 0xFF8A7A9A)), Pen(0xFF2A2030, 1.5), new Point(x, y + 2 * flip), 14 * size, 12 * size);
                dc.FillRectangle(Brush(W(0xFF4A5468)), new Rect(x - 18 * size, e.Roof ? y - 14 * size : y + 8 * size, 36 * size, 6 * size), 2);
                dc.FillEllipse(x, y, 3.5 * size, 0xFFFFD040);
                break;
            }
            case Foe.Pod:
            {
                double open = e.FireIn < 0.5 ? 1 - e.FireIn / 0.5 : 0.1;
                dc.DrawEllipse(Brush(W(0xFFC0508A)), Pen(0xFF5A1A38, 2), new Point(x, y + 4 * flip), 22, 16);
                dc.DrawEllipse(Brush(0xFF2A0812), null, new Point(x, y - 8 * flip), 9, 3 + 6 * open);
                break;
            }
            case Foe.Embryo:
                dc.FillEllipse(x, y, 9, W(0xFFD0A070));
                dc.FillEllipse(x - 3, y - 2, 3, 0xFF201010);
                dc.DrawLine(Pen(W(0xFFD0A070), 2), new Point(x + 6, y), new Point(x + 14, y + Math.Sin(e.T * 10) * 5));
                break;
            case Foe.Mother: DrawMother(dc, e, W); break;
            case Foe.Serpent: DrawSerpent(dc, e, W); break;
            case Foe.Engine: DrawEngine(dc, e, W); break;
            case Foe.Overmind: DrawOvermind(dc, e, W); break;
        }
    }

    private void DrawMother(DrawingContext dc, Enemy e, Func<uint, uint> W)
    {
        double x = e.X, y = e.Y;
        // The tail, swinging.
        var tail = Sim.Tail(e).ToList();
        for (int i = tail.Count - 1; i >= 0; i--)
        {
            var (tx, ty) = tail[i];
            dc.DrawEllipse(Brush(i % 2 == 0 ? 0xFF8A7A5A : 0xFF7A6A4A), Pen(0xFF3A3020, 2), new Point(tx, ty), 16 - i * 0.5, 15 - i * 0.5);
        }
        // The body: ribs of bone over dark flesh.
        dc.DrawEllipse(Brush(0xFF3A2030), Pen(0xFF1A0810, 3), new Point(x + 20, y - 20), 110, 180);
        for (int k = 0; k < 7; k++)
        {
            double ry = y - 150 + k * 44;
            dc.DrawLine(Pen(0xFFD8C8A0, 9), new Point(x - 70, ry + 20), new Point(x + 90, ry));
        }
        // The head, and its mouth.
        dc.DrawEllipse(Brush(0xFF8A7A5A), Pen(0xFF3A3020, 3), new Point(x - 70, y - 90), 50, 40);
        dc.FillEllipse(x - 90, y - 98, 6, 0xFFFF3020);
        dc.DrawEllipse(Brush(0xFF100508), null, new Point(x - 100, y - 70), 20, 8 + 4 * Math.Sin(e.T * 4));
        // The core, pulsing in its belly.
        var (cx, cy) = Sim.MotherCore(e);
        double p = 1 + 0.12 * Math.Sin(_clock * 6);
        dc.FillEllipse(cx, cy, 30 * p, 0x60FF4060);
        dc.DrawEllipse(Brush(W(0xFFE04070)), Pen(0xFF5A0A20, 2), new Point(cx, cy), 22 * p, 22 * p);
        dc.FillEllipse(cx - 5, cy - 5, 6, 0xFFFFC0D0);
    }

    private void DrawSerpent(DrawingContext dc, Enemy e, Func<uint, uint> W)
    {
        var body = Sim.Body(e).ToList();
        for (int i = body.Count - 1; i >= 0; i--)
        {
            var (bx, by) = body[i];
            dc.DrawEllipse(Brush(i % 2 == 0 ? 0xFF6A8A5A : 0xFF5A7A4A), Pen(0xFF203018, 2), new Point(bx, by), 20, 20);
            dc.FillEllipse(bx, by, 6, 0xFFB0C080);
        }
        double x = e.X, y = e.Y;
        dc.DrawEllipse(Brush(W(0xFF8AA05A)), Pen(0xFF203018, 3), new Point(x, y), 28, 24);
        dc.FillEllipse(x - 10, y - 8, 6, 0xFFFFE040);
        dc.FillEllipse(x - 10, y + 8, 6, 0xFFFFE040);
        Poly(dc, 0xFFE0E0C0, null, 0, new Point(x - 26, y - 6), new Point(x - 40, y - 2), new Point(x - 26, y + 2));
    }

    private void DrawEngine(DrawingContext dc, Enemy e, Func<uint, uint> W)
    {
        double x = e.X, y = e.Y;
        dc.DrawRectangle(Brush(0xFF4A525E), Pen(0xFF1A1E24, 3), new Rect(x - 20, y - 64, 140, 128), 10, 10);
        for (int k = 0; k < 4; k++) dc.FillRectangle(Brush(0xFF2A3038), new Rect(x - 10 + k * 30, y - 58, 6, 116));
        // The nozzle, glowing.
        double p = 0.7 + 0.3 * Math.Sin(_clock * 10 + e.Index);
        dc.DrawEllipse(Brush(0xFF2A2A30), Pen(0xFF8A929E, 3), new Point(x - 20, y), 32, 36);
        dc.FillEllipse(x - 20, y, 24, W(Alpha(0xFFFF8020, p)));
        dc.FillEllipse(x - 20, y, 12, W(0xFFFFF0A0));
    }

    private void DrawOvermind(DrawingContext dc, Enemy e, Func<uint, uint> W)
    {
        double x = e.X, y = e.Y;
        bool open = Sim.Open(e);
        double opening = open ? Math.Min(1, (e.T % 6 - 3) * 3) : Math.Max(0, 1 - (e.T % 6) * 3);
        dc.FillEllipse(x + 20, y, 140, 0x30FF2040);
        // The shell, in two halves that part.
        double gap = 50 * opening;
        Poly(dc, 0xFF5A3040, 0xFF200810, 3, new Point(x - 70, y - gap), new Point(x - 40, y - 110 - gap), new Point(x + 100, y - 110 - gap), new Point(x + 110, y - gap));
        Poly(dc, 0xFF5A3040, 0xFF200810, 3, new Point(x - 70, y + gap), new Point(x - 40, y + 110 + gap), new Point(x + 100, y + 110 + gap), new Point(x + 110, y + gap));
        // The brain within.
        dc.DrawEllipse(Brush(W(0xFFE07A9A)), Pen(0xFF5A1A38, 2), new Point(x - 30, y), 44, 40 * Math.Max(0.15, opening));
        for (int k = 0; k < 4; k++)
            dc.DrawLine(Pen(0xFF9A3A5A, 2.5), new Point(x - 64 + k * 6, y - 22 + k * 14), new Point(x + 4 - k * 8, y - 18 + k * 13 + Math.Sin(_clock * 2 + k) * 3));
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double a = 1 - e.Age / e.Life;
        switch (e.Kind)
        {
            case EffectKind.Score:
                Text(dc, e.Text, new Point(e.X, e.Y - e.Age * 40), 22, Alpha(e.Colour, a), center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(e.X, e.Y - e.Age * 30), 26, Alpha(e.Colour, Math.Min(1, a * 2)), center: true);
                break;
            case EffectKind.Burst:
            {
                int n = int.TryParse(e.Text, out int v) ? v : 10;
                dc.FillEllipse(e.X, e.Y, 18 * (1 - a) + 4, Alpha(0xFFFFFFFF, a * 0.6));
                for (int i = 0; i < n; i++)
                {
                    double ang = i * Math.PI * 2 / n + i, r = 6 + e.Age * 140;
                    dc.FillEllipse(e.X + Math.Cos(ang) * r, e.Y + Math.Sin(ang) * r, 3.5 * a + 1, Alpha(e.Colour, a));
                }
                break;
            }
            case EffectKind.Blast:
            {
                double r = 10 + e.Age * 120;
                dc.FillEllipse(e.X, e.Y, r * 0.6, Alpha(0xFFFFF0C0, a * 0.8));
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, a), 6 * a + 1), new Point(e.X, e.Y), r, r);
                for (int i = 0; i < 14; i++)
                {
                    double ang = i * 0.45 + e.X, rr = r * (0.6 + (i % 3) * 0.25);
                    dc.FillEllipse(e.X + Math.Cos(ang) * rr, e.Y + Math.Sin(ang) * rr, 4 * a + 1, Alpha(0xFFFF8030, a));
                }
                break;
            }
            case EffectKind.Spark:
                dc.FillEllipse(e.X, e.Y, 10 * a + 2, Alpha(e.Colour, a));
                break;
        }
    }

    // ───────────────────────────── around the edges ─────────────────────────────


    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        dc.FillRectangle(Brush(0xFF06060E), new Rect(0, 0, Field.Width, Field.Hud));
        dc.FillRectangle(Brush(0xFF06060E), new Rect(0, Field.Bottom, Field.Width, Field.Height - Field.Bottom));
        dc.DrawLine(Pen(0xFF2A3A6A, 2), new Point(0, Field.Hud), new Point(Field.Width, Field.Hud));
        Text(dc, "1P", new Point(20, 24), 22, Cyan);
        Text(dc, g.Score.ToString("0000000"), new Point(60, 24), 32, White);
        Text(dc, "HI " + Math.Max(g.HighScores.TopScore, g.Score).ToString("0000000"), new Point(Field.Width / 2, 24), 28, Yellow, center: true);
        if (!InGame) return;
        dc.DrawLine(Pen(0xFF2A3A6A, 2), new Point(0, Field.Bottom), new Point(Field.Width, Field.Bottom));
        Text(dc, w.Stage.Name, new Point(Field.Width - 20, 24), 22, 0xFF9AB0E0, right: true);
        for (int i = 0; i < Math.Min(g.Lives - 1, 6); i++)
            using (dc.PushTransform(Matrix.CreateScale(0.6, 0.6) * Matrix.CreateTranslation(30 + i * 30, Field.Bottom + 40)))
                DrawLancet(dc, 0, 0, 0);
        // The beam meter.
        var s = w.Ship;
        double bx = 260, by = Field.Bottom + 30, bw = 480;
        Text(dc, "BEAM", new Point(bx - 16, by + 8), 26, 0xFF80E0FF, right: true);
        dc.DrawRectangle(Brush(0xFF0A1428), Pen(0xFF3A5A8A, 2), new Rect(bx, by, bw, 18), 4, 4);
        double k = Math.Min(1, Math.Max(0, s.Charge) / Ship.FullCharge);
        uint bar = s.ChargeLevel switch { 3 => (int)(_clock * 10) % 2 == 0 ? 0xFFFFFFFF : 0xFF80E0FF, 2 => 0xFF60C0FF, _ => 0xFF3A80D0 };
        if (k > 0) dc.FillRectangle(Brush(bar), new Rect(bx + 2, by + 2, (bw - 4) * k, 14), 3);
        // What the ship carries.
        var o = w.Orb;
        string orb = o.State == OrbState.None ? "NO ORB" : $"ORB {o.Level}  {o.Laser.ToString().ToUpperInvariant()}";
        Text(dc, orb, new Point(bx + bw + 20, by + 2), 22, o.State == OrbState.None ? 0xFF5A6A8A : o.Laser switch { Laser.Red => 0xFFFF7050, Laser.Blue => 0xFF60B0FF, _ => 0xFFFFE050 });
        Text(dc, $"SPEED {s.Speed + 1}  BITS {s.Bits}{(s.Missile ? "  MISSILES" : "")}", new Point(bx + bw + 20, by + 28), 20, 0xFFB0C8FF);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = Field.Top + 230;
        dc.FillRectangle(Brush(0xA0000000), new Rect(cx - 400, cy - 52, 800, 108), 10);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 18 ? 48 : 60, 0xFFFFC040, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    // ───────────────────────────── title and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        DrawSpace(dc, _clock * 120, 0, Themes[0]);
        // The Lancet with its orb and bits, weaving across.
        double sx = (_clock * 160) % 1300 - 120, sy = 625 + Math.Sin(_clock * 1.6) * 25;
        DrawOrb(dc, sx + 34, sy, 3, Laser.Red, OrbState.Front);
        DrawBit(dc, sx - 4, sy - 36);
        DrawBit(dc, sx - 4, sy + 36);
        DrawLancet(dc, sx, sy, Math.Cos(_clock * 1.6) * 0.6);
        string title = "AVA R-TYPE";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 32 + 32 + i * 64;
            double ty = 160 + Math.Sin(_clock * 2.4 + i * 0.5) * 4;
            Text(dc, title[i].ToString(), new Point(tx + 3, ty + 3), 120, 0xFF401010, center: true, shadow: false);
            Text(dc, title[i].ToString(), new Point(tx, ty), 120, i < 3 ? Cyan : (i % 2 == 0 ? 0xFFFF6A30 : 0xFFFFB040), center: true, shadow: false);
        }
        Text(dc, "AFTER IREM'S 1987 ARCADE GAME", new Point(cx, 236), 24, Dim, center: true);
        Text(dc, "THE SWARM, A LIVING EMPIRE OF BONE AND METAL, IS SPREADING THROUGH THE STARS.", new Point(cx, 300), 26, White, center: true);
        Text(dc, "FLY THE LANCET INTO ITS DOMAIN AND STRIKE AT ITS HEART.", new Point(cx, 330), 26, White, center: true);
        Text(dc, "SHOOT THE PORTERS FOR CRYSTALS: THE FIRST BRINGS THE ORB, WHICH SHIELDS YOU, BITES,", new Point(cx, 378), 22, 0xFFFFA070, center: true);
        Text(dc, "AND FLIES OFF ON ITS OWN WITH Z. HOLD FIRE TO CHARGE THE BEAM; LET GO TO LOOSE IT.", new Point(cx, 404), 22, 0xFFFFA070, center: true);
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER TO LAUNCH", new Point(cx, 476), 44, Yellow, center: true);
        Text(dc, "ARROWS: FLY    X OR CTRL: FIRE (HOLD TO CHARGE)    Z OR SPACE: THE ORB    P: PAUSE    M: SOUND", new Point(cx, 530), 20, 0xFFB0A0D0, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        DrawSpace(dc, _clock * 60, 3, Themes[3]);
        Text(dc, "THE ACE PILOTS", new Point(cx, 120), 64, 0xFFFF7050, center: true);
        Text(dc, "RANK NAME     SCORE  STAGES", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,9}   {e.Stage,4}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        DrawSpace(dc, _clock * 60, 3, Themes[3]);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
}
