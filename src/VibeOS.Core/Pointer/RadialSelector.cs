namespace VibeOS.Core.Pointer;

/// <summary>
/// Angle-based slot selection for the radial quick menu (spec §5.9).
/// The highlighted slot is whichever segment the stick vector points into —
/// continuous angle, not discrete steps, which is what gives the GTA feel.
/// Returns -1 when the stick is centred (release-to-cancel).
/// Pure maths, fully testable.
/// </summary>
public static class RadialSelector
{
    /// <param name="stick">Stick reading, Y positive-up.</param>
    /// <param name="slotCount">Number of slots (6, 8 or 12).</param>
    /// <param name="centerDeadzone">Magnitude below which the stick counts as centred.</param>
    /// <param name="topSlot">
    /// Angle in degrees of the centre of slot 0, measured clockwise from
    /// 12 o'clock. 0 puts slot 0 at the top.
    /// </param>
    /// <returns>Slot index, or -1 when centred.</returns>
    public static int Select(
        Input.StickVector stick,
        int slotCount,
        double centerDeadzone = 0.35d,
        double topSlot = 0d)
    {
        if (stick.Magnitude <= centerDeadzone)
            return -1;

        // Screen angle: clockwise from 12 o'clock. Stick Y is positive-up,
        // X positive-right, so atan2(x, y) already gives exactly that.
        var degrees = Math.Atan2(stick.X, stick.Y) * 180d / Math.PI;
        if (degrees < 0d) degrees += 360d;

        var segment = 360d / slotCount;
        var shifted = degrees - topSlot + segment / 2d;
        shifted -= Math.Floor(shifted / 360d) * 360d;
        return (int)(shifted / segment) % slotCount;
    }
}
