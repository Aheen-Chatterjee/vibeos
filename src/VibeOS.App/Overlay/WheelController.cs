using VibeOS.App.Overlay;
using VibeOS.Core.Input;
using VibeOS.Core.Pointer;

namespace VibeOS.App.Overlay;

/// <summary>
/// Radial-wheel state machine, owned by the input thread (spec §5.9).
/// <code>
/// LB + RB held (~150 ms) → wheel opens
/// Right stick direction  → aim (continuous angle)
/// D-pad Left / Right     → cycle between wheels
/// Release LB + RB        → execute highlighted slot
/// B, or release centred  → cancel, execute nothing
/// </code>
/// While open the controller reports <see cref="SuppressInput"/>, which tells
/// Program to freeze pointer output and chord dispatch (overlay precedence,
/// PRD §46). If the overlay fails, releasing the bumpers executes nothing
/// rather than firing a blind action — <see cref="Update"/> returns null
/// unless a live wheel with a highlighted slot closed normally.
/// </summary>
public sealed class WheelController
{
    private readonly int _openHoldMs;

    private IReadOnlyList<Wheel> _wheels = Array.Empty<Wheel>();
    private readonly object _gate = new();

    private long? _armedAt;
    private bool _open;
    private int _wheelIndex;
    private int _highlighted = -1;
    private int _tickedHighlight = -2;
    private long _lastTickAt;

    /// <summary>
    /// Subtle haptic ticks (PRD §36 — kept quiet: open/execute/cancel only).
    /// Set once at startup; may be null.
    /// </summary>
    public Action<ushort, ushort, uint>? Rumble { get; set; }

    public WheelController(int openHoldMs = 150)
    {
        _openHoldMs = openHoldMs;
    }

    public bool IsOpen
    {
        get { lock (_gate) return _open; }
    }

    /// <summary>True while open: pointer and chords stay frozen.</summary>
    public bool SuppressInput => IsOpen;

    public void SetWheels(IReadOnlyList<Wheel> wheels)
    {
        lock (_gate) _wheels = wheels;
    }

    /// <summary>Snapshot for the overlay thread. Cheap; called every tick.</summary>
    public WheelView View
    {
        get
        {
            lock (_gate)
            {
                var wheel = EffectiveWheel(_wheels, _wheelIndex);
                return new WheelView(
                    _open,
                    wheel?.Name ?? string.Empty,
                    wheel?.Slots ?? (IReadOnlyList<WheelSlot>)Array.Empty<WheelSlot>(),
                    _highlighted);
            }
        }
    }

    /// <summary>
    /// Advances the state machine. Returns an action id to execute, or null.
    /// </summary>
    public string? Update(ControllerSnapshot previous, ControllerSnapshot current, long now)
    {
        lock (_gate)
        {
            var bumpers = current.IsDown(ButtonId.LB) && current.IsDown(ButtonId.RB);

            if (!_open)
            {
                if (!bumpers)
                {
                    _armedAt = null;
                    return null;
                }

                _armedAt ??= now;
                if (now - _armedAt.Value < _openHoldMs) return null;

                _armedAt = null;
                _open = true;
                _wheelIndex = 0;
                _highlighted = -1;
                _tickedHighlight = -2;
                Input.Haptics.Confirm(Rumble);
                return null;
            }

            // Open: aim with the right stick.
            var wheel = EffectiveWheel(_wheels, _wheelIndex);
            var slots = wheel?.Slots.Count ?? 0;
            _highlighted = slots == 0
                ? -1
                : RadialSelector.Select(current.RightStick, slots);

            // Traversal tick: each new slot clicks, throttled so a sweep
            // feels like detents rather than a buzz.
            if (_highlighted != _tickedHighlight && now - _lastTickAt >= 60)
            {
                _tickedHighlight = _highlighted;
                _lastTickAt = now;
                Input.Haptics.Tick(Rumble);
            }

            // D-pad cycles wheels on press edges.
            if (IsEdge(previous, current, ButtonId.DpadLeft))
                _wheelIndex = Mod(_wheelIndex - 1, _wheels.Count);
            if (IsEdge(previous, current, ButtonId.DpadRight))
                _wheelIndex = Mod(_wheelIndex + 1, _wheels.Count);

            // B cancels.
            if (IsEdge(previous, current, ButtonId.B))
            {
                _open = false;
                _highlighted = -1;
                Input.Haptics.Soft(Rumble);
                return null;
            }

            // Releasing the bumpers closes: execute highlight, or nothing.
            if (!bumpers)
            {
                _open = false;
                wheel = EffectiveWheel(_wheels, _wheelIndex);
                var index = _highlighted;
                _highlighted = -1;
                if (wheel is null || (uint)index >= (uint)wheel.Slots.Count)
                    return null;
                Input.Haptics.Confirm(Rumble);
                return wheel.Slots[index].Action;
            }

            return null;
        }
    }

    /// <summary>Closes without executing. Called on suspend / panic (spec §8).</summary>
    public void ForceClose()
    {
        lock (_gate)
        {
            _open = false;
            _armedAt = null;
            _highlighted = -1;
        }
    }

    private static int Mod(int a, int n) => n == 0 ? 0 : ((a % n) + n) % n;

    private static Wheel? EffectiveWheel(IReadOnlyList<Wheel> wheels, int index)
    {
        if (wheels.Count == 0) return null;
        var wheel = wheels[Mod(index, wheels.Count)];
        // An empty wheel falls back to wheel 1 (spec §5.9).
        if (wheel.Slots.Count == 0) wheel = wheels[0];
        return wheel.Slots.Count == 0 ? null : wheel;
    }

    private static bool IsEdge(ControllerSnapshot previous, ControllerSnapshot current, ButtonId button) =>
        current.IsDown(button) && !previous.IsDown(button);

    /// <summary>Immutable view consumed by the overlay render thread.</summary>
    public sealed record WheelView(
        bool Open,
        string WheelName,
        IReadOnlyList<WheelSlot> Slots,
        int Highlighted);
}
