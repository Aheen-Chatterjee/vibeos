namespace VibeOS.App.Input;

/// <summary>
/// Central haptic vocabulary (PRD §36). Philosophy: only rare, meaningful
/// events rumble — mode flips, overlay open/execute/close, voice lifecycle,
/// sticky latch. Frequent events (chords, traversal, typing, clicks, pointer)
/// stay silent, or everything blends into unfeelable noise.
///
/// Durations respect motor physics: cheap ERM motors need ~80–150 ms just to
/// spin up, so nothing meaningful is shorter than that.
/// </summary>
public static class Haptics
{
    /// <summary>Firm confirm: wheel/keyboard execute, sticky latch-on.</summary>
    public static void Confirm(Action<ushort, ushort, uint>? rumble) =>
        rumble?.Invoke(0x8000, 0x8000, 180);

    /// <summary>Soft landing: cancel, close, voice stop, unlatch.</summary>
    public static void Soft(Action<ushort, ushort, uint>? rumble) =>
        rumble?.Invoke(0x5000, 0x3000, 150);

    /// <summary>Mode flip: suspend/resume, voice start.</summary>
    public static void Toggle(Action<ushort, ushort, uint>? rumble) =>
        rumble?.Invoke(0x9000, 0x6000, 220);

    /// <summary>Error buzz: voice cancel/failure. Low motor, long.</summary>
    public static void Error(Action<ushort, ushort, uint>? rumble) =>
        rumble?.Invoke(0xC000, 0x2000, 320);
}
