namespace VibeOS.Core.Input;

/// <summary>Controller buttons, named after SDL3's gamepad abstraction so the
/// mapping is controller-independent (PRD §49).</summary>
public enum ButtonId
{
    A, B, X, Y,
    LB, RB,
    Back, Start,
    L3, R3,
    DpadUp, DpadDown, DpadLeft, DpadRight,
    LT, RT
}
