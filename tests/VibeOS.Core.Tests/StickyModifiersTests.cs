using VibeOS.Core.Input;

namespace VibeOS.Core.Tests;

public class StickyModifiersTests
{
    [Fact]
    public void Toggle_latches_and_untoggles()
    {
        var s = new StickyModifiers(() => 0);
        Assert.True(s.Toggle("sticky-shift"));
        Assert.True(s.IsLatched("sticky-shift"));
        Assert.False(s.Toggle("sticky-shift"));
        Assert.False(s.IsLatched("sticky-shift"));
    }

    [Fact]
    public void Unknown_action_throws()
    {
        var s = new StickyModifiers(() => 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => s.Toggle("nope"));
    }

    [Fact]
    public void TakeAll_returns_oldest_first_and_clears()
    {
        var now = 1000L;
        var s = new StickyModifiers(() => now);
        s.Toggle("sticky-ctrl");
        now += 10;
        s.Toggle("sticky-shift");
        Assert.Equal(new[] { "sticky-ctrl", "sticky-shift" }, s.TakeAll());
        Assert.Empty(s.TakeAll());
    }

    [Fact]
    public void Latch_expires_after_lifetime()
    {
        var now = 0L;
        var s = new StickyModifiers(() => now, TimeSpan.FromMilliseconds(100));
        s.Toggle("sticky-win");
        Assert.True(s.IsLatched("sticky-win"));
        now = 500;
        Assert.False(s.IsLatched("sticky-win"));
        Assert.Empty(s.TakeAll());
    }

    [Fact]
    public void Action_names_are_case_insensitive()
    {
        var s = new StickyModifiers(() => 0);
        Assert.True(s.Toggle("STICKY-SHIFT"));
        Assert.True(s.IsLatched("sticky-shift"));
    }
}
