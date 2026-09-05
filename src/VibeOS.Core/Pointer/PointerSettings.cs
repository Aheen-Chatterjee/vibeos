namespace VibeOS.Core.Pointer;

/// <summary>Pointer tuning. Defaults come from spec §5.4 / PRD §14-15.</summary>
public sealed record PointerSettings
{
    public double Deadzone { get; init; } = 0.14d;
    public double Gamma { get; init; } = 1.7d;
    public double MaxSpeedPxPerSec { get; init; } = 1500d;
    public double PrecisionSpeedPxPerSec { get; init; } = 350d;

    public static PointerSettings Default { get; } = new();
}
