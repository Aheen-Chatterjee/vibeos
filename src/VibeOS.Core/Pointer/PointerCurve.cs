using VibeOS.Core.Input;

namespace VibeOS.Core.Pointer;

/// <summary>
/// Converts a stick reading into a pointer velocity in pixels/second.
///
/// Radial deadzone, then the magnitude is renormalized across the remaining
/// range and raised to <c>Gamma</c>. Gamma above 1 means a light tilt gives
/// pixel precision while full tilt crosses a large monitor quickly (PRD §14).
/// </summary>
public static class PointerCurve
{
    public static (double DxPerSec, double DyPerSec) Velocity(
        StickVector stick,
        PointerSettings settings,
        bool precision)
    {
        double rawMagnitude = stick.Magnitude;
        if (rawMagnitude <= settings.Deadzone)
            return (0d, 0d);

        // Clamp so a full diagonal (magnitude ~1.414) cannot exceed max speed.
        double magnitude = rawMagnitude > 1d ? 1d : rawMagnitude;

        double normalized = (magnitude - settings.Deadzone) / (1d - settings.Deadzone);
        double curved = Math.Pow(normalized, settings.Gamma);
        double maxSpeed = precision ? settings.PrecisionSpeedPxPerSec : settings.MaxSpeedPxPerSec;
        double speed = curved * maxSpeed;

        // Unit direction taken from the raw reading so direction survives clamping.
        double ux = stick.X / rawMagnitude;
        double uy = stick.Y / rawMagnitude;

        return (ux * speed, uy * speed);
    }
}
