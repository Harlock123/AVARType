using AVARType.Audio;

namespace AVARType.Engine;

/// <summary>
/// Plays the game's sounds through OpenAL Soft, which mixes as many at once as the game needs. The
/// tunes play on the music channel. In a virtual machine a larger mixing buffer keeps the sound from breaking up.
/// </summary>
public sealed class SoundManager : IDisposable
{
    private readonly IAudioEngine _engine;
    private readonly string _folder;

    public SoundManager(bool silent = false)
    {
        _folder = Path.Combine(AppContext.BaseDirectory, "Sounds");
        _engine = silent
            ? new NullAudioEngine("silent")
            : OpenAlAudioEngine.CreateOrSilent(AudioBuffer.PeriodFrames(AudioBuffer.Automatic, VirtualMachine.Detect()));
        _engine.SetVolumes(0.9f, 0.8f, 0.7f);
        _engine.SetAmbienceVolume(0.45f);
    }

    public bool IsAvailable => _engine.IsAvailable;

    public bool IsMuted
    {
        get => _muted;
        set
        {
            _muted = value;
            if (value) StopAll();
        }
    }
    private bool _muted;

    private string File(Sfx s) => Path.Combine(_folder, s + ".wav");

    public void Play(Sfx sfx)
    {
        if (_muted) return;
        switch (sfx)
        {
            case Sfx.TitleTune: _engine.PlayMusic(File(sfx), loop: true); return;
            case Sfx.GameOver or Sfx.StageClear or Sfx.Victory: _engine.PlayMusic(File(sfx), loop: false); return;
            case Sfx.Music or Sfx.BossMusic: _engine.StopMusic(); _engine.StopAmbience(); _engine.PlayAmbience(File(sfx)); return;
            case Sfx.MusicStop: _engine.StopAmbience(); return;
        }
        _engine.PlayEffect(File(sfx), sfx switch { Sfx.Pea => 0.4f, Sfx.Laser => 0.4f, Sfx.EnemyFire => 0.45f, Sfx.Crack => 0.5f, Sfx.Clink => 0.5f, Sfx.Chew => 0.45f, Sfx.MissileOut => 0.5f, _ => 1f });
    }

    public void StopAll()
    {
        _engine.StopAmbience();
        _engine.StopMusic();
    }

    public void Dispose() => _engine.Dispose();
}
