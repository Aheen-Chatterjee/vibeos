using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace VibeOS.App.Windows;

/// <summary>Identity of the currently focused application (PRD §20).</summary>
public sealed record ForegroundApp(IntPtr Hwnd, int ProcessId, string ProcessName, string WindowTitle)
{
    public static readonly ForegroundApp None = new(IntPtr.Zero, 0, string.Empty, string.Empty);
}

/// <summary>
/// Polls the foreground window and reports which application owns it, so
/// profiles can switch (PRD §20, §37).
///
/// Polling rather than a WinEvent hook: PRD §66 allows 100 ms for profile
/// switch detection, and polling avoids adding a hook that could be blocked by
/// a wedged application. The process name is cached per HWND so the common case
/// costs one Win32 call.
/// </summary>
public sealed class ForegroundWatcher
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    private readonly Dictionary<IntPtr, (int Pid, string Name)> _cache = new();

    private ForegroundApp _current = ForegroundApp.None;

    /// <summary>Fired when the owning application changes (not merely the title).</summary>
    public event Action<ForegroundApp>? Changed;

    public ForegroundApp Current => _current;

    public void Poll()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        if (!_cache.TryGetValue(hwnd, out var info))
        {
            info = Resolve(hwnd);
            // Bound the cache; HWNDs are recycled and this runs forever.
            if (_cache.Count > 256) _cache.Clear();
            _cache[hwnd] = info;
        }

        if (info.Pid == 0) return;

        // Only raise Changed when the *application* changes. Switching between
        // two Chrome windows must not churn the profile.
        if (!string.Equals(info.Name, _current.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            _current = new ForegroundApp(hwnd, info.Pid, info.Name, ReadTitle(hwnd));
            Changed?.Invoke(_current);
        }
        else if (hwnd != _current.Hwnd)
        {
            _current = _current with { Hwnd = hwnd, WindowTitle = ReadTitle(hwnd) };
        }
    }

    private static (int Pid, string Name) Resolve(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return (0, string.Empty);

            using var proc = Process.GetProcessById((int)pid);
            // ProcessName has no ".exe"; profiles match on the full file name.
            return ((int)pid, proc.ProcessName + ".exe");
        }
        catch
        {
            // Process exited between the two calls, or access denied on a
            // protected process. Neither is worth failing over.
            return (0, string.Empty);
        }
    }

    private static string ReadTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        return GetWindowTextW(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }
}
