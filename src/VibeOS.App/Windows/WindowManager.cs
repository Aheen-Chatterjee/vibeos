using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace VibeOS.App.Windows;

/// <summary>
/// Window switching for wheel slots (user demand: selecting an app must focus
/// its open window, not spray new instances). Focus-or-launch: an existing
/// visible window owned by the exe comes forward (restored if minimized);
/// only when none exists is the exe started.
/// </summary>
public static class WindowManager
{
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private const int SW_RESTORE = 9;

    public sealed record OpenWindow(IntPtr Hwnd, string ProcessName, string Title);

    /// <summary>Top-down (z-order) visible windows with titles, sans ghosts.</summary>
    public static List<OpenWindow> ListWindows()
    {
        var result = new List<OpenWindow>();
        EnumWindows((hWnd, _) =>
        {
            try
            {
                if (!IsWindowVisible(hWnd)) return true;
                if (!GetWindowRect(hWnd, out var rect) || rect.Right - rect.Left <= 0 || rect.Bottom - rect.Top <= 0)
                    return true;
                var sb = new StringBuilder(256);
                if (GetWindowTextW(hWnd, sb, sb.Capacity) <= 0) return true;
                GetWindowThreadProcessId(hWnd, out var pid);
                string name;
                try { name = Process.GetProcessById((int)pid).ProcessName + ".exe"; }
                catch { return true; }
                result.Add(new OpenWindow(hWnd, name, sb.ToString()));
            }
            catch { }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>Focuses a known window handle. False when gone or refused.</summary>
    public static bool FocusWindow(IntPtr hwnd)
    {
        try
        {
            if (!IsWindow(hwnd) || !IsWindowVisible(hwnd)) return false;
            if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);

            // Foreground-lock workaround: attach to the foreground thread.
            GetWindowThreadProcessId(GetForegroundWindow(), out var fgThread);
            var ours = GetCurrentThreadId();
            var attached = false;
            if (fgThread != 0 && fgThread != ours)
                attached = AttachThreadInput(ours, fgThread, true);
            try { return SetForegroundWindow(hwnd); }
            finally { if (attached) AttachThreadInput(ours, fgThread, false); }
        }
        catch { return false; }
    }

    /// <summary>Focuses the exe's topmost window, or starts it when none.</summary>
    public static void FocusOrLaunch(string exe, Action<string> log)
    {
        try
        {
            var hit = ListWindows().FirstOrDefault(w =>
                string.Equals(w.ProcessName, exe, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                if (!FocusWindow(hit.Hwnd))
                    log($"[VibeOS] Could not focus {exe}.");
                return;
            }
        }
        catch (Exception ex)
        {
            log($"[VibeOS] Window lookup failed: {ex.Message}");
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            log($"[VibeOS] Launch {exe} failed: {ex.Message}");
        }
    }
}
