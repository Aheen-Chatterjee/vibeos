namespace VibeOS.App.Overlay;

/// <summary>One wheel slot: a label plus an action id (spec §5.9).
/// Slots invoke actions through the same <c>ActionRouter</c> as chords —
/// there is no parallel action system.</summary>
public sealed record WheelSlot(string Label, string Action);

/// <summary>A named wheel: an ordered ring of slots.</summary>
public sealed record Wheel(string Name, List<WheelSlot> Slots);
