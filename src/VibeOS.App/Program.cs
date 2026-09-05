using Microsoft.Win32;
using VibeOS.App.Input;
using VibeOS.App.Pointer;
using VibeOS.App.Windows;
using VibeOS.Core;
using VibeOS.Core.Input;
using VibeOS.Core.Pointer;

namespace VibeOS.App;

internal static class Program
{
    private const int MasterToggleHoldMs = 1000;
    private const int HoldThresholdMs = 200;
    private const int DoubleTapWindowMs = 300;
    private const int PollIntervalMs = 8;   // ~125 Hz

    private static SyntheticInputLedger _ledger = null!;
    private static SendInputInjector _injector = null!;
    private static PointerEngine _pointer = null!;
    private static HoldDetector _holds = null!;

    private static SystemState _state = SystemState.Active;
    private static long? _masterArmedAt;
    private static bool _masterLatched;

    private static int Main()
    {
        _ledger = new SyntheticInputLedger();
        _injector = new SendInputInjector(_ledger);
        _pointer = new PointerEngine(_injector, PointerSettings.Default);
        _holds = new HoldDetector(HoldThresholdMs, DoubleTapWindowMs);

        // Safety wiring (PRD §47): every path that could strand a held input.
        AppDomain.CurrentDomain.UnhandledException += (_, _) => PanicRelease("unhandled exception");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => PanicRelease("process exit");
        Console.CancelKeyPress += (_, _) => PanicRelease("ctrl+c");
        SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect)
                PanicRelease("session lock");
        };

        using var pad = new Sdl3GamepadSource();
        if (!pad.Initialize())
        {
            Console.Error.WriteLine("[VibeOS] SDL init failed.");
            return 1;
        }

        pad.ConnectionChanged += connected =>
        {
            if (!connected) PanicRelease("controller disconnected");
        };

        _pointer.Start();
        PrintBanner();

        var previous = ControllerSnapshot.Empty;

        while (true)
        {
            var snapshot = pad.Poll();
            var now = snapshot.TimestampMs;

            TrackEdges(previous, snapshot, now);
            HandleMasterToggle(snapshot, now);

            if (_state == SystemState.Active)
            {
                // Left stick = cursor, right stick = scroll, both live at once (spec §5.4).
                _pointer.UpdateInput(
                    cursorStick: snapshot.LeftStick,
                    scrollStick: snapshot.RightStick,
                    precision: snapshot.IsDown(ButtonId.LB));

                MouseEdge(previous, snapshot, ButtonId.RT, MouseButton.Left);
                MouseEdge(previous, snapshot, ButtonId.LT, MouseButton.Right);
            }
            else
            {
                _pointer.UpdateInput(StickVector.Zero, StickVector.Zero, precision: false);
            }

            previous = snapshot;
            Thread.Sleep(PollIntervalMs);
        }
    }

    private static void PrintBanner()
    {
        Console.WriteLine("=== VibeOS ===");
        Console.WriteLine("  Left stick  : cursor      Right stick : scroll");
        Console.WriteLine("  RT          : left click  LT          : right click");
        Console.WriteLine("  LB (hold)   : precision   Back+Start  : suspend/resume (1s)");
        Console.WriteLine();
        Console.WriteLine("[VibeOS] Active. Ctrl+C to quit.");
    }

    private static void TrackEdges(ControllerSnapshot previous, ControllerSnapshot current, long now)
    {
        foreach (var b in current.Pressed)
            if (!previous.IsDown(b)) _holds.Press(b, now);

        foreach (var b in previous.Pressed)
            if (!current.IsDown(b)) _holds.Release(b, now);

        // Drain hold events so HoldDetector state stays current. M4 consumes these.
        _holds.Poll(now);
    }

    /// <summary>
    /// Back+Start held together for the master threshold (PRD §6). Latches so a
    /// continued hold toggles once, not once per second.
    /// </summary>
    private static void HandleMasterToggle(ControllerSnapshot snapshot, long now)
    {
        var bothDown = snapshot.IsDown(ButtonId.Back) && snapshot.IsDown(ButtonId.Start);

        if (!bothDown)
        {
            _masterArmedAt = null;
            _masterLatched = false;
            return;
        }

        if (_masterLatched) return;

        _masterArmedAt ??= now;
        if ((now - _masterArmedAt.Value) < MasterToggleHoldMs) return;

        _masterLatched = true;
        ToggleState();
    }

    private static void ToggleState()
    {
        _state = _state == SystemState.Active ? SystemState.Suspended : SystemState.Active;
        Console.WriteLine($"[VibeOS] {_state}");

        // Suspending must release every synthetic held input immediately (PRD §6).
        PanicRelease("state change");
        _pointer.SetEnabled(_state == SystemState.Active);
    }

    private static void MouseEdge(
        ControllerSnapshot previous, ControllerSnapshot current,
        ButtonId button, MouseButton mouse)
    {
        var isDown = current.IsDown(button);
        var wasDown = previous.IsDown(button);

        if (isDown && !wasDown) _injector.MouseDown(mouse);
        else if (!isDown && wasDown) _injector.MouseUp(mouse);
    }

    /// <summary>
    /// Releases every synthetic input and clears pending hold state. Always
    /// safe to call; only logs when something was actually released, so routine
    /// state changes stay quiet.
    /// </summary>
    private static void PanicRelease(string reason)
    {
        var hadHeldInput = _ledger.IsAnythingHeld;
        if (hadHeldInput)
            Console.WriteLine($"[VibeOS] Releasing all synthetic input ({reason}).");

        // Both of these must run unconditionally — the log is the only optional part.
        _ledger.ReleaseAll();
        _holds.Reset();
    }
}
