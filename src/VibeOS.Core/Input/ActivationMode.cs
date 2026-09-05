namespace VibeOS.Core.Input;

/// <summary>Chord activation modes (PRD §18).</summary>
public enum ActivationMode
{
    Press,
    Release,
    Hold,
    DoubleTap,
    WhileHeld
}

/// <summary>What a release turned out to mean.</summary>
public enum HoldOutcome
{
    None,
    Tap,
    HoldRelease,
    DoubleTap
}
