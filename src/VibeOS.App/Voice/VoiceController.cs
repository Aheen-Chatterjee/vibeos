using VibeOS.App.Actions;
using VibeOS.App.Windows;
using VibeOS.Core.Input;

namespace VibeOS.App.Voice;

/// <summary>
/// Voice input, both phases (PRD §24–28):
///
/// Phase 1 (M5, always available): OpenWhispr hotkey bridge (PRD §25).
/// <code>
/// Y DOWN → hotkey DOWN at 0 ms (never delayed for tap discrimination)
/// Y UP   → hotkey UP (a quick tap records ~nothing and is discarded)
/// B      → cancel the in-flight session if not yet submitted (PRD §28)
/// </code>
/// Phase 2 (M8, when the fork's IPC is configured): RB+Y starts an IPC
/// session; Y release stops dictation; the submit key fires only after the
/// <c>inserted</c> event — never on a timer (PRD §27).
/// </summary>
public sealed class VoiceController
{
    private enum State { Idle, Bridging, Ipc }

    private readonly SendInputInjector _injector;
    private readonly Action<ushort, ushort, uint> _rumble;
    private readonly Action<string> _log;

    private KeyGesture _hotkey;
    private OpenWhisprIpcClient? _ipc;
    private readonly object _gate = new();
    private State _state = State.Idle;
    private string? _ipcSession;
    private CancellationTokenSource? _ipcCts;
    private bool _earlyRelease;

    public VoiceController(
        SendInputInjector injector,
        KeyGesture hotkey,
        Action<ushort, ushort, uint> rumble,
        Action<string> log)
    {
        _injector = injector;
        _hotkey = hotkey;
        _rumble = rumble;
        _log = log;
    }

    public bool IsRecording
    {
        get { lock (_gate) return _state != State.Idle; }
    }

    public void SetHotkey(KeyGesture hotkey)
    {
        lock (_gate) _hotkey = hotkey;
    }

    public void SetIpc(OpenWhisprIpcClient? ipc)
    {
        lock (_gate) _ipc = ipc;
    }

    /// <summary>Y pressed with no modifiers: start recording immediately.</summary>
    public void Press()
    {
        KeyGesture hotkey;
        lock (_gate)
        {
            if (_state != State.Idle) return;
            _state = State.Bridging;
            hotkey = _hotkey;
        }
        _injector.ChordDown(hotkey.Modifiers, hotkey.Key);
        _rumble(0x2000, 0x4000, 80);
        _log("[VibeOS] Voice: recording… (release Y to dictate, B to cancel)");
    }

    /// <summary>RB+Y hold (or any voice-* action): start if not already going.</summary>
    public void EnsureRecording()
    {
        if (!IsRecording) Press();
    }

