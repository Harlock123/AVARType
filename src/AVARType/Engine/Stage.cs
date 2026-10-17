namespace AVARType.Engine;

/// <summary>Up, down, left and right (up the screen is -1), the orb button, and fire (tap to shoot, hold to charge the beam).</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Orb = false, bool Fire = false);

/// <summary>The screen: the score along the top, the stage 32 squares wide and 20 high, and the beam meter below.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768, Hud = 48;
    public const double Tile = 32;
    public const int Rows = 20, ViewCols = 32;
    public const double Top = Hud, Bottom = Top + Rows * Tile;
    /// <summary>How fast the stage goes by, in pixels a second.</summary>
    public const double Scroll = 72;
}

/// <summary>What a square of a stage is.</summary>
public enum Tile : byte
{
    Empty,
    /// <summary>Hull, bone or rock: touching it is the end of the ship.</summary>
    Rock,
    /// <summary>Armour plate: as deadly, but shots break it.</summary>
    Crystal,
    /// <summary>Living tissue: shots burst it, and it grows back.</summary>
    Cell,
}

public enum Foe
{
    /// <summary>Fliers that come in lines, weaving.</summary>
    Flitter,
    /// <summary>Spinning rings that wheel in on a curve.</summary>
    Gyro,
    /// <summary>A hatch on the ground or the roof that lets out fliers.</summary>
    Hatch,
    /// <summary>A walker on long legs that strides along the ground (or the roof) and fires.</summary>
    Strider,
    Gun,
    /// <summary>A lumbering carrier with something in its hold: shoot it down for what it carries.</summary>
    Porter,
    /// <summary>A heavy gun on a warship's hull: it takes a lot of shots.</summary>
    Battery,
    /// <summary>A growth in a living wall that spits.</summary>
    Pod,
    /// <summary>A small thing that hunts the ship, spawned by the bosses.</summary>
    Embryo,
    // The ends of the stages.
    Mother, Serpent, Engine, Overmind,
}

/// <summary>What a porter carries.</summary>
public enum Loot { None, RedCrystal, BlueCrystal, YellowCrystal, Speed, Bit, Missile }

/// <summary>
/// Something placed in a stage, coming to life when the stage reaches it: a line of fliers (with how they fly), a
/// porter with its load, or something on the ground or the roof.
/// </summary>
public readonly record struct Spawn(int Col, Foe Kind, double Row = 10, int Count = 1, int Pattern = 0, Loot Loot = Loot.None, bool Roof = false);

/// <summary>
/// A stage: its squares, what comes at the ship and where, its volcanoes, where a lost ship starts again, and what
/// waits at the end.
/// </summary>
public sealed class StageDef
{
    public string Name = "";
    public int Theme, Cols;
    /// <summary>The squares, [column, row].</summary>
    public Tile[,] Grid = new Tile[0, 0];
    /// <summary>How deep the rock of the ground and of the roof is at each column, in squares.</summary>
    public int[] Floor = [], Roof = [];
    public List<Spawn> Spawns = [];
    public List<(int Col, int Row)> Volcanoes = [];
    public int[] Checkpoints = [0];
    /// <summary>The column at the screen's right edge when the stage stops for the fight at the end.</summary>
    public int BossCol;
    public Foe Boss;

    public Tile At(int c, int r) => r < 0 || r >= Field.Rows || c < 0 || c >= Cols ? Tile.Empty : Grid[c, r];

    /// <summary>The top of the ground at a column, in screen pixels.</summary>
    public double FloorY(int c) => Field.Bottom - Floor[Math.Clamp(c, 0, Cols - 1)] * Field.Tile;
    /// <summary>The underside of the roof at a column, in screen pixels.</summary>
    public double RoofY(int c) => Field.Top + Roof[Math.Clamp(c, 0, Cols - 1)] * Field.Tile;
}

/// <summary>
/// Building a stage: the ground and the roof as heights along it (wandering, flat, peaked), walls of glass or
/// living cells across the way, and what comes where.
/// </summary>
public sealed class StageBuilder
{
    private readonly StageDef _s;
    private readonly List<(int C0, int C1, int R0, int R1, Tile Kind)> _walls = [];
    private Dice _dice;

