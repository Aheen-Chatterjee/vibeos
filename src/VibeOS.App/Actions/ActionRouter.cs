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

        // Bare keys (base buttons, keyboard overlay reuse)
        ["enter"] = "ENTER",
        ["escape"] = "ESCAPE",
        ["tab"] = "TAB",
        ["space"] = "SPACE",
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
        BuiltIn.ContainsKey(actionId) || LaunchTargets.ContainsKey(actionId);

    public static bool IsVoiceStub(string actionId) =>
        actionId.StartsWith("voice-", StringComparison.OrdinalIgnoreCase);

    /// <summary>Executes a named action or voice stub. Returns false if unknown.</summary>
    public bool Execute(string actionId)
    {
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

    public void ExecuteGesture(KeyGesture gesture)
    {
        if (gesture.Modifiers.Count == 0)
            _injector.KeyTap(gesture.Key);
        else
            _injector.SendChord(gesture.Modifiers, gesture.Key);
    }

    private void Launch(string exe)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _log($"[VibeOS] Launch {exe} failed: {ex.Message}");
        }
    }
}
