using System.Collections.Immutable;

namespace VibeOS.Core.Input;

/// <summary>Immutable snapshot of the controller at one poll. Immutability keeps
/// chord resolution a pure function of state, which is what makes it testable.</summary>
public sealed class ControllerSnapshot
{
    public static readonly ControllerSnapshot Empty =
        new(ImmutableHashSet<ButtonId>.Empty, StickVector.Zero, StickVector.Zero, 0f, 0f, 0L, false);

    public ControllerSnapshot(
        ImmutableHashSet<ButtonId> pressed,
        StickVector leftStick,
        StickVector rightStick,
        float leftTrigger,
        float rightTrigger,
        long timestampMs,
        bool connected)
    {
        Pressed = pressed;
        LeftStick = leftStick;
        RightStick = rightStick;
        LeftTrigger = leftTrigger;
        RightTrigger = rightTrigger;
        TimestampMs = timestampMs;
        Connected = connected;
    }

    public ImmutableHashSet<ButtonId> Pressed { get; }
    public StickVector LeftStick { get; }
    public StickVector RightStick { get; }

    /// <summary>0..1</summary>
    public float LeftTrigger { get; }

    /// <summary>0..1</summary>
    public float RightTrigger { get; }

    public long TimestampMs { get; }
    public bool Connected { get; }

    public bool IsDown(ButtonId button) => Pressed.Contains(button);
}
