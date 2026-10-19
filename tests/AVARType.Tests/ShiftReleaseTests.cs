using Avalonia.Input;
using AVARType.Engine;

namespace AVARType.Tests;

/// <summary>With the keyboard option shift:both_capslock_cancel, X11 names a Shift's release CapsLock.</summary>
public sealed class ShiftReleaseTests
{
    [Fact]
    public void A_shift_let_go_as_caps_lock_is_let_go()
    {
        var input = new InputHandler();
        input.OnKeyDown(Key.LeftShift, PhysicalKey.ShiftLeft);
        input.OnKeyDown(Key.RightShift, PhysicalKey.ShiftRight);
        Assert.True(input.Has(Key.LeftShift) && input.Has(Key.RightShift));
        input.OnKeyUp(Key.CapsLock, PhysicalKey.ShiftLeft);
        Assert.False(input.Has(Key.LeftShift));
        Assert.True(input.Has(Key.RightShift));
        input.OnKeyUp(Key.CapsLock, PhysicalKey.ShiftRight);
        Assert.False(input.Has(Key.RightShift));
        (input as IDisposable)?.Dispose();
    }
}
