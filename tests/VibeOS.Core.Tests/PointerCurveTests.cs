using VibeOS.Core.Input;
using VibeOS.Core.Pointer;

namespace VibeOS.Core.Tests;

public class PointerCurveTests
{
    private static readonly PointerSettings S = PointerSettings.Default;

    [Fact]
    public void Inside_deadzone_produces_no_movement()
    {
        var (dx, dy) = PointerCurve.Velocity(new StickVector(0.10f, 0f), S, precision: false);
        Assert.Equal(0d, dx);
        Assert.Equal(0d, dy);
    }

    [Fact]
    public void Just_below_deadzone_produces_no_movement()
    {
        var (dx, dy) = PointerCurve.Velocity(new StickVector(0.139f, 0f), S, precision: false);
        Assert.Equal(0d, dx);
        Assert.Equal(0d, dy);
    }

    [Fact]
    public void At_the_deadzone_boundary_movement_is_negligible()
    {
        // StickVector is float, Deadzone is double: 0.14f widens to
        // 0.14000000059604645, which is strictly greater than 0.14d, so this
        // input lands a hair outside the deadzone. Asserting exact zero here
        // would be testing IEEE754 rather than the curve. What actually matters
        // is that a stick resting on the boundary cannot creep the cursor, so
        // assert the velocity is far below one pixel per second.
        var (dx, dy) = PointerCurve.Velocity(new StickVector(0.14f, 0f), S, precision: false);
        Assert.True(Math.Abs(dx) < 0.001d, $"boundary velocity {dx} px/s would be visible");
        Assert.Equal(0d, dy);
    }

    [Fact]
    public void Full_tilt_reaches_max_speed()
    {
        var (dx, _) = PointerCurve.Velocity(new StickVector(1.0f, 0f), S, precision: false);
        Assert.Equal(S.MaxSpeedPxPerSec, dx, precision: 3);
    }

    [Fact]
    public void Precision_mode_caps_at_precision_speed()
    {
        var (dx, _) = PointerCurve.Velocity(new StickVector(1.0f, 0f), S, precision: true);
        Assert.Equal(S.PrecisionSpeedPxPerSec, dx, precision: 3);
    }

    [Fact]
    public void Small_input_past_deadzone_is_much_slower_than_linear()
    {
        // Gamma > 1 means a light tilt should be far slower than a
        // proportional mapping would give - that is what gives pixel precision.
        var (dx, _) = PointerCurve.Velocity(new StickVector(0.3f, 0f), S, precision: false);
        var linear = 0.3d * S.MaxSpeedPxPerSec;
        Assert.True(dx < linear * 0.5d, $"expected {dx} to be well under half of {linear}");
        Assert.True(dx > 0d);
    }

    [Fact]
    public void Direction_is_preserved_and_deadzone_is_radial()
    {
        // A diagonal whose magnitude exceeds the deadzone must move on both axes.
        var (dx, dy) = PointerCurve.Velocity(new StickVector(0.5f, 0.5f), S, precision: false);
        Assert.True(dx > 0d);
        Assert.True(dy > 0d);
        Assert.Equal(dx, dy, precision: 6);
    }

    [Fact]
    public void Negative_axes_move_negatively()
    {
        var (dx, dy) = PointerCurve.Velocity(new StickVector(-1.0f, -1.0f), S, precision: false);
        Assert.True(dx < 0d);
        Assert.True(dy < 0d);
    }

    [Fact]
    public void Magnitude_is_clamped_so_diagonals_do_not_exceed_max_speed()
    {
        var (dx, dy) = PointerCurve.Velocity(new StickVector(1.0f, 1.0f), S, precision: false);
        var speed = Math.Sqrt(dx * dx + dy * dy);
        Assert.True(speed <= S.MaxSpeedPxPerSec + 0.001d, $"speed {speed} exceeded max {S.MaxSpeedPxPerSec}");
    }
}
