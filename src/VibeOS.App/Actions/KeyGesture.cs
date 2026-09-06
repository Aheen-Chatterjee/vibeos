namespace VibeOS.App.Actions;

/// <summary>
/// A parsed key gesture from configuration (PRD §57): modifiers + one key,
/// e.g. "CTRL+SHIFT+P". Case-insensitive. Executed via
/// <see cref="Windows.SendInputInjector.SendChord"/> (modifiers) or
/// <see cref="Windows.SendInputInjector.KeyTap"/> (bare key).
/// </summary>
public sealed record KeyGesture(IReadOnlyList<Windows.VirtualKey> Modifiers, Windows.VirtualKey Key)
{
    private static readonly Dictionary<string, Windows.VirtualKey> ModNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CTRL"] = Windows.VirtualKey.Control,
        ["CONTROL"] = Windows.VirtualKey.Control,
        ["SHIFT"] = Windows.VirtualKey.Shift,
        ["ALT"] = Windows.VirtualKey.Alt,
        ["WIN"] = Windows.VirtualKey.LWin,
        ["WINDOWS"] = Windows.VirtualKey.LWin,
    };

    private static readonly Dictionary<string, Windows.VirtualKey> KeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ENTER"] = Windows.VirtualKey.Enter,
        ["ESCAPE"] = Windows.VirtualKey.Escape,
        ["ESC"] = Windows.VirtualKey.Escape,
        ["TAB"] = Windows.VirtualKey.Tab,
        ["SPACE"] = Windows.VirtualKey.Space,
        ["BACKSPACE"] = Windows.VirtualKey.Back,
        ["BACK"] = Windows.VirtualKey.Back,
        ["DELETE"] = Windows.VirtualKey.Delete,
        ["DEL"] = Windows.VirtualKey.Delete,
        ["INSERT"] = Windows.VirtualKey.Insert,
        ["HOME"] = Windows.VirtualKey.Home,
        ["END"] = Windows.VirtualKey.End,
        ["PAGEUP"] = Windows.VirtualKey.PageUp,
        ["PAGEDOWN"] = Windows.VirtualKey.PageDown,
        ["UP"] = Windows.VirtualKey.Up,
        ["DOWN"] = Windows.VirtualKey.Down,
        ["LEFT"] = Windows.VirtualKey.Left,
        ["RIGHT"] = Windows.VirtualKey.Right,
    };

    public static bool TryParse(string gesture, out KeyGesture? result, out string? error)
    {
        result = null;
        error = null;

        if (string.IsNullOrWhiteSpace(gesture))
        {
            error = "gesture is empty";
            return false;
        }

        var tokens = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            error = $"gesture '{gesture}' has no keys";
            return false;
        }

        var modifiers = new List<Windows.VirtualKey>();
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (!ModNames.TryGetValue(tokens[i], out var mod))
            {
                error = $"unknown modifier '{tokens[i]}' in gesture '{gesture}'";
                return false;
            }
            modifiers.Add(mod);
        }

        var keyToken = tokens[^1];
        Windows.VirtualKey key;
        if (keyToken.Length == 1)
        {
            var c = char.ToUpperInvariant(keyToken[0]);
            if ((c < 'A' || c > 'Z') && (c < '0' || c > '9'))
            {
                error = $"unsupported key '{keyToken}' in gesture '{gesture}'";
                return false;
            }
            key = (Windows.VirtualKey)c;
        }
        else if (!KeyNames.TryGetValue(keyToken, out key) &&
                 !Enum.TryParse<Windows.VirtualKey>(keyToken, ignoreCase: true, out key))
        {
            error = $"unknown key '{keyToken}' in gesture '{gesture}'";
            return false;
        }

        if (!Enum.IsDefined(typeof(Windows.VirtualKey), key))
        {
            error = $"unknown key '{keyToken}' in gesture '{gesture}'";
            return false;
        }

        result = new KeyGesture(modifiers, key);
        return true;
    }

    public override string ToString() =>
        Modifiers.Count == 0 ? Key.ToString() : string.Join("+", Modifiers.Append(Key));
}
