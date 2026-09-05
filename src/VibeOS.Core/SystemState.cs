namespace VibeOS.Core;

/// <summary>The single global state (spec §6 / PRD §6). There is deliberately
/// no other global mode.</summary>
public enum SystemState
{
    Active,
    Suspended
}
