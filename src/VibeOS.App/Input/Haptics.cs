namespace VibeOS.App.Input;

/// <summary>
/// Central haptic vocabulary (PRD §36). Legibility first: cheap pads need
/// strong values, so confirms use both motors and nothing important is under
/// ~60 ms. Ticks stay on the high-frequency (right) motor so they read as
/// crisp clicks rather than buzzes. Deliberately silent: pointer movement and
/// mouse clicks never rumble.
/// </summary>
public static class Haptics
{
    /// <summary>Crisp tick: selection traversal, chord fire, unlatch.</summary>
    public static void Tick(Action<ushort, ushort, uint>? rumble) =>
        rumble?.Invoke(0x0000, 0x5000, 45);

    /// <summary>Firm confirm: execute, latch-on, wheel/keyboard open.</summary>
    public static void Confirm(Action<ushort, ushort, uint>? rumble) =>
        rumble?.Invoke(0x7000, 0x7000, 90);

    /// <summary>Soft landing: cancel, close, voice stop.</summary>
    public static void Soft(Action<ushort, ushort, uint>? rumble) =>
        rumble?.Invoke(0x4000, 0x2000, 110);

    /// <summary>Mode flip: suspend/resume, voice start.</summary>
    public static void Toggle(Action<ushort, ushort, uint>? rumble) =>
        rumble?.Invoke(0x8000, 0x5000, 130);

    /// <summary>Error buzz: voice cancel/failure. Low motor, long.</summary>
    public static void Error(Action<ushort, ushort, uint>? rumble) =>
        rumble?.Invoke(0xB000, 0x1000, 200);
}