    public StageBuilder(string name, int theme, int cols, int seed)
    {
        _s = new StageDef { Name = name, Theme = theme, Cols = cols, Floor = new int[cols], Roof = new int[cols] };
        _dice = new Dice(seed);
    }

    /// <summary>Ground and roof at fixed depths from column c0 to c1.</summary>
    public StageBuilder Flat(int c0, int c1, int floor, int roof)
    {
        for (int c = c0; c <= c1; c++) { _s.Floor[c] = floor; _s.Roof[c] = roof; }
        return this;
    }

    /// <summary>Ground and roof wandering up and down, a square at a time, never closing to less than <paramref name="gap"/> rows.</summary>
    public StageBuilder Wander(int c0, int c1, int floorMin, int floorMax, int roofMin, int roofMax, int gap = 8)
    {
        int f = Math.Clamp(_s.Floor[Math.Max(0, c0 - 1)], floorMin, floorMax), r = Math.Clamp(_s.Roof[Math.Max(0, c0 - 1)], roofMin, roofMax);
        for (int c = c0; c <= c1; c++)
        {
            if (c % 2 == 0)
            {
                f = Math.Clamp(f + _dice.Next(3) - 1, floorMin, floorMax);
                r = Math.Clamp(r + _dice.Next(3) - 1, roofMin, roofMax);
                while (Field.Rows - f - r < gap) { if (f > floorMin) f--; else r--; }
            }
            _s.Floor[c] = f;
            _s.Roof[c] = r;
        }
        return this;
    }

    /// <summary>A mountain rising from the ground (a volcano, if it erupts), its peak <paramref name="height"/> squares up.</summary>
    public StageBuilder Peak(int col, int height, int half, bool volcano = false)
    {
        for (int c = col - half; c <= col + half; c++)
            if (c >= 0 && c < _s.Cols)
                _s.Floor[c] = Math.Max(_s.Floor[c], height - Math.Abs(c - col) * height / (half + 1));
        if (volcano) _s.Volcanoes.Add((col, Field.Rows - height));
        return this;
    }

    /// <summary>A crag hanging from the roof.</summary>
    public StageBuilder Crag(int col, int depth, int half)
    {
        for (int c = col - half; c <= col + half; c++)
            if (c >= 0 && c < _s.Cols)
                _s.Roof[c] = Math.Max(_s.Roof[c], depth - Math.Abs(c - col) * depth / (half + 1));
        return this;
    }

    /// <summary>A wall of glass (or cells) across the open way, from column c0 to c1, to be shot through.</summary>
    public StageBuilder Wall(int c0, int c1, Tile kind = Tile.Crystal, int r0 = 0, int r1 = Field.Rows - 1)
    {
        _walls.Add((c0, c1, r0, r1, kind));
        return this;
    }

    /// <summary>A line of fliers coming in at a row: pattern 0 weaves, 1 swoops at the ship, 2 comes from behind.</summary>
    public StageBuilder Line(int col, double row, int count = 5, int pattern = 0)
    {
        _s.Spawns.Add(new Spawn(col, Foe.Flitter, row, count, pattern));
        return this;
    }

    /// <summary>Lines of fliers (and wheels of gyros) every few columns, at different heights and ways.</summary>
    public StageBuilder Lines(int c0, int c1, int every, int seed)
    {
        var d = new Dice(seed);
        int n = 0;
        for (int c = c0; c <= c1; c += every, n++)
        {
            if (n % 3 == 2) { _s.Spawns.Add(new Spawn(c, Foe.Gyro, 4 + d.Next(12), 4, d.Next(2))); continue; }
            int pattern = d.Next(3);
            double row = pattern == 2 ? (n % 2 == 0 ? 4 : 15) : 5 + d.Next(10);
            Line(c, row, 4 + d.Next(3), pattern);
        }
        return this;
    }

    /// <summary>A porter at a row, carrying something.</summary>
    public StageBuilder Porter(int col, double row, Loot loot)
    {
        _s.Spawns.Add(new Spawn(col, Foe.Porter, row, 1, 0, loot));
        return this;
    }

    /// <summary>Something on the ground (or, with <paramref name="roof"/>, hanging from the roof) at a column.</summary>
    public StageBuilder On(int col, Foe kind, bool roof = false)
    {
        _s.Spawns.Add(new Spawn(col, kind, 0, 1, 0, Loot.None, roof));
        return this;
    }

