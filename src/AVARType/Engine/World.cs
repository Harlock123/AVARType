namespace AVARType.Engine;

/// <summary>A small random number generator whose state can be copied (so a copy of the world plays out the same).</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
    public double Between(double a, double b) => a + Next() * (b - a);
}

/// <summary>The colour of the orb's crystal, which decides the laser it fires.</summary>
public enum Laser { Red, Blue, Yellow }

/// <summary>Where the orb is: not yet won, fixed to the ship's nose or tail, out on its own, or coming back.</summary>
public enum OrbState { None, Front, Back, Free, Recall }

/// <summary>The Lancet: where it is, how fast, its bits and missiles, and the charge in its beam.</summary>
public sealed class Ship
{
    public const double HalfWidth = 18, HalfHeight = 8, FullCharge = 1.6;
    public double X, Y, FireCool, MissileCool, Charge, Held, DeadTime, Safe, Bank;
    public int Speed, Bits;
    public bool Missile, Dead, FirePrev, OrbPrev;
    public double Speed0 => 160 + Speed * 55;
    /// <summary>The beam's strength from its charge: 0 none, 1 to 3.</summary>
    public int ChargeLevel => Charge < 0.35 ? 0 : Charge < 0.9 ? 1 : Charge < FullCharge ? 2 : 3;
    public Ship Copy() => (Ship)MemberwiseClone();
}

/// <summary>The orb: the indestructible pod that guards the ship, chews up what it touches, soaks up shots, and fires the laser.</summary>
public sealed class Orb
{
    public const double Radius = 16;
    public OrbState State;
    public int Level = 1;
    public Laser Laser;
    public double X, Y, Vx, Vy, FireCool, ChewCool;
    public Orb Copy() => (Orb)MemberwiseClone();
}

public enum ShotKind { Pea, Ring, Reflect, Crawl, Pellet, Beam, Missile }

/// <summary>Something the ship (or the orb, or a bit) fired.</summary>
public sealed class Shot
{
    public double X, Y, Vx, Vy, Length, Height, Damage = 1, Life = 3;
    public ShotKind Kind;
    /// <summary>Crawling along the ground (or the roof, with Up).</summary>
    public bool Running, Up, Dead;
    public int Bounces;
    /// <summary>A beam goes on through, hurting each thing once.</summary>
    public HashSet<int>? Struck;
    public Shot Copy()
    {
        var s = (Shot)MemberwiseClone();
        if (Struck != null) s.Struck = [.. Struck];
        return s;
    }
}

public sealed class Enemy
{
    public Foe Kind;
    public int Id, Hp = 1, Pattern, State, Index;
    public double X, Y, Vx, Vy, T, FireIn, Hurt, HomeY, ChewCool;
    public Loot Loot;
    public bool Roof, Boss, Dead, Entered;
    /// <summary>A serpent's body: where its head has been.</summary>
    public (double X, double Y)[] Trail = [];
    public Enemy Copy()
    {
        var e = (Enemy)MemberwiseClone();
        if (Trail.Length > 0) e.Trail = ((double, double)[])Trail.Clone();
        return e;
    }

    /// <summary>Half its width and height, for hits.</summary>
    public (double W, double H) Size => Kind switch
    {
        Foe.Flitter => (14, 11),
        Foe.Gyro => (14, 14),
        Foe.Hatch => (30, 18),
        Foe.Strider => (16, 22),
        Foe.Gun => (16, 14),
        Foe.Porter => (26, 20),
        Foe.Battery => (28, 20),
        Foe.Pod => (20, 16),
        Foe.Embryo => (9, 9),
        Foe.Mother => (40, 40),
        Foe.Serpent => (24, 24),
        Foe.Engine => (26, 34),
        _ => (60, 60),
    };
}

/// <summary>Something fired at the ship: a pellet, or (with a length) a flame or a beam.</summary>
public sealed class Missile
{
    public double X, Y, Vx, Vy, Length;
    public bool Dead;
    public Missile Copy() => (Missile)MemberwiseClone();
}

/// <summary>What a porter dropped, drifting, to be picked up.</summary>
public sealed class Item
{
    public double X, Y, T;
    public Loot Loot;
    public bool Dead;
    public Item Copy() => (Item)MemberwiseClone();
}

public enum EffectKind { Score, Burst, Word, Spark, Blast }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}

/// <summary>
/// Everything in a stage that moves or changes, kept together so it can be copied whole: the computer pilot
/// tries its moves out on a copy before making them.
/// </summary>
public sealed class World
{
    public StageDef Stage = StageBook.All[0];
    public int StageIndex, Round = 1;
    public Ship Ship = new();
    public Orb Orb = new();
    public List<Shot> Shots = [];
    public List<Enemy> Enemies = [];
    public List<Missile> Missiles = [];
    public List<Item> Items = [];
    public List<Effect> Effects = [];
    /// <summary>The squares as they are now: armour shot away, tissue burst (and growing back).</summary>
    public Tile[,] Grid = new Tile[0, 0];
    public List<(int C, int R, double T)> Regrow = [];
    public double CamX, Clock, BossTime;
    public int Score, NextSpawn, NextId;
    /// <summary>The stage has stopped for the fight at the end; the boss is here; it's beaten (or gone); the stage is over.</summary>
    public bool BossLock, BossUp, BossDown, Done, BossBeaten;
    public Dice Dice;
    /// <summary>Sounds asked for this step (the game plays them; a copy's are thrown away).</summary>
    public List<Sfx> Heard = [];
    public string Cause = "";

    public double Speed => Field.Scroll * (Round > 1 ? 1.15 : 1);

    public World Copy() => new()
    {
        Stage = Stage, StageIndex = StageIndex, Round = Round, Ship = Ship.Copy(), Orb = Orb.Copy(),
        Shots = Shots.Select(s => s.Copy()).ToList(),
        Enemies = Enemies.Select(e => e.Copy()).ToList(),
        Missiles = Missiles.Select(m => m.Copy()).ToList(),
        Items = Items.Select(c => c.Copy()).ToList(),
        Grid = (Tile[,])Grid.Clone(), Regrow = [.. Regrow],
        CamX = CamX, Clock = Clock, BossTime = BossTime,
        Score = Score, NextSpawn = NextSpawn, NextId = NextId,
        BossLock = BossLock, BossUp = BossUp, BossDown = BossDown, Done = Done, BossBeaten = BossBeaten, Dice = Dice, Cause = Cause,
    };

    public Tile At(int c, int r) => r < 0 || r >= Field.Rows || c < 0 || c >= Stage.Cols ? Tile.Empty : Grid[c, r];
    public Tile AtPoint(double x, double y) => At((int)Math.Floor(x / Field.Tile), (int)Math.Floor((y - Field.Top) / Field.Tile));
}
