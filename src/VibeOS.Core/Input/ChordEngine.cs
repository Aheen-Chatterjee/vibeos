namespace VibeOS.Core.Input;

/// <summary>
/// Resolves a trigger press into exactly one action (PRD §45, §46).
///
/// A candidate matches when every one of its modifiers is currently held. The
/// most specific match wins, so LB+RB+A beats LB+A beats A. Resolution happens
/// at trigger time against the already-held modifier set, which adds no latency
/// and never double-dispatches.
/// </summary>
public sealed class ChordEngine
{
    private readonly List<ChordDefinition> _chords;

    public ChordEngine(IEnumerable<ChordDefinition> chords)
    {
        _chords = chords.ToList();
    }

    public ChordDefinition? Resolve(
        IReadOnlySet<ButtonId> held,
        ButtonId trigger,
        ActivationMode mode,
        string? appContext)
    {
        ChordDefinition? best = null;

        foreach (var chord in _chords)
        {
            if (chord.Trigger != trigger) continue;
            if (chord.Mode != mode) continue;
            if (chord.AppContext is not null &&
                !string.Equals(chord.AppContext, appContext, StringComparison.OrdinalIgnoreCase))
                continue;

            var allModifiersHeld = true;
            foreach (var m in chord.Modifiers)
            {
                if (!held.Contains(m)) { allModifiersHeld = false; break; }
            }
            if (!allModifiersHeld) continue;

            if (best is null || Compare(chord.Specificity, best.Specificity) > 0)
                best = chord;
        }

        return best;
    }

    private static int Compare(
        (int Modifiers, int Context, int Priority) a,
        (int Modifiers, int Context, int Priority) b)
    {
        if (a.Modifiers != b.Modifiers) return a.Modifiers.CompareTo(b.Modifiers);
        if (a.Context != b.Context) return a.Context.CompareTo(b.Context);
        return a.Priority.CompareTo(b.Priority);
    }
}
