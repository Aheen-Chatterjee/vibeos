using VibeOS.App.Actions;
using VibeOS.Core.Input;

namespace VibeOS.App.Voice;

/// <summary>
/// In-house voice dictation (voice plan §1): every Y press captures, release
/// transcribes + inserts, RB+Y adds submit on the internal transcript-ready
/// event, B cancels. No external backend, no IPC, no timers-as-Submit.
/// </summary>
public sealed class VoiceController : IDisposable
{
    private enum State { Idle, Capturing, Working }

    private const double MinSeconds = 0.3;

    private readonly MicCapture _mic;
    private readonly TextInserter _inserter;
    private readonly Profiles.VoiceConfig _baseConfig;
    private readonly string _modelsDir;
    private readonly Action<ushort, ushort, uint> _rumble;
    private readonly Action<string> _log;

    private readonly object _gate = new();
    private State _state = State.Idle;
    private KeyGesture? _pendingSubmit;
    private CancellationTokenSource? _cts;
    private Profiles.VoiceConfig? _pendingConfig;
    private bool _disposed;

    private DictationEngine _engine;
    private LocalTranscriber _transcriber;
    private CleanupClient _cleanup;

    public VoiceController(
        MicCapture mic,
        TextInserter inserter,
        Profiles.VoiceConfig config,
        string modelsDir,
        Action<ushort,ushort,uint> rumble,
        Action<string> log)
    {
        _mic = mic;
        _inserter = inserter;
        _baseConfig = config;
        _modelsDir = modelsDir;
        _rumble = rumble;
        _log = log;
        (_engine, _transcriber, _cleanup) = BuildStack(config);
    }

    public bool IsRecording
    {
        get { lock (_gate) return _state != State.Idle; }
    }

    public void PrefetchModel() => _engine.PrefetchModel();

    /// <summary>Applies new voice settings when idle, else after the utterance.</summary>
    public void UpdateConfig(Profiles.VoiceConfig config)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_state != State.Idle)
            {
                _pendingConfig = config;
                _log("[VibeOS] Voice settings apply after the current utterance.");
                return;
            }
            RebuildLocked(config);
        }
    }

    private (DictationEngine, LocalTranscriber, CleanupClient) BuildStack(Profiles.VoiceConfig cfg)
    {
        var transcriber = new LocalTranscriber(_modelsDir, cfg.Model, cfg.Language, _log);
        var cleanup = new CleanupClient(cfg.Ollama, cfg.CleanupModel, cfg.CleanupTimeoutMs, _log);
        return (new DictationEngine(_mic, transcriber, cleanup, _inserter, _log), transcriber, cleanup);
    }

    private void RebuildLocked(Profiles.VoiceConfig cfg)
    {
        _transcriber.Dispose();
        _cleanup.Dispose();
        (_engine, _transcriber, _cleanup) = BuildStack(cfg);
        _engine.PrefetchModel();
    }

    /// <summary>Every Y press starts (or keeps) capture.</summary>
    public void Press()
    {
        lock (_gate)
        {
            if (_disposed || _state != State.Idle) return;
            if (_pendingConfig is not null)
            {
                RebuildLocked(_pendingConfig);
                _pendingConfig = null;
            }
            _state = State.Capturing;
            _pendingSubmit = null;
        }
        _mic.BeginSession();
        _rumble(0x2000, 0x4000, 80);
        _log("[VibeOS] Voice: recording… (release Y to dictate, B to cancel)");
    }

    /// <summary>RB+Y hold: submit after insert on this utterance.</summary>
    public void MarkSubmit(KeyGesture submit)
    {
        lock (_gate)
        {
            if (_state != State.Capturing) return;
            _pendingSubmit = submit;
        }
        _log("[VibeOS] Voice+submit armed.");
    }

    /// <summary>
    /// Y released: taps discard; otherwise transcribe → insert (→ submit).
    /// </summary>
    public void Release(HoldOutcome outcome, string prompt, bool cleanup)
    {
        float[] audio;
        KeyGesture? submit;
        CancellationTokenSource? cts = null;
        lock (_gate)
        {
            if (_state != State.Capturing) return;
            audio = _mic.EndSession();
            submit = _pendingSubmit;
            _pendingSubmit = null;

            if (outcome == HoldOutcome.Tap || audio.Length < 16000 * MinSeconds)
            {
                _state = State.Idle;
            }
            else
            {
                _state = State.Working;
                _cts = new CancellationTokenSource();
                cts = _cts;
            }
        }

        if (outcome == HoldOutcome.Tap || audio.Length < 16000 * MinSeconds || cts is null)
        {
            _log("[VibeOS] Voice: tap discarded.");
            return;
        }

        _rumble(0x1000, 0x1000, 60);
        _log("[VibeOS] Voice: transcribing…");
        _ = RunAsync(audio, prompt, cleanup, submit, cts);
    }

    private async Task RunAsync(
        float[] audio, string prompt, bool cleanup, KeyGesture? submit, CancellationTokenSource cts)
    {
        try
        {
            await _engine.RunAsync(audio, prompt, cleanup, submit, cts.Token);
        }
        catch (OperationCanceledException) { _log("[VibeOS] Voice: cancelled."); }
        catch (Exception ex) { _log($"[VibeOS] Voice failed: {ex.Message}"); }
        finally
        {
            lock (_gate)
            {
                if (_cts == cts) { _state = State.Idle; _cts = null; }
            }
            cts.Dispose();
        }
    }

    /// <summary>B pressed mid-dictation: stop before anything is submitted.</summary>
    public void Cancel()
    {
        CancellationTokenSource? cts = null;
        lock (_gate)
        {
            if (_state == State.Idle || _disposed) return;
            if (_state == State.Capturing) _mic.AbandonSession();
            cts = _cts;
            _state = State.Idle;
            _pendingSubmit = null;
            _cts = null;
        }
        cts?.Cancel();
        cts?.Dispose();
        _rumble(0x6000, 0x6000, 120);
        _log("[VibeOS] Voice: cancelled.");
    }

    /// <summary>Unconditional stop: suspend, disconnect, shutdown (PRD §47).</summary>
    public void ForceStop()
    {
        CancellationTokenSource? cts = null;
        lock (_gate)
        {
            if (_state == State.Capturing) _mic.AbandonSession();
            cts = _cts;
            _state = State.Idle;
            _pendingSubmit = null;
            _cts = null;
        }
        cts?.Cancel();
        cts?.Dispose();
    }

    public void Dispose()
    {
        ForceStop();
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _transcriber.Dispose();
        _cleanup.Dispose();
    }
}
