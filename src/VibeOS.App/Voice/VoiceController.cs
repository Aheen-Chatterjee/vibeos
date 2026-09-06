using VibeOS.App.Actions;
using VibeOS.App.Windows;
using VibeOS.Core.Input;

namespace VibeOS.App.Voice;

/// <summary>
/// Voice phase 1: OpenWhispr hotkey bridge (PRD §25). The OpenWhispr PTT
/// binding is configured to an uncommon synthetic hotkey (default
/// Ctrl+Shift+F11); VibeOS synthesizes that hotkey's down/up around the Y
/// button:
/// <code>
/// Y DOWN → hotkey DOWN at 0 ms (never delayed for tap discrimination)
/// Y UP   → hotkey UP (a quick tap records ~nothing and is discarded)
/// B      → cancel the in-flight session if not yet submitted (PRD §28)
/// </code>
/// Voice+submit (RB+Y, PRD §27) rides the same bridge in M5 — without the M8
/// IPC channel there is no TRANSCRIPT_INSERTED event, so submit-on-release is
/// phase-1 behaviour; M8 makes it event-driven.
/// </summary>
public sealed class VoiceController
{
    private readonly SendInputInjector _injector;
    private readonly Action<ushort, ushort, uint> _rumble;
    private readonly Action<string> _log;

    private KeyGesture _hotkey;
    private readonly object _gate = new();
    private bool _recording;

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
        get { lock (_gate) return _recording; }
    }

    public void SetHotkey(KeyGesture hotkey)
    {
        lock (_gate) _hotkey = hotkey;
    }

    /// <summary>Y pressed with no modifiers: start recording immediately.</summary>
    public void Press()
    {
        KeyGesture hotkey;
        lock (_gate)
        {
            if (_recording) return;
            _recording = true;
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

    /// <summary>Y released: stop the hotkey; a tap is discarded, never delayed.</summary>
    public void Release(HoldOutcome outcome)
    {
        KeyGesture hotkey;
        lock (_gate)
        {
            if (!_recording) return;
            _recording = false;
            hotkey = _hotkey;
        }
        _injector.ChordUp(hotkey.Modifiers, hotkey.Key);
        _rumble(0x1000, 0x1000, 60);
        _log(outcome == HoldOutcome.Tap
            ? "[VibeOS] Voice: tap discarded."
            : "[VibeOS] Voice: dictating…");
    }

    /// <summary>B pressed mid-dictation: stop before anything is submitted.</summary>
    public void Cancel()
    {
        KeyGesture hotkey;
        lock (_gate)
        {
            if (!_recording) return;
            _recording = false;
            hotkey = _hotkey;
        }
        _injector.ChordUp(hotkey.Modifiers, hotkey.Key);
        _rumble(0x6000, 0x6000, 120);
        _log("[VibeOS] Voice: cancelled.");
    }

    /// <summary>Unconditional stop: suspend, disconnect, shutdown (PRD §47).</summary>
    public void ForceStop()
    {
        KeyGesture? hotkey = null;
        lock (_gate)
        {
            if (_recording)
            {
                _recording = false;
                hotkey = _hotkey;
            }
        }
        if (hotkey is not null)
            _injector.ChordUp(hotkey.Modifiers, hotkey.Key);
    }
}