    /// <summary>
    /// Voice+submit via IPC (PRD §27). Falls back to the PTT bridge when the
    /// fork is not configured. The submit key fires only on <c>inserted</c>.
    /// </summary>
    public void SubmitViaIpc(KeyGesture submitKey)
    {
        OpenWhisprIpcClient? ipc;
        lock (_gate)
        {
            if (_state != State.Idle) return;
            ipc = _ipc;
        }

        if (ipc is null || !ipc.IsConfigured)
        {
            EnsureRecording();
            return;
        }

        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            if (_state != State.Idle) { cts.Dispose(); return; }
            _state = State.Ipc;
            _ipcCts = cts;
        }
        _rumble(0x2000, 0x4000, 80);
        _log("[VibeOS] Voice+submit: recording… (release Y, then Enter fires on insert)");
        _ = RunIpcSessionAsync(ipc, submitKey, cts);
    }

    private async Task RunIpcSessionAsync(
        OpenWhisprIpcClient ipc, KeyGesture submitKey, CancellationTokenSource cts)
    {
        var session = await ipc.StartDictationAsync(cts.Token);
        if (session is null)
        {
            lock (_gate)
            {
                if (_ipcCts == cts) { _state = State.Idle; _ipcCts = null; }
            }
            cts.Dispose();
            return;
        }

        bool releasedEarly;
        lock (_gate)
        {
            _ipcSession = session;
            releasedEarly = _earlyRelease;
            _earlyRelease = false;
        }

        // Y was released while start was in flight: stop now; the insert
        // event still drives submit, which is exactly the desired semantics.
        if (releasedEarly)
            _ = ipc.StopDictationAsync(session);

        await foreach (var (type, _) in ipc.WatchEventsAsync(session, TimeSpan.FromSeconds(60), cts.Token))
        {
            if (type == "inserted")
            {
                _log("[VibeOS] Voice: transcript inserted — submitting.");
                if (submitKey.Modifiers.Count == 0) _injector.KeyTap(submitKey.Key);
                else _injector.SendChord(submitKey.Modifiers, submitKey.Key);
                _rumble(0x1000, 0x1000, 60);
            }
            else if (type == "failed")
            {
                _log("[VibeOS] Voice: dictation failed — nothing submitted.");
                _rumble(0x6000, 0x6000, 120);
            }
        }

        lock (_gate)
        {
            if (_ipcCts == cts) { _state = State.Idle; _ipcSession = null; _ipcCts = null; }
        }
        cts.Dispose();
    }

    /// <summary>
    /// Y released: stop the hotkey (bridge), or stop dictation and wait for
    /// the insert event (IPC). A tap is discarded, never delayed.
    /// </summary>
    public void Release(HoldOutcome outcome)
    {
        KeyGesture? hotkey = null;
        OpenWhisprIpcClient? ipc = null;
        string? session = null;
        lock (_gate)
        {
            if (_state == State.Idle) return;
            if (_state == State.Ipc)
            {
                ipc = _ipc;
                session = _ipcSession;
                if (session is null)
                    _earlyRelease = true; // start still in flight; stop on arrival
            }
            else
            {
                _state = State.Idle;
                hotkey = _hotkey;
            }
        }

        if (ipc is not null)
        {
            // The event loop finishes the session (insert → submit).
            if (session is not null) _ = ipc.StopDictationAsync(session);
            _log("[VibeOS] Voice: dictating…");
            return;
        }

        if (hotkey is not null)
        {
            _injector.ChordUp(hotkey.Modifiers, hotkey.Key);
            _rumble(0x1000, 0x1000, 60);
        }
        _log(outcome == HoldOutcome.Tap
            ? "[VibeOS] Voice: tap discarded."
            : "[VibeOS] Voice: dictating…");
    }

    /// <summary>B pressed mid-dictation: stop before anything is submitted.</summary>
    public void Cancel()
    {
        KeyGesture? hotkey = null;
        OpenWhisprIpcClient? ipc = null;
        string? session = null;
        CancellationTokenSource? cts = null;
        lock (_gate)
        {
            if (_state == State.Idle) return;
            if (_state == State.Ipc)
            {
                ipc = _ipc;
                session = _ipcSession;
                cts = _ipcCts;
                _state = State.Idle;
                _ipcSession = null;
                _ipcCts = null;
                _earlyRelease = false;
            }
            else
            {
                _state = State.Idle;
                hotkey = _hotkey;
            }
        }

        cts?.Cancel();
        cts?.Dispose();
        if (ipc is not null && session is not null) _ = ipc.CancelDictationAsync(session);
        if (hotkey is not null) _injector.ChordUp(hotkey.Modifiers, hotkey.Key);
        _rumble(0x6000, 0x6000, 120);
        _log("[VibeOS] Voice: cancelled.");
    }

    /// <summary>Unconditional stop: suspend, disconnect, shutdown (PRD §47).</summary>
    public void ForceStop()
    {
        KeyGesture? hotkey = null;
        OpenWhisprIpcClient? ipc = null;
        string? session = null;
        CancellationTokenSource? cts = null;
        lock (_gate)
        {
            if (_state == State.Ipc)
            {
                ipc = _ipc;
                session = _ipcSession;
                cts = _ipcCts;
            }
            else if (_state == State.Bridging)
            {
                hotkey = _hotkey;
            }
            _state = State.Idle;
            _ipcSession = null;
            _ipcCts = null;
            _earlyRelease = false;
        }

        cts?.Cancel();
        cts?.Dispose();
        if (ipc is not null && session is not null) _ = ipc.CancelDictationAsync(session);
        if (hotkey is not null) _injector.ChordUp(hotkey.Modifiers, hotkey.Key);
    }
}