    public StageDef End(Foe boss, params int[] checkpoints)
    {
        _s.Boss = boss;
        _s.BossCol = _s.Cols;
        _s.Checkpoints = checkpoints;
        _s.Grid = new Tile[_s.Cols, Field.Rows];
        for (int c = 0; c < _s.Cols; c++)
        for (int r = 0; r < Field.Rows; r++)
            _s.Grid[c, r] = r < _s.Roof[c] || r >= Field.Rows - _s.Floor[c] ? Tile.Rock : Tile.Empty;
        foreach (var (c0, c1, r0, r1, kind) in _walls)
            for (int c = c0; c <= c1; c++)
            for (int r = r0; r <= r1; r++)
                if (_s.Grid[c, r] == Tile.Empty) _s.Grid[c, r] = kind;
        _s.Spawns.Sort((a, b) => a.Col.CompareTo(b.Col));
        return _s;
    }
}

/// <summary>The four stages: the outer wall of the Swarm's domain, its spawning caves, a dreadnought of its fleet, and its heart.</summary>
public static class StageBook
{
    public static readonly StageDef[] All = [OuterWall(), Caves(), Dreadnought(), Heart()];

    /// <summary>Open space, then bone-and-metal structures, striders and guns, and the Hollow Mother.</summary>
    private static StageDef OuterWall() =>
        new StageBuilder("THE OUTER WALL", 0, 236, 11)
            .Flat(0, 22, 0, 0)
            .Wander(23, 70, 1, 4, 0, 2)
            .Crag(48, 6, 3).Peak(62, 6, 3)
            .Wander(71, 130, 2, 5, 2, 5)
            .Wall(96, 97, Tile.Crystal, 6, 13)
            .Flat(131, 160, 3, 3)
            .Peak(140, 8, 3).Crag(152, 8, 3)
            .Wander(161, 200, 1, 4, 1, 4)
            .Flat(201, 235, 1, 1)
            .Lines(6, 60, 7, 1)
            .Porter(14, 9, Loot.RedCrystal).Porter(40, 6, Loot.Speed).Porter(70, 11, Loot.Bit)
            .On(30, Foe.Gun).On(38, Foe.Strider).On(52, Foe.Gun, roof: true).On(58, Foe.Hatch)
            .Lines(72, 128, 9, 2)
            .On(76, Foe.Strider).On(84, Foe.Gun, roof: true).On(90, Foe.Hatch, roof: true).On(104, Foe.Strider)
            .On(110, Foe.Gun).On(118, Foe.Strider, roof: true).On(124, Foe.Gun, roof: true)
            .Porter(100, 9, Loot.BlueCrystal).Porter(134, 8, Loot.Missile)
            .Lines(136, 196, 8, 3)
            .On(146, Foe.Gun).On(158, Foe.Hatch).On(170, Foe.Strider).On(182, Foe.Gun, roof: true).On(190, Foe.Strider)
            .Porter(176, 10, Loot.Speed)
            .End(Foe.Mother, 0, 72, 132, 202);

    /// <summary>Living tunnels: tissue that grows back, pods spitting, and the Serpent coiling in its lair.</summary>
    private static StageDef Caves() =>
        new StageBuilder("THE SPAWNING CAVES", 1, 236, 23)
            .Flat(0, 14, 2, 2)
            .Wander(15, 66, 2, 6, 2, 6, 8)
            .Wall(28, 29, Tile.Cell).Wall(46, 46, Tile.Cell).Wall(60, 61, Tile.Cell)
            .Flat(67, 84, 4, 4)
            .Wall(74, 76, Tile.Cell, 4, 9)
            .Wander(85, 150, 2, 6, 2, 6, 8)
            .Wall(98, 99, Tile.Cell).Wall(118, 118, Tile.Cell).Wall(136, 137, Tile.Cell)
            .Flat(151, 170, 3, 3)
            .Wander(171, 198, 2, 5, 2, 5, 9)
            .Wall(184, 185, Tile.Cell)
            .Flat(199, 235, 1, 1)
            .Lines(4, 22, 7, 7)
            .Porter(10, 10, Loot.RedCrystal).Porter(38, 9, Loot.Bit).Porter(88, 10, Loot.YellowCrystal)
            .On(34, Foe.Pod).On(40, Foe.Pod, roof: true).On(54, Foe.Pod)
            .Lines(68, 90, 8, 8)
            .On(92, Foe.Pod, roof: true).On(104, Foe.Hatch).On(110, Foe.Pod).On(124, Foe.Pod, roof: true)
            .On(144, Foe.Hatch, roof: true).Porter(130, 9, Loot.Speed)
            .Lines(152, 170, 7, 9)
            .On(176, Foe.Pod).On(180, Foe.Pod, roof: true).On(192, Foe.Strider).Porter(160, 9, Loot.Missile)
            .End(Foe.Serpent, 0, 68, 152, 200);

