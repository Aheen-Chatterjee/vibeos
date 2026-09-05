using System.Collections.Immutable;

namespace VibeOS.Core.Input;

/// <summary>
/// One binding: modifiers + trigger + activation mode -> action id.
/// <paramref name="AppContext"/> null means global.
/// </summary>
public sealed record ChordDefinition(
    ImmutableHashSet<ButtonId> Modifiers,
    ButtonId Trigger,
    ActivationMode Mode,
    string ActionId,
    string? AppContext = null)
{
    public int Priority { get; init; }

    /// <summary>Specificity for the §46 precedence ladder: more modifiers wins,
    /// then app-specific beats global, then explicit priority.</summary>
    public (int Modifiers, int Context, int Priority) Specificity =>
        (Modifiers.Count, AppContext is null ? 0 : 1, Priority);
}
