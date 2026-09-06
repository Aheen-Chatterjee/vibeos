using VibeOS.Core.Input;

namespace VibeOS.Core.Tests;

public class ChordParserTests
{
    [Fact]
    public void Single_button_is_a_trigger_with_no_modifiers()
    {
        Assert.True(ChordParser.TryParse("A", out var mods, out var trigger, out var error));
        Assert.Empty(mods);
        Assert.Equal(ButtonId.A, trigger);
        Assert.Null(error);
    }

    [Fact]
    public void Two_button_chord_splits_modifier_and_trigger()
    {
        Assert.True(ChordParser.TryParse("LB+X", out var mods, out var trigger, out _));
        Assert.Equal(new[] { ButtonId.LB }, mods);
        Assert.Equal(ButtonId.X, trigger);
    }

    [Fact]
    public void Two_modifier_chord_keeps_both_modifiers()
    {
        Assert.True(ChordParser.TryParse("LB+RB+A", out var mods, out var trigger, out _));
        Assert.Equal(2, mods.Count);
        Assert.Contains(ButtonId.LB, mods);
        Assert.Contains(ButtonId.RB, mods);
        Assert.Equal(ButtonId.A, trigger);
    }

    [Fact]
    public void Parsing_is_case_insensitive()
    {
        Assert.True(ChordParser.TryParse("rb+dpad_down", out var mods, out var trigger, out _));
        Assert.Equal(new[] { ButtonId.RB }, mods);
        Assert.Equal(ButtonId.DpadDown, trigger);
    }

    [Fact]
    public void Dpad_accepts_underscore_and_compact_forms()
    {
        Assert.True(ChordParser.TryParse("RB+DPAD_LEFT", out _, out var t1, out _));
        Assert.True(ChordParser.TryParse("RB+DpadLeft", out _, out var t2, out _));
        Assert.Equal(ButtonId.DpadLeft, t1);
        Assert.Equal(ButtonId.DpadLeft, t2);
    }

    [Fact]
    public void Unknown_button_fails_with_error()
    {
        Assert.False(ChordParser.TryParse("LB+ZZZ", out _, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Empty_chord_fails()
    {
        Assert.False(ChordParser.TryParse("", out _, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Duplicate_button_fails()
    {
        Assert.False(ChordParser.TryParse("LB+LB+A", out _, out _, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Parsed_chord_resolves_through_chord_engine()
    {
        Assert.True(ChordParser.TryParse("LB+A", out var mods, out var trigger, out _));
        var engine = new ChordEngine(new[]
        {
            new ChordDefinition(mods, trigger, ActivationMode.Press, "select_all", null)
        });

        var result = engine.Resolve(
            new[] { ButtonId.LB, ButtonId.A }.ToHashSet(),
            ButtonId.A, ActivationMode.Press, null);
        Assert.Equal("select_all", result?.ActionId);
    }
}
