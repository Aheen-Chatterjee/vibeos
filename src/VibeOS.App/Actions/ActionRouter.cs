using VibeOS.App.Windows;

namespace VibeOS.App.Actions;

/// <summary>
/// Maps action ids to synthetic input. Two kinds of actions:
/// <list type="bullet">
/// <item>Named actions (copy, save, next-tab, …) — the built-in table below,
/// defined once so global and per-app profiles share one vocabulary.</item>
/// <item>Raw key gestures ("CTRL+P") used directly in a binding — handled by
/// <see cref="ProfileManager"/> resolving them at load time.</item>
/// </list>
/// Voice actions are stubs until M5 owns them; they log rather than silently
/// doing nothing so a misconfigured build is loud.
/// </summary>
public sealed class ActionRouter
{
    private readonly SendInputInjector _injector;
    private readonly Action<string> _log;

    private static readonly Dictionary<string, string> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
    {
        // Editing (PRD §19, §44)
        ["copy"] = "CTRL+C",
        ["paste"] = "CTRL+V",
        ["cut"] = "CTRL+X",
        ["select-all"] = "CTRL+A",
        ["undo"] = "CTRL+Z",
        ["redo"] = "CTRL+Y",

        // Files / app commands
        ["save"] = "CTRL+S",
        ["find"] = "CTRL+F",
        ["quick-open"] = "CTRL+P",
        ["cmd-palette"] = "CTRL+SHIFT+P",
        ["new-tab"] = "CTRL+T",
        ["reopen-tab"] = "CTRL+SHIFT+T",
        ["address-bar"] = "CTRL+L",

        // Browser / editor tabs (also the M4 "tab-switch" gate)
        ["next-tab"] = "CTRL+TAB",
        ["prev-tab"] = "CTRL+SHIFT+TAB",

        // Window / shell
        ["task-switch"] = "ALT+TAB",
        ["show-desktop"] = "WIN+D",
        ["clipboard-history"] = "WIN+V",
        ["screenshot"] = "WIN+SHIFT+S",
        ["prev-desktop"] = "WIN+CTRL+LEFT",
        ["next-desktop"] = "WIN+CTRL+RIGHT",

        // Bare keys (base buttons, keyboard overlay reuse, nav wheel)
        ["enter"] = "ENTER",
        ["escape"] = "ESCAPE",
        ["tab"] = "TAB",
        ["space"] = "SPACE",
        ["backspace"] = "BACKSPACE",
        ["up"] = "UP",
        ["down"] = "DOWN",
        ["left"] = "LEFT",
        ["right"] = "RIGHT",
        ["home"] = "HOME",
        ["end"] = "END",
        ["pageup"] = "PAGEUP",
        ["pagedown"] = "PAGEDOWN",
    };

    /// <summary>App-launch actions for wheel slots (spec §5.9).</summary>
    private static readonly Dictionary<string, string> LaunchTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["launch-chrome"] = "chrome.exe",
        ["launch-edge"] = "msedge.exe",
        ["launch-vscode"] = "Code.exe",
        ["launch-cursor"] = "Cursor.exe",
        ["launch-terminal"] = "wt.exe",
        ["launch-explorer"] = "explorer.exe",
    };

    private readonly Dictionary<string, KeyGesture> _gestures;

    public ActionRouter(SendInputInjector injector, Action<string> log)
    {
        _injector = injector;
        _log = log;
        _gestures = new Dictionary<string, KeyGesture>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, gesture) in BuiltIn)
        {
            if (!KeyGesture.TryParse(gesture, out var parsed, out var error) || parsed is null)
                throw new InvalidOperationException($"Built-in action '{id}' is invalid: {error}");
            _gestures[id] = parsed;
        }
    }

    public static bool IsBuiltIn(string actionId) =>
        BuiltIn.ContainsKey(actionId) ||
        LaunchTargets.ContainsKey(actionId) ||
        Core.Input.StickyModifiers.IsStickyAction(actionId) ||
        string.Equals(actionId, "none", StringComparison.OrdinalIgnoreCase);

    public static bool IsVoiceStub(string actionId) =>
        actionId.StartsWith("voice-", StringComparison.OrdinalIgnoreCase);

    public bool TryGetGesture(string actionId, out KeyGesture gesture) =>
        _gestures.TryGetValue(actionId, out gesture!);

    private readonly Core.Input.StickyModifiers _sticky = new();

    /// <summary>Currently latched sticky modifiers (for status display).</summary>
    public bool IsStickyLatched(string actionId) => _sticky.IsLatched(actionId);

    /// <summary>Display names of live latches, e.g. ["Shift"].</summary>
    public IReadOnlyList<string> LatchedNames() => _sticky.PeekAll();

    /// <summary>Executes a named action or voice stub. Returns false if unknown.</summary>
    public bool Execute(string actionId)
    {
        // Explicit swallow for per-app overrides (e.g. XAML apps like Windows
        // Terminal drive themselves from the pad natively; VibeOS stays out).
        if (string.Equals(actionId, "none", StringComparison.OrdinalIgnoreCase))
            return true;

        // Sticky modifier toggle (Keys wheel). Returns whether now latched so
        // the caller can tick/confirm.
        if (Core.Input.StickyModifiers.IsStickyAction(actionId))
        {
            LatchedChanged?.Invoke(actionId, _sticky.Toggle(actionId));
            return true;
        }

        if (_gestures.TryGetValue(actionId, out var gesture))
        {
            ExecuteGesture(gesture);
            return true;
        }

        if (LaunchTargets.TryGetValue(actionId, out var exe))
        {
            Launch(exe);
            return true;
        }

        if (IsVoiceStub(actionId))
        {
            _log($"[VibeOS] Action '{actionId}' not yet wired (M5 voice).");
            return true;
        }

        return false;
    }

    /// <summary>Fired on sticky toggle (action id, now latched).</summary>
    public event Action<string, bool>? LatchedChanged;

    public void ExecuteGesture(KeyGesture gesture)
    {
        // Sticky modifiers (Keys wheel) prepend one-shot, then clear — so
        // Shift-tap then LB+X yields Shift+Ctrl+X.
        var latched = _sticky.TakeAll();
        if (latched.Count == 0)
        {
            if (gesture.Modifiers.Count == 0) _injector.KeyTap(gesture.Key);
            else _injector.SendChord(gesture.Modifiers, gesture.Key);
            return;
        }

        var mods = latched.Select(StickyToKey).Concat(gesture.Modifiers).Distinct().ToList();
        if (gesture.Key == VirtualKey.None && mods.Count > 0)
        {
            // No key to carry the latch — re-latch by tapping? No: a bare
            // sticky with no key is meaningless; drop it loudly.
            _log($"[VibeOS] Sticky {string.Join("+", latched)} had no key to modify — cleared.");
            return;
        }
        _injector.SendChord(mods, gesture.Key);
    }

    private static VirtualKey StickyToKey(string actionId) => actionId.ToLowerInvariant() switch
    {
        "sticky-shift" => VirtualKey.Shift,
        "sticky-ctrl" => VirtualKey.Control,
        "sticky-win" => VirtualKey.LWin,
        "sticky-alt" => VirtualKey.Alt,
        _ => throw new ArgumentOutOfRangeException(nameof(actionId)),
    };

    private void Launch(string exe) => Windows.WindowManager.FocusOrLaunch(exe, _log);
}
