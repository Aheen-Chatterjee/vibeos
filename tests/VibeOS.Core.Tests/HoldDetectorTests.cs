using VibeOS.Core.Input;

namespace VibeOS.Core.Tests;

public class HoldDetectorTests
{
    private static HoldDetector NewDetector() => new(holdThresholdMs: 200, doubleTapWindowMs: 300);

    [Fact]
    public void Release_before_threshold_is_a_tap()
    {
        var d = NewDetector();
        d.Press(ButtonId.Y, 1000);
        var outcome = d.Release(ButtonId.Y, 1150);
        Assert.Equal(HoldOutcome.Tap, outcome);
    }

    [Fact]
    public void Release_after_threshold_is_a_hold_release()
    {
        var d = NewDetector();
        d.Press(ButtonId.Y, 1000);
        d.Poll(1250);
        var outcome = d.Release(ButtonId.Y, 1300);
        Assert.Equal(HoldOutcome.HoldRelease, outcome);
    }

    [Fact]
    public void Poll_reports_button_crossing_hold_threshold_exactly_once()
    {
        var d = NewDetector();
        d.Press(ButtonId.Start, 1000);

        Assert.Empty(d.Poll(1100));                            // not yet
        Assert.Equal(new[] { ButtonId.Start }, d.Poll(1200));  // crosses
        Assert.Empty(d.Poll(1400));                            // not again
    }

    [Fact]
    public void Second_press_within_window_is_a_double_tap()
    {
        var d = NewDetector();
        d.Press(ButtonId.A, 1000);
        d.Release(ButtonId.A, 1050);
        d.Press(ButtonId.A, 1200);
        var outcome = d.Release(ButtonId.A, 1240);
        Assert.Equal(HoldOutcome.DoubleTap, outcome);
    }

    [Fact]
    public void Second_press_outside_window_is_a_plain_tap()
    {
        var d = NewDetector();
        d.Press(ButtonId.A, 1000);
        d.Release(ButtonId.A, 1050);
        d.Press(ButtonId.A, 1500);
        var outcome = d.Release(ButtonId.A, 1540);
        Assert.Equal(HoldOutcome.Tap, outcome);
    }

    [Fact]
    public void Release_without_press_is_ignored()
    {
        var d = NewDetector();
        Assert.Equal(HoldOutcome.None, d.Release(ButtonId.B, 1000));
    }

    [Fact]
    public void Reset_clears_all_pending_state()
    {
        var d = NewDetector();
        d.Press(ButtonId.Start, 1000);
        d.Reset();
        Assert.Empty(d.Poll(2000));
        Assert.Equal(HoldOutcome.None, d.Release(ButtonId.Start, 2100));
    }
}
