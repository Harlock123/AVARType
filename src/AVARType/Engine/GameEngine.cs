namespace AVARType.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

/// <summary>How a stage is going.</summary>
public enum Phase
{
    /// <summary>The stage named; the Lancet waiting to go.</summary>
    Start,
    Play,
    /// <summary>Shot down: a moment, then back to the last checkpoint (without the orb or anything else), or the game is over.</summary>
    Dying,
    /// <summary>The boss beaten (or gone): a moment, then the next stage.</summary>
    Clear,
    /// <summary>The Overmind destroyed: the Swarm broken, then round again.</summary>
    Ending,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Pea, Laser, Charge, Beam, MissileOut, OrbLaunch, OrbRecall, OrbAttach, Chew, Boom, BigBoom, Hit, Clink, Crack, PowerUp,
    EnemyFire, Flame, Spit, Die, ExtraLife, BossCome, BossHit, BossDie, BossLeave,
    SayGo, SayWarning, SayClear, SayVictory,
    Music, BossMusic, MusicStop, StageClear, Victory, TitleTune, GameOver,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 50;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 2.5, DyingSeconds = 2.4, ClearSeconds = 4.5, EndingSeconds = 9;
    public const int Lives0 = 3, FirstExtra = 20000, ExtraEvery = 70000;

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true, _music;
    private readonly int _seed;
    private int _tries, _nextExtra, _checkpoint;

    public GameEngine(HighScoreTable? table = null, int seed = 1)
    {
        HighScores = table ?? HighScoreTable.Load();
        _seed = seed;
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    public int Lives { get; private set; }
    public int Score => World.Score;
    /// <summary>Stages cleared this game.</summary>
    public int Cleared { get; private set; }
    public string? Message { get; private set; }
    public string? SubMessage { get; private set; }
    public double MessageTime { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    private void Say(string text, string? sub = null, double seconds = 2.5)
    {
        Message = text;
        SubMessage = sub;
        MessageTime = seconds;
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = SubMessage = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || (c.Fire || c.Orb) && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > 4)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Cleared);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        Lives = Lives0;
        Cleared = 0;
        _tries = 0;
        _nextExtra = FirstExtra;
        Begin(0, 1, 0, null);
    }

    /// <summary>A stage begun at a checkpoint, with the arms of <paramref name="carry"/> (none after a loss).</summary>
    private void Begin(int stage, int round, int checkpoint, Ship? carry, Orb? orb = null)
    {
        int score = World.Score;
        Sim.Start(World, stage, round, checkpoint, carry, orb, _seed * 1000 + stage * 31 + _tries++);
        World.Score = score;
        if (checkpoint == 0) _checkpoint = 0;
        SetPhase(Phase.Start);
        Say(World.Stage.Name, round > 1 ? $"ROUND {round}" : stage == 0 && checkpoint == 0 ? "STRIKE AT THE HEART OF THE SWARM" : null, StartSeconds);
    }

    public void StartGame()
    {
        NewGame();
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        NewGame();
        State = GameState.Attract;
        StateTime = 0;
        Pilot.Reset();
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = SubMessage = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play)
        {
            Music(true);
            if (World.BossUp && !World.BossDown) Play(Sfx.BossMusic);
        }
    }

    private void GameOver()
    {
        Music(false);
        Play(Sfx.GameOver);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Play);
                    Music(true);
                    Play(Sfx.SayGo);
                }
                break;
            case Phase.Play:
                int before = w.Score;
                Sim.Step(w, c);
                foreach (int cp in w.Stage.Checkpoints)
                    if (cp * Field.Tile <= w.CamX + 160) _checkpoint = Math.Max(_checkpoint, cp);
                if (w.Score != before) ExtraLives();
                if (w.Ship.Dead) { SetPhase(Phase.Dying); Music(false); }
                else if (w.Done)
                {
                    bool last = w.StageIndex == StageBook.All.Length - 1;
                    SetPhase(last ? Phase.Ending : Phase.Clear);
                    Music(false);
                    Play(last ? Sfx.Victory : Sfx.StageClear);
                    Play(last ? Sfx.SayVictory : Sfx.SayClear);
                    if (last) Say("THE OVERMIND IS DESTROYED!", "THE SWARM FALLS APART", EndingSeconds);
                    else Say("STAGE CLEAR", null, ClearSeconds);
                }
                break;
            case Phase.Dying:
                Sim.Step(w, default);
                if (PhaseTime > DyingSeconds)
                {
                    Lives--;
                    if (Lives <= 0) { GameOver(); return; }
                    Begin(w.StageIndex, w.Round, _checkpoint, null);
                }
                break;
            case Phase.Clear or Phase.Ending:
                Sim.Step(w, c with { Fire = false, Orb = false });
                if (PhaseTime > (Phase == Phase.Ending ? EndingSeconds : ClearSeconds))
                {
                    Cleared++;
                    bool last = w.StageIndex == StageBook.All.Length - 1;
                    Begin(last ? 0 : w.StageIndex + 1, last ? w.Round + 1 : w.Round, 0, w.Ship, w.Orb);
                }
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void ExtraLives()
    {
        while (World.Score >= _nextExtra)
        {
            _nextExtra += ExtraEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into a stage (at a checkpoint), past its name.</summary>
    public void Go(int stage = 0, int round = 1, int checkpoint = 0)
    {
        NewGame();
        State = GameState.Playing;
        Begin(stage, round, checkpoint, null);
        _checkpoint = checkpoint;
        SetPhase(Phase.Play);
        Message = null;
    }

    public void SetLives(int n) => Lives = n;
}
