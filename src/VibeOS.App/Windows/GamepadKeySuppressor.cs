using System.Runtime.InteropServices;

namespace VibeOS.App.Windows;

/// <summary>
/// Swallows Windows' OS-level gamepad-to-key translation (VK_GAMEPAD_*,
/// 0xC3–0xDA) via a low-level keyboard hook.
///
/// Background: Windows translates every XInput input into synthetic
/// VK_GAMEPAD_* key messages for the focused window, and XAML apps (Windows
/// Terminal, Settings, Store) consume them for native XY focus navigation +
/// A-invoke. That double-drives alongside VibeOS with no way to turn it off
/// per-app and no filter driver (PRD §53). These messages traverse the
/// WH_KEYBOARD_LL chain (proven with InputProbe), so a hook that returns
/// non-zero for exactly that range removes them before any app sees them.
///
/// Scope discipline: only 0xC3–0xDA is ever swallowed — VibeOS's own synthetic
/// keys are normal VKs and pass through untouched, as do real keyboard keys.
/// Disabled while Suspended so the pad behaves stock outside VibeOS.
/// Real games read XInput directly and are unaffected either way.
/// </summary>
public sealed class GamepadKeySuppressor : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const uint First = 0xC3;
    private const uint Last = 0xDA;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHook
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private Thread? _thread;
    private IntPtr _hook = IntPtr.Zero;
    private HookProc? _proc;
    private readonly ManualResetEventSlim _ready = new(false);
    private volatile bool _enabled = true;
    private bool _disposed;

    public void SetEnabled(bool enabled) => _enabled = enabled;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(() =>
        {
            _proc = Hook;
            GC.KeepAlive(_proc);
            _hook = SetWindowsHookEx(
                WH_KEYBOARD_LL, _proc,
                GetModuleHandle(System.Diagnostics.Process.GetCurrentProcess().MainModule?.ModuleName),
                0);
            _ready.Set();
            Application.Run();
        });
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Name = "VibeOS.KeySuppress";
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    private IntPtr Hook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _enabled && !_disposed)
        {
            var vk = Marshal.PtrToStructure<KbdLlHook>(lParam).vkCode;
            if (vk >= First && vk <= Last)
                return (IntPtr)1; // swallow: never reaches any app
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        _disposed = true;
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _ready.Dispose();
    }
}
