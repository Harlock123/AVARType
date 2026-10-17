using Avalonia.Input;

namespace AVARType.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: left and right, up and down (up the screen is -1), and a button.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update()
    {
        _pad.Update();
    }

    /// <summary>
    /// The arrows or W A S D to fly; X, Ctrl or Shift to fire (tap to shoot, hold to charge the beam); Z or Space
    /// for the orb (launch it, call it back). On a gamepad, B, X or the right trigger fires and A works the orb.
    /// </summary>
    public Controls Player => Read(Key.Left, Key.Right, Key.Up, Key.Down, [Key.X, Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift], _pad, also: true);

    private Controls Read(Key left, Key right, Key up, Key down, Key[] fire, ControllerManager pad, bool also = false)
    {
        int dx = (Any(right) || also && Any(Key.D, Key.NumPad6) ? 1 : 0) - (Any(left) || also && Any(Key.A, Key.NumPad4) ? 1 : 0);
        if (dx == 0) dx = pad.DPadLeft || pad.LeftStickX < -0.4f ? -1 : pad.DPadRight || pad.LeftStickX > 0.4f ? 1 : 0;
        // Down the screen is +1.
        int dy = (Any(down) || also && Any(Key.S, Key.NumPad2) ? 1 : 0) - (Any(up) || also && Any(Key.W, Key.NumPad8) ? 1 : 0);
        if (dy == 0) dy = pad.DPadUp || pad.LeftStickY < -0.4f ? -1 : pad.DPadDown || pad.LeftStickY > 0.4f ? 1 : 0;
        bool orb = Any(Key.Space, Key.Z) || pad.ButtonA;
        bool button = Any(fire) || pad.ButtonB || pad.ButtonX || pad.RightTrigger > 0.5f;
        return new Controls(dx, dy, orb, button);
    }

    /// <summary>Enter or 1.</summary>
    public bool Start => Any(Key.Enter, Key.D1, Key.NumPad1) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
