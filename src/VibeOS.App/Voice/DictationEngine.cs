using System.Diagnostics;
using VibeOS.App.Actions;

namespace VibeOS.App.Voice;

/// <summary>
/// One-utterance dictation pipeline (voice plan §1): transcribe on GPU,
/// optionally clean via Ollama, insert at the caret, submit on the internal
/// transcript-ready event. Serialized — one utterance at a time.
/// </summary>
public sealed class DictationEngine
{
    private readonly MicCapture _mic;
    private readonly LocalTranscriber _transcriber;
    private readonly CleanupClient _cleanup;
    private readonly TextInserter _inserter;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DictationEngine(
        MicCapture mic,
        LocalTranscriber transcriber,
        CleanupClient cleanup,
        TextInserter inserter,
        Action<string> log)
    {
        _mic = mic;
        _transcriber = transcriber;
        _cleanup = cleanup;
        _inserter = inserter;
        _log = log;
    }

    public void PrefetchModel()
    {
        _ = Task.Run(async () =>
        {
            try { await _transcriber.EnsureModelAsync(); }
            catch (Exception ex) { _log($"[VibeOS] STT model prefetch failed: {ex.Message}"); }
        });
    }

    public async Task RunAsync(
        float[] audio,
        string prompt,
        bool cleanup,
        KeyGesture? submit,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var sw = Stopwatch.StartNew();
            var raw = await _transcriber.TranscribeAsync(audio, prompt, ct);
            var sttMs = sw.ElapsedMilliseconds;

            if (string.IsNullOrWhiteSpace(raw))
            {
                _log("[VibeOS] Voice: heard nothing.");
                return;
            }

            string text = raw;
            if (cleanup)
                text = await _cleanup.CleanAsync(raw, ct);

            ct.ThrowIfCancellationRequested();
            _inserter.Insert(text, submit);
            _log($"[VibeOS] Voice: inserted {text.Length} chars " +
                 $"(stt {sttMs} ms{(cleanup ? " + cleanup" : "")}" +
                 $"{(submit is not null ? " + submit" : "")}).");
        }
        finally { _gate.Release(); }
    }
}