    /// <summary>Along the hull of a warship of the Swarm: batteries all over it, armour to shoot through, and its engines.</summary>
    private static StageDef Dreadnought() =>
        new StageBuilder("THE DREADNOUGHT", 2, 230, 41)
            .Flat(0, 16, 0, 0)
            .Flat(17, 200, 6, 0)
            .Crag(40, 5, 2).Crag(84, 5, 2).Crag(128, 6, 2).Crag(172, 5, 2)
            .Peak(60, 10, 2).Peak(106, 10, 2).Peak(150, 11, 2)
            .Wall(60, 61, Tile.Crystal, 4, 9).Wall(106, 107, Tile.Crystal, 4, 9).Wall(150, 151, Tile.Crystal, 2, 8)
            .Flat(201, 229, 3, 1)
            .Lines(4, 16, 6, 10)
            .Porter(8, 8, Loot.RedCrystal).Porter(30, 6, Loot.Speed)
            .On(22, Foe.Battery).On(32, Foe.Battery).On(40, Foe.Gun, roof: true).On(46, Foe.Gun).On(128, Foe.Gun, roof: true).On(52, Foe.Battery).On(70, Foe.Battery).On(78, Foe.Gun)
            .On(92, Foe.Battery).On(98, Foe.Hatch).On(116, Foe.Battery).On(124, Foe.Gun).On(138, Foe.Battery)
            .On(160, Foe.Battery).On(166, Foe.Hatch).On(180, Foe.Battery).On(190, Foe.Battery)
            .Lines(26, 196, 12, 11)
            .Porter(66, 5, Loot.Bit).Porter(112, 5, Loot.BlueCrystal).Porter(158, 4, Loot.Missile)
            .End(Foe.Engine, 0, 64, 112, 196);

    /// <summary>The heart of the Swarm: living walls closing in, and the Overmind in its shell.</summary>
    private static StageDef Heart() =>
        new StageBuilder("THE HEART", 3, 224, 51)
            .Flat(0, 12, 2, 2)
            .Wander(13, 60, 3, 6, 3, 6, 7)
            .Wall(26, 26, Tile.Cell).Wall(44, 45, Tile.Cell)
            .Flat(61, 80, 4, 4)
            .Peak(70, 8, 3).Crag(76, 8, 3)
            .Wander(81, 150, 3, 6, 3, 6, 7)
            .Wall(92, 93, Tile.Cell).Wall(112, 112, Tile.Cell).Wall(132, 133, Tile.Cell)
            .Flat(151, 188, 3, 3)
            .Wall(166, 167, Tile.Cell)
            .Flat(189, 223, 2, 2)
            .Lines(3, 11, 5, 12)
            .Porter(8, 9, Loot.RedCrystal).Porter(36, 9, Loot.Bit)
            .On(16, Foe.Pod).On(20, Foe.Pod, roof: true).On(30, Foe.Gun).On(34, Foe.Pod, roof: true)
            .On(38, Foe.Hatch).On(52, Foe.Strider).On(56, Foe.Pod)
            .Lines(62, 78, 6, 13).Porter(66, 9, Loot.Speed)
            .On(86, Foe.Hatch, roof: true).On(96, Foe.Pod).On(104, Foe.Pod, roof: true).On(120, Foe.Strider).On(126, Foe.Gun)
            .Lines(140, 186, 8, 14).Porter(144, 9, Loot.YellowCrystal).Porter(176, 8, Loot.Missile)
            .On(196, Foe.Pod).On(200, Foe.Pod, roof: true).On(210, Foe.Gun).On(214, Foe.Gun, roof: true)
            .End(Foe.Overmind, 0, 61, 151, 189);
}
