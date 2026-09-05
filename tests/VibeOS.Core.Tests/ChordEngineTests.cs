using System.Collections.Immutable;
using VibeOS.Core.Input;

namespace VibeOS.Core.Tests;

public class ChordEngineTests
{
    private static ChordDefinition Chord(string action, ButtonId trigger,
        ActivationMode mode = ActivationMode.Press, string? app = null, ButtonId[]? mods = null) =>
        new((mods ?? Array.Empty<ButtonId>()).ToImmutableHashSet(), trigger, mode, action, app);

    private static ImmutableHashSet<ButtonId> Held(params ButtonId[] b) => b.ToImmutableHashSet();

    [Fact]
    public void Resolves_base_action_when_no_modifiers_held()
    {
        var engine = new ChordEngine(new[] { Chord("activate", ButtonId.A) });
        var result = engine.Resolve(Held(ButtonId.A), ButtonId.A, ActivationMode.Press, null);
        Assert.Equal("activate", result?.ActionId);
    }

    [Fact]
    public void Prefers_more_specific_chord_over_base_action()
    {
        var engine = new ChordEngine(new[]
        {
            Chord("activate", ButtonId.A),
            Chord("select_all", ButtonId.A, mods: new[] { ButtonId.LB })
        });

        var result = engine.Resolve(Held(ButtonId.LB, ButtonId.A), ButtonId.A, ActivationMode.Press, null);
        Assert.Equal("select_all", result?.ActionId);
    }

    [Fact]
    public void Two_modifier_chord_beats_one_modifier_chord()
    {
        var engine = new ChordEngine(new[]
        {
            Chord("select_all", ButtonId.A, mods: new[] { ButtonId.LB }),
            Chord("task_switcher", ButtonId.A, mods: new[] { ButtonId.LB, ButtonId.RB })
        });

        var result = engine.Resolve(Held(ButtonId.LB, ButtonId.RB, ButtonId.A), ButtonId.A, ActivationMode.Press, null);
        Assert.Equal("task_switcher", result?.ActionId);
    }

    [Fact]
    public void Chord_requiring_unheld_modifier_does_not_match()
    {
        var engine = new ChordEngine(new[] { Chord("select_all", ButtonId.A, mods: new[] { ButtonId.LB }) });
        var result = engine.Resolve(Held(ButtonId.A), ButtonId.A, ActivationMode.Press, null);
        Assert.Null(result);
    }

    [Fact]
    public void App_specific_chord_beats_global_chord_with_same_modifier_count()
    {
        var engine = new ChordEngine(new[]
        {
            Chord("global_find", ButtonId.X, mods: new[] { ButtonId.RB }),
            Chord("vscode_palette", ButtonId.X, app: "Code.exe", mods: new[] { ButtonId.RB })
        });

        var result = engine.Resolve(Held(ButtonId.RB, ButtonId.X), ButtonId.X, ActivationMode.Press, "Code.exe");
        Assert.Equal("vscode_palette", result?.ActionId);
    }

    [Fact]
    public void App_specific_chord_is_ignored_in_a_different_app()
    {
        var engine = new ChordEngine(new[]
        {
            Chord("global_find", ButtonId.X, mods: new[] { ButtonId.RB }),
            Chord("vscode_palette", ButtonId.X, app: "Code.exe", mods: new[] { ButtonId.RB })
        });

        var result = engine.Resolve(Held(ButtonId.RB, ButtonId.X), ButtonId.X, ActivationMode.Press, "chrome.exe");
        Assert.Equal("global_find", result?.ActionId);
    }

    [Fact]
    public void Activation_mode_must_match()
    {
        var engine = new ChordEngine(new[] { Chord("dictate", ButtonId.Y, ActivationMode.Hold) });

        Assert.Null(engine.Resolve(Held(ButtonId.Y), ButtonId.Y, ActivationMode.Press, null));
        Assert.Equal("dictate", engine.Resolve(Held(ButtonId.Y), ButtonId.Y, ActivationMode.Hold, null)?.ActionId);
    }

    [Fact]
    public void Resolution_is_deterministic_for_equally_specific_chords()
    {
        // Same trigger, same modifier count, same context: higher Priority wins.
        var engine = new ChordEngine(new[]
        {
            Chord("low", ButtonId.B, mods: new[] { ButtonId.LB }) with { Priority = 1 },
            Chord("high", ButtonId.B, mods: new[] { ButtonId.LB }) with { Priority = 5 }
        });

        var result = engine.Resolve(Held(ButtonId.LB, ButtonId.B), ButtonId.B, ActivationMode.Press, null);
        Assert.Equal("high", result?.ActionId);
    }
}
