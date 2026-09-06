using VibeOS.Core.Input;
using VibeOS.Core.Pointer;

namespace VibeOS.Core.Tests;

public class RadialSelectorTests
{
    [Fact]
    public void Centred_stick_selects_nothing()
    {
        Assert.Equal(-1, RadialSelector.Select(StickVector.Zero, 8));
        Assert.Equal(-1, RadialSelector.Select(new StickVector(0.1f, 0.1f), 8));
    }

    [Fact]
    public void Straight_up_selects_slot_zero()
    {
        Assert.Equal(0, RadialSelector.Select(new StickVector(0f, 1f), 8));
    }

    [Fact]
    public void Straight_right_selects_slot_two_of_eight()
    {
        Assert.Equal(2, RadialSelector.Select(new StickVector(1f, 0f), 8));
    }

    [Fact]
    public void Straight_down_selects_slot_four_of_eight()
    {
        Assert.Equal(4, RadialSelector.Select(new StickVector(0f, -1f), 8));
    }

    [Fact]
    public void Straight_left_selects_slot_six_of_eight()
    {
        Assert.Equal(6, RadialSelector.Select(new StickVector(-1f, 0f), 8));
    }

    [Fact]
    public void Slightly_off_axis_stays_in_the_same_segment()
    {
        // 10 degrees off vertical is still inside slot 0's 45-degree segment.
        var rad = 10d * Math.PI / 180d;
        var stick = new StickVector((float)Math.Sin(rad), (float)Math.Cos(rad));
        Assert.Equal(0, RadialSelector.Select(stick, 8));
    }

    [Fact]
    public void Boundary_angle_rounds_into_the_next_segment()
    {
        // 23 degrees clockwise is just past slot 0's edge (22.5°).
        var rad = 23d * Math.PI / 180d;
        var stick = new StickVector((float)Math.Sin(rad), (float)Math.Cos(rad));
        Assert.Equal(1, RadialSelector.Select(stick, 8));
    }

    [Fact]
    public void Six_and_twelve_slot_wheels_cover_the_full_circle()
    {
        Assert.Equal(0, RadialSelector.Select(new StickVector(0f, 1f), 6));
        Assert.Equal(3, RadialSelector.Select(new StickVector(0f, -1f), 6));
        Assert.Equal(0, RadialSelector.Select(new StickVector(0f, 1f), 12));
        Assert.Equal(6, RadialSelector.Select(new StickVector(0f, -1f), 12));
        Assert.Equal(3, RadialSelector.Select(new StickVector(1f, 0f), 12));
    }
}
