using Microsoft.Win32;
using VibeOS.App.Actions;
using VibeOS.App.Input;
using VibeOS.App.Overlay;
using VibeOS.App.Pointer;
using VibeOS.App.Voice;
using VibeOS.App.Profiles;
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
    private static ProfileManager _profiles = null!;
    private static ActionRouter _router = null!;
    private static ForegroundWatcher _foreground = null!;
    private static ChordEngine _engine = new(Array.Empty<ChordDefinition>());
    private static WheelController _wheel = null!;
    private static RadialWheelOverlay _wheelOverlay = null!;
    private static VoiceController? _voice;
    private static MicCapture _mic = null!;
    private static TextInserter _inserter = null!;
    private static Action<ushort, ushort, uint>? _rumble;
    private static bool _rumbleFailed;
    private static KeyboardController _keyboard = null!;
    private static KeyboardOverlay _keyboardOverlay = null!;
    private static string? _lastApp;
    private static GamepadKeySuppressor _suppressor = null!;

    private static SystemState _state = SystemState.Active;
    private static long? _masterArmedAt;
    private static bool _masterLatched;
    private static Sdl3GamepadSource? _pad;
    private static Gui.TrayApp? _gui;
    private static Mutex? _instanceMutex;

    /// <summary>Broadcast for the GUI log view (console always prints too).</summary>
    public static event Action<string>? LogLine;

    /// <summary>Immutable snapshot for the status window. Cheap; polled at 2 Hz.</summary>
    public sealed record StatusSnapshot(
        string State,
        bool PadConnected,
        string PadName,
        string Profile,
        string Voice,
        string VoiceModel,
        bool WheelOpen,
        bool KeyboardOpen,
        string Sticky);

    public static StatusSnapshot GetStatus() => new(
        State: _state.ToString(),
        PadConnected: _pad?.IsConnected ?? false,
        PadName: _pad?.ConnectedName ?? "waiting for controller…",
        Profile: _foreground?.Current.ProcessName is string p && p.Length > 0 ? p : "—",
        Voice: _voice is null ? "off" : _voice.IsRecording ? "recording" : "idle",
        VoiceModel: _voice is not null && _voice.IsModelReady ? _voice.ModelName : "loading…",
        WheelOpen: _wheel?.IsOpen ?? false,
        KeyboardOpen: _keyboard?.IsOpen ?? false,
        Sticky: _router is null ? "—" : string.Join("+", _router.LatchedNames()));

    /// <summary>GUI requests (marshalled to the input thread implicitly — all
    /// operations below are thread-safe by construction).</summary>
    public static void RequestToggle() => ToggleState();

    public static void RequestReload()
    {
        _engine = new ChordEngine(_profiles.Chords);
        _voice?.UpdateConfig(_profiles.Voice);
        RefreshWheels(_foreground.Current.ProcessName);
        Log($"[VibeOS] Bindings reloaded: {_profiles.Chords.Count} chords.");
    }

    public static void RequestQuit()
    {
        PanicRelease("quit from GUI");
        Environment.Exit(0);
    }

    public static string ConfigDir => _profiles?.ConfigDirectory ?? string.Empty;

    private static int Main(string[] args)
    {
        using (_instanceMutex = new Mutex(true, @"Global\VibeOS-single-instance", out var owned))
        {
            if (!owned)
            {
                Console.Error.WriteLine("[VibeOS] Another instance is already running.");
                return 2;
            }
            return Run(args);
        }
    }

    private static int Run(string[] args)
    {
        _ledger = new SyntheticInputLedger();
        _injector = new SendInputInjector(_ledger);
        _pointer = new PointerEngine(_injector, PointerSettings.Default);
        _holds = new HoldDetector(HoldThresholdMs, DoubleTapWindowMs);
        _router = new ActionRouter(_injector, Log);
        _foreground = new ForegroundWatcher();

        var configDir = ResolveConfigDir();
        _profiles = new ProfileManager(configDir, Log);
        _engine = new ChordEngine(_profiles.Chords);
        var modelsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VibeOS", "models");
        _mic = new MicCapture(Log);
        _inserter = new TextInserter(_injector, Log);
        _voice = new VoiceController(
            _mic, _inserter, _profiles.Voice, modelsDir,
            (low, high, ms) => _rumble?.Invoke(low, high, ms), Log);
        _voice.PrefetchModel();
        Actions.ActionHints.Resolver = id =>
        {
            if (_profiles.GestureActions.TryGetValue(id, out var gesture)) return gesture;
            return _router.TryGetGesture(id, out var builtIn) ? builtIn : null;
        };
        _router.LatchedChanged += (_, latched) =>
        {
            if (latched) Input.Haptics.Confirm(_rumble);
            else Input.Haptics.Soft(_rumble);
            Log(latched ? "[VibeOS] Sticky on — next action consumes it." : "[VibeOS] Sticky off.");
        };
        _profiles.Reloaded += RequestReload;
        _wheel = new WheelController();
        _wheel.SetWheels(_profiles.Wheels);
        _wheelOverlay = new RadialWheelOverlay();
        _wheelOverlay.Start();
        _keyboard = new KeyboardController();
        _keyboardOverlay = new KeyboardOverlay();
        _keyboardOverlay.Start();
        _suppressor = new GamepadKeySuppressor();
        _suppressor.Start();
        _foreground.Changed += app =>
        {
            Log($"[VibeOS] Profile: {app.ProcessName}");
            RefreshWheels(app.ProcessName);
        };
        RefreshWheels(_foreground.Current.ProcessName);

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
        _pad = pad;

        pad.ConnectionChanged += connected =>
        {
            if (!connected) PanicRelease("controller disconnected");
        };
        _rumble = (low, high, ms) =>
        {
            if (!pad.Rumble(low, high, ms) && !_rumbleFailed)
            {
                _rumbleFailed = true;
                Log("[VibeOS] Pad reports no rumble support — haptics will be silent.");
            }
        };
        _wheel.Rumble = _rumble;
        _keyboard.Rumble = _rumble;

        // GUI (tray + status window) on its own STA thread.
        var startHidden = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
                                        a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        _gui = new Gui.TrayApp();
        _gui.Start(startHidden);
        if (startHidden) Gui.TrayApp.HideConsole();

        _pointer.Start();
        PrintBanner();
        if (!pad.IsConnected)
            Log("[VibeOS] No controller yet — connect it any time, VibeOS picks it up.");

        var previous = ControllerSnapshot.Empty;

        while (true)
        {
            var snapshot = pad.Poll();
            var now = snapshot.TimestampMs;

            _foreground.Poll();
            var app = _foreground.Current.ProcessName;
            if (string.IsNullOrEmpty(app)) app = null;
            _lastApp = app;

            // Hold tracking always runs so HoldDetector never goes stale,
            // even while the wheel owns the buttons.
            var edges = TrackHoldEdges(previous, snapshot, now);
            HandleMasterToggle(snapshot, now);

            string? wheelAction = null;
            if (_state == SystemState.Active)
            {
                wheelAction = _wheel.Update(previous, snapshot, now);
                _keyboard.Update(previous, snapshot, now, _injector);
            }
            else
            {
                _wheel.ForceClose();
                _keyboard.ForceClose();
            }

            if (wheelAction is not null)
                DispatchAction(wheelAction);
            _wheelOverlay.Update(_wheel.View);
            _keyboardOverlay.Update(_keyboard.View);

            // Overlay precedence (PRD §46): while the wheel or keyboard is
            // open the sticks freeze and chords stay silent.
            if (_state == SystemState.Active && !_wheel.SuppressInput && !_keyboard.IsOpen)
            {
                FireChords(snapshot, edges, app);

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
        Console.WriteLine("  LB (hold)   : precision   L3+R3 (1s)  : suspend/resume");
        Console.WriteLine("  LB+X/B/A   : copy/paste/select-all   RB+A/X/B : save/find/quick-open");
        Console.WriteLine("  LB+RB hold : radial wheel            RB+D-pad : prev/next tab");
        Console.WriteLine("  Y hold     : voice dictate (B cancels) RB+Y hold: voice+submit");
        Console.WriteLine("  D-pad up hold : virtual keyboard");
        Console.WriteLine("  A / B      : Enter / Escape");
        Console.WriteLine();
        Console.WriteLine("[VibeOS] Active. Hold L3+R3 for 1s to suspend. Ctrl+C to quit.");
    }

    private sealed record HoldEdges(
        List<ButtonId> Pressed,
        List<(ButtonId Button, HoldOutcome Outcome)> Released,
        List<ButtonId> HoldCrossed);

    /// <summary>
    /// Feeds the HoldDetector from every poll. Always runs — even suspended or
    /// while the wheel is open — so timing state never goes stale.
    /// </summary>
    private static HoldEdges TrackHoldEdges(
        ControllerSnapshot previous, ControllerSnapshot current, long now)
    {
        var pressed = new List<ButtonId>();
        foreach (var b in current.Pressed.OrderBy(b => b))
        {
            if (previous.IsDown(b)) continue;
            _holds.Press(b, now);
            pressed.Add(b);
        }

        var released = new List<(ButtonId, HoldOutcome)>();
        foreach (var b in previous.Pressed.OrderBy(b => b))
        {
            if (current.IsDown(b)) continue;
            released.Add((b, _holds.Release(b, now)));
        }

        return new HoldEdges(pressed, released, new List<ButtonId>(_holds.Poll(now)));
    }

    /// <summary>
    /// Central chord dispatch (PRD §45). Modifiers-first contract: a chord
    /// fires when its trigger is pressed while the modifiers are already held.
    /// Resolution happens at trigger time, so nothing double-dispatches and no
    /// base action leaks before the chord resolves. A press with modifiers held
    /// but no matching chord emits nothing.
    /// </summary>
    private static void FireChords(ControllerSnapshot current, HoldEdges edges, string? app)
    {
        // Every executed chord ticks (PRD §36). Wheel slots, voice actions and
        // keyboard typing carry their own haptics; mouse clicks stay silent.
        void Fire(ChordDefinition? hit)
        {
            if (hit is null) return;
            DispatchAction(hit.ActionId);
            Input.Haptics.Tick(_rumble);
        }

        // Press edges — Press-mode chords.
        foreach (var b in edges.Pressed)
        {
            // B mid-dictation cancels voice instead of escaping (PRD §28).
            if (b == ButtonId.B && (_voice?.IsRecording ?? false))
            {
                _voice?.Cancel();
                continue;
            }

            // Every Y press starts capture; release decides dictate vs tap.
            // RB+Y hold marks submit via the Hold-mode chord below.
            // (Voice owns Y's haptics, so it dispatches directly.)
            if (b == ButtonId.Y)
            {
                _voice?.Press();
                var yHit = _engine.Resolve(current.Pressed, b, ActivationMode.Press, app);
                if (yHit is not null) DispatchAction(yHit.ActionId);
                continue;
            }

            Fire(_engine.Resolve(current.Pressed, b, ActivationMode.Press, app));
        }

        // Release edges — Release-mode chords fire on tap releases only, so
        // "LB+Y tap → undo" and "Y hold → voice" can share a button (M5).
        foreach (var (b, outcome) in edges.Released)
        {
            if (b == ButtonId.Y)
                _voice?.Release(outcome, PromptFor(app), CleanupFor(app));

            if (outcome == HoldOutcome.Tap)
                Fire(_engine.Resolve(current.Pressed, b, ActivationMode.Release, app));
            else if (outcome == HoldOutcome.DoubleTap)
                Fire(_engine.Resolve(current.Pressed, b, ActivationMode.DoubleTap, app));
        }

        // Hold crossings — Hold-mode chords.
        foreach (var b in edges.HoldCrossed)
            Fire(_engine.Resolve(current.Pressed, b, ActivationMode.Hold, app));
    }

    /// <summary>
    /// Wheel 2 is the active app's wheel (spec §5.9); anything else falls back
    /// to the global set, and an empty wheel falls back to wheel 1.
    /// </summary>
    private static void RefreshWheels(string processName)
    {
        var globals = _profiles.Wheels;
        if (globals.Count == 0)
        {
            _wheel.SetWheels(_profiles.AppWheels.TryGetValue(processName, out var only)
                ? new[] { only }
                : Array.Empty<Wheel>());
            return;
        }

        // Splice the app wheel into slot 2, keeping every other global wheel.
        _wheel.SetWheels(_profiles.AppWheels.TryGetValue(processName, out var appWheel)
            ? globals.Take(1).Append(appWheel).Concat(globals.Skip(2)).ToList()
            : globals);
    }

    /// <summary>
    /// Submit key for voice+submit: per-app override, else Enter (PRD §27).
    /// </summary>
    private static KeyGesture SubmitGesture()
    {
        string? text = null;
        if (_lastApp is not null && _profiles.AppVoice.TryGetValue(_lastApp, out var voice))
            text = voice.SubmitKey;
        if (!KeyGesture.TryParse(text ?? "ENTER", out var gesture, out _) || gesture is null)
            KeyGesture.TryParse("ENTER", out gesture, out _);
        return gesture!;
    }

    /// <summary>Profile dictionary as the whisper prompt (M7).</summary>
    private static string PromptFor(string? app)
    {
        if (app is not null && _profiles.AppVoice.TryGetValue(app, out var voice) &&
            voice.Dictionary.Count > 0)
        {
            return string.Join(", ", voice.Dictionary);
        }
        return string.Empty;
    }

    /// <summary>Cleanup runs unless globally off or the app wants verbatim.</summary>
    private static bool CleanupFor(string? app)
    {
        if (!_profiles.Voice.Cleanup) return false;
        if (app is not null && _profiles.AppVoice.TryGetValue(app, out var voice))
            return voice.Polished;
        return true;
    }

    private static void DispatchAction(string actionId)
    {
        // RB+Y hold: submit after insert on this utterance (internal event).
        if (ActionRouter.IsVoiceStub(actionId))
        {
            _voice?.MarkSubmit(SubmitGesture());
            return;
        }

        if (_profiles.GestureActions.TryGetValue(actionId, out var gesture))
        {
            _router.ExecuteGesture(gesture);
            return;
        }

        if (!_router.Execute(actionId))
            Log($"[VibeOS] Unknown action '{actionId}' — check config.");
    }

    private static void Log(string message)
    {
        Console.WriteLine(message);
        try { LogLine?.Invoke(message); } catch { }
    }

    /// <summary>Finds config/vibeos.jsonc: cwd first (dev loop), then beside the exe.</summary>
    private static string ResolveConfigDir()
    {
        var candidates = new List<string>
        {
            Path.Combine(Directory.GetCurrentDirectory(), "config"),
            Path.Combine(AppContext.BaseDirectory, "config"),
        };

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 4 && dir?.Parent is not null; i++)
        {
            dir = dir.Parent;
            candidates.Add(Path.Combine(dir.FullName, "config"));
        }

        foreach (var c in candidates)
            if (File.Exists(Path.Combine(c, "vibeos.jsonc")))
                return c;

        return candidates[0];
    }

    /// <summary>
    /// L3+R3 (both stick clicks) held together for the master threshold.
    /// Latches so a continued hold toggles once, not once per second.
    ///
    /// History: this was Back+Start per PRD §6, but the Redgear pad reports
    /// Back+Start as the Xbox Guide button in firmware, which Windows claims
    /// for Game Bar / gamepad navigation before SDL ever sees a clean chord.
    /// L3+R3 is unused in the layout and not intercepted by Windows.
    /// </summary>
    private static void HandleMasterToggle(ControllerSnapshot snapshot, long now)
    {
        var bothDown = snapshot.IsDown(ButtonId.L3) && snapshot.IsDown(ButtonId.R3);

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
        _suppressor?.SetEnabled(_state == SystemState.Active);
        Input.Haptics.Toggle(_rumble);
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

        // All of these must run unconditionally — the log is the only optional part.
        _ledger.ReleaseAll();
        _holds.Reset();
        _wheel?.ForceClose();
        _keyboard?.ForceClose();
        _voice?.ForceStop();
    }
}
