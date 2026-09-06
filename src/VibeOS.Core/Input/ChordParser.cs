using System.Collections.Immutable;

namespace VibeOS.Core.Input;

/// <summary>
/// Parses chord strings from configuration (PRD §56) into modifiers + trigger.
/// Grammar: "LB+X", "RB+DPAD_DOWN", "LB+RB+A". The trigger is the last token;
/// everything before it is a modifier. Case-insensitive; D-pad accepts both
/// "DPAD_UP" and "DPADUP". Pure function, no platform dependencies.
/// </summary>
public static class ChordParser
{
    private static readonly Dictionary<string, ButtonId> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["A"] = ButtonId.A,
        ["B"] = ButtonId.B,
        ["X"] = ButtonId.X,
        ["Y"] = ButtonId.Y,
        ["LB"] = ButtonId.LB,
        ["RB"] = ButtonId.RB,
        ["BACK"] = ButtonId.Back,
        ["START"] = ButtonId.Start,
        ["L3"] = ButtonId.L3,
        ["R3"] = ButtonId.R3,
        ["LT"] = ButtonId.LT,
        ["RT"] = ButtonId.RT,
        ["DPAD_UP"] = ButtonId.DpadUp,
        ["DPADUP"] = ButtonId.DpadUp,
        ["DPAD_DOWN"] = ButtonId.DpadDown,
        ["DPADDOWN"] = ButtonId.DpadDown,
        ["DPAD_LEFT"] = ButtonId.DpadLeft,
        ["DPADLEFT"] = ButtonId.DpadLeft,
        ["DPAD_RIGHT"] = ButtonId.DpadRight,
        ["DPADRIGHT"] = ButtonId.DpadRight,
    };

    public static bool TryParse(
        string chord,
        out ImmutableHashSet<ButtonId> modifiers,
        out ButtonId trigger,
        out string? error)
    {
        modifiers = ImmutableHashSet<ButtonId>.Empty;
        trigger = default;
        error = null;

        if (string.IsNullOrWhiteSpace(chord))
        {
            error = "chord is empty";
            return false;
        }

        var tokens = chord.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            error = $"chord '{chord}' has no buttons";
            return false;
        }

        var buttons = new List<ButtonId>(tokens.Length);
        foreach (var token in tokens)
        {
            if (!Names.TryGetValue(token, out var button))
            {
                error = $"unknown button '{token}' in chord '{chord}'";
                return false;
            }
            buttons.Add(button);
        }

        if (buttons.Distinct().Count() != buttons.Count)
        {
            error = $"duplicate button in chord '{chord}'";
            return false;
        }

        trigger = buttons[^1];
        modifiers = buttons.Take(buttons.Count - 1).ToImmutableHashSet();
        return true;
    }
}
