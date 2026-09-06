using System.Runtime.InteropServices;
using static VibeOS.App.Windows.NativeMethods;

namespace VibeOS.App.Windows;

/// <summary>
/// All synthetic pointer output. Every down goes through the ledger so it can
/// be force-released on suspend, disconnect, lock or crash (PRD §47).
/// </summary>
public sealed class SendInputInjector
{
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();

    private readonly SyntheticInputLedger _ledger;

    public SendInputInjector(SyntheticInputLedger ledger)
    {
        _ledger = ledger;

        // The ledger asks for a release; emit it WITHOUT re-entering the ledger
        // (it has already cleared its own state by this point).
        _ledger.MouseReleaseRequested += button => SendMouseFlag(UpFlag(button), 0);
        _ledger.KeyReleaseRequested += vk => SendKeyRaw(vk, down: false);
    }

    // ---- Keyboard -------------------------------------------------------

    public void KeyDown(VirtualKey key)
    {
        _ledger.RegisterKeyDown((ushort)key);
        SendKeyRaw((ushort)key, down: true);
    }

    public void KeyUp(VirtualKey key)
    {
        _ledger.RegisterKeyUp((ushort)key);
        SendKeyRaw((ushort)key, down: false);
    }

    public void KeyTap(VirtualKey key)
    {
        KeyDown(key);
        KeyUp(key);
    }

    /// <summary>
    /// Presses modifiers, taps the key, then releases modifiers in reverse
    /// order. Every press is ledgered, so an exception or a suspend mid-chord
    /// still releases the modifiers (PRD §47 — no stuck Ctrl).
    /// </summary>
    public void SendChord(IReadOnlyList<VirtualKey> modifiers, VirtualKey key)
    {
        for (var i = 0; i < modifiers.Count; i++) KeyDown(modifiers[i]);
        try
        {
            if (key != VirtualKey.None) KeyTap(key);
        }
        finally
        {
            for (var i = modifiers.Count - 1; i >= 0; i--) KeyUp(modifiers[i]);
        }
    }

    private static void SendKeyRaw(ushort virtualKey, bool down)
    {
        Send(new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = down ? 0u : KEYEVENTF_KEYUP,
                    dwExtraInfo = GetMessageExtraInfo()
                }
            }
        });
    }

    public void MoveRelative(int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;

        Send(new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    dwFlags = MOUSEEVENTF_MOVE,
                    dwExtraInfo = GetMessageExtraInfo()
                }
            }
        });
    }

    public void MouseDown(MouseButton button)
    {
        _ledger.RegisterMouseDown(button);
        SendMouseFlag(DownFlag(button), 0);
    }

    public void MouseUp(MouseButton button)
    {
        _ledger.RegisterMouseUp(button);
        SendMouseFlag(UpFlag(button), 0);
    }

    /// <summary>Positive scrolls up / away from the user.</summary>
    public void ScrollVertical(int notches)
    {
        if (notches == 0) return;
        SendMouseFlag(MOUSEEVENTF_WHEEL, unchecked((uint)(notches * WHEEL_DELTA)));
    }

    /// <summary>Positive scrolls right.</summary>
    public void ScrollHorizontal(int notches)
    {
        if (notches == 0) return;
        SendMouseFlag(MOUSEEVENTF_HWHEEL, unchecked((uint)(notches * WHEEL_DELTA)));
    }

    private static uint DownFlag(MouseButton b) => b switch
    {
        MouseButton.Left => MOUSEEVENTF_LEFTDOWN,
        MouseButton.Right => MOUSEEVENTF_RIGHTDOWN,
        MouseButton.Middle => MOUSEEVENTF_MIDDLEDOWN,
        _ => throw new ArgumentOutOfRangeException(nameof(b))
    };

    private static uint UpFlag(MouseButton b) => b switch
    {
        MouseButton.Left => MOUSEEVENTF_LEFTUP,
        MouseButton.Right => MOUSEEVENTF_RIGHTUP,
        MouseButton.Middle => MOUSEEVENTF_MIDDLEUP,
        _ => throw new ArgumentOutOfRangeException(nameof(b))
    };

    private void SendMouseFlag(uint flags, uint mouseData)
    {
        Send(new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = flags,
                    mouseData = mouseData,
                    dwExtraInfo = GetMessageExtraInfo()
                }
            }
        });
    }

    private static void Send(INPUT input)
    {
        var arr = new[] { input };
        var sent = SendInput(1, arr, InputSize);
        if (sent == 0)
        {
            // UIPI blocks injection into higher-integrity windows (PRD §50).
            // Report, never crash the input loop.
            var err = Marshal.GetLastWin32Error();
            Console.Error.WriteLine(
                $"[VibeOS] SendInput blocked (win32 error {err}) - likely an elevated window has focus.");
        }
    }
}
