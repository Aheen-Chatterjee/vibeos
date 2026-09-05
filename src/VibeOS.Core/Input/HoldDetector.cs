namespace VibeOS.Core.Input;

/// <summary>
/// Tracks press timing per button to distinguish tap / hold / double-tap.
/// Time is injected (never read from a clock) so behaviour is deterministic
/// and unit-testable.
/// </summary>
public sealed class HoldDetector
{
    private readonly int _holdThresholdMs;
    private readonly int _doubleTapWindowMs;

    private readonly Dictionary<ButtonId, long> _pressedAt = new();
    private readonly HashSet<ButtonId> _holdFired = new();
    private readonly Dictionary<ButtonId, long> _lastTapAt = new();

    public HoldDetector(int holdThresholdMs, int doubleTapWindowMs)
    {
        _holdThresholdMs = holdThresholdMs;
        _doubleTapWindowMs = doubleTapWindowMs;
    }

    public void Press(ButtonId button, long nowMs)
    {
        _pressedAt[button] = nowMs;
        _holdFired.Remove(button);
    }

    public HoldOutcome Release(ButtonId button, long nowMs)
    {
        if (!_pressedAt.Remove(button, out var downAt))
            return HoldOutcome.None;

        var wasHold = _holdFired.Remove(button) || (nowMs - downAt) >= _holdThresholdMs;
        if (wasHold)
        {
            _lastTapAt.Remove(button);
            return HoldOutcome.HoldRelease;
        }

        if (_lastTapAt.TryGetValue(button, out var lastTap) &&
            (nowMs - lastTap) <= _doubleTapWindowMs)
        {
            _lastTapAt.Remove(button);
            return HoldOutcome.DoubleTap;
        }

        _lastTapAt[button] = nowMs;
        return HoldOutcome.Tap;
    }

    /// <summary>Returns buttons whose hold threshold elapsed since the last poll.
    /// Each button is reported at most once per press.</summary>
    public IReadOnlyList<ButtonId> Poll(long nowMs)
    {
        List<ButtonId>? crossed = null;

        foreach (var (button, downAt) in _pressedAt)
        {
            if (_holdFired.Contains(button)) continue;
            if ((nowMs - downAt) < _holdThresholdMs) continue;
            (crossed ??= new List<ButtonId>()).Add(button);
        }

        if (crossed is null) return Array.Empty<ButtonId>();
        foreach (var b in crossed) _holdFired.Add(b);
        return crossed;
    }

    /// <summary>Drops all pending state. Called on suspend and on controller
    /// disconnect so a held button cannot survive the gap.</summary>
    public void Reset()
    {
        _pressedAt.Clear();
        _holdFired.Clear();
        _lastTapAt.Clear();
    }
}
