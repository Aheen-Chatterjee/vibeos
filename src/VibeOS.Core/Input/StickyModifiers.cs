namespace VibeOS.Core.Input;

/// <summary>
/// One-shot sticky modifiers for the Keys wheel (Shift/Ctrl/Win): tapping the
/// slot latches the modifier, the next executed action consumes it combined
/// with its own gesture, then it clears. Re-tapping cancels; latches expire
/// after a timeout so a forgotten Shift can't haunt later input.
/// Time is injected (ticks provider) for determinism.
/// </summary>
public sealed class StickyModifiers
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(10);

    private readonly TimeSpan _lifetime;
    private readonly Func<long> _ticks;
    private readonly Dictionary<string, long> _latched = new(StringComparer.OrdinalIgnoreCase);

    public static readonly IReadOnlyDictionary<string, string> Names =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sticky-shift"] = "Shift",
            ["sticky-ctrl"] = "Control",
            ["sticky-win"] = "Win",
            ["sticky-alt"] = "Alt",
        };

    public StickyModifiers(Func<long>? ticks = null, TimeSpan? lifetime = null)
    {
        _ticks = ticks ?? (() => Environment.TickCount64);
        _lifetime = lifetime ?? DefaultLifetime;
    }

    public static bool IsStickyAction(string actionId) => Names.ContainsKey(actionId);

    /// <summary>Toggles the latch. Returns true when now latched.</summary>
    public bool Toggle(string actionId)
    {
        if (!Names.TryGetValue(actionId, out _))
            throw new ArgumentOutOfRangeException(nameof(actionId));
        if (_latched.Remove(actionId)) return false;
        _latched[actionId] = _ticks();
        return true;
    }

    public bool IsLatched(string actionId)
    {
        Prune();
        return _latched.ContainsKey(actionId);
    }

    /// <summary>Live latches without clearing (for status display).</summary>
    public IReadOnlyList<string> PeekAll()
    {
        Prune();
        return _latched.OrderBy(kv => kv.Value).Select(kv => Names[kv.Key]).ToList();
    }

    /// <summary>Takes and clears all live latches, oldest first.</summary>
    public IReadOnlyList<string> TakeAll()
    {
        Prune();
        var all = _latched.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();
        _latched.Clear();
        return all;
    }

    public void Clear() => _latched.Clear();

    private void Prune()
    {
        var now = _ticks();
        foreach (var key in _latched.Keys.ToList())
        {
            if (now - _latched[key] > _lifetime.TotalMilliseconds)
                _latched.Remove(key);
        }
    }
}
