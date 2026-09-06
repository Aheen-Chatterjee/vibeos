using NAudio.Wave;

namespace VibeOS.App.Voice;

/// <summary>
/// Microphone capture for dictation (voice plan §3). The device opens on first
/// use and **stays open** — open cost (~100–300 ms) is the classic PTT latency
/// killer. 16 kHz mono float, the format whisper consumes natively.
/// A 90 s session cap bounds memory; hold-to-talk is the only gate.
/// </summary>
public sealed class MicCapture : IDisposable
{
    private const int SampleRate = 16000;
    private const int MaxSeconds = 90;

    private readonly Action<string> _log;
    private readonly object _gate = new();
    private WaveInEvent? _device;
    private List<float>? _session;
    private bool _micErrorLogged;
    private bool _disposed;

    public MicCapture(Action<string> log)
    {
        _log = log;
    }

    public bool IsOpen
    {
        get { lock (_gate) return _device is not null; }
    }

    /// <summary>Opens the device (once) and starts a fresh session buffer.</summary>
    public void BeginSession()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _session = new List<float>(SampleRate * 10);
            if (_device is not null) return;

            try
            {
                if (WaveInEvent.DeviceCount == 0)
                    throw new InvalidOperationException("no capture devices");
                _device = new WaveInEvent
                {
                    DeviceNumber = 0,
                    WaveFormat = new WaveFormat(SampleRate, 16, 1),
                };
                _device.DataAvailable += OnData;
                _device.StartRecording();
                _log("[VibeOS] Mic open (16 kHz, stays open while VibeOS runs).");
            }
            catch (Exception ex)
            {
                _device?.Dispose();
                _device = null;
                if (!_micErrorLogged)
                {
                    _micErrorLogged = true;
                    _log($"[VibeOS] Mic blocked ({ex.Message}) — Settings → Privacy → Microphone → desktop apps on.");
                }
            }
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        lock (_gate)
        {
            if (_session is null) return;
            if (_session.Count >= SampleRate * MaxSeconds) return; // cap; engine notes truncation
            for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
                _session.Add(BitConverter.ToInt16(e.Buffer, i) / 32768f);
        }
    }

    /// <summary>Takes the session audio and clears the buffer.</summary>
    public float[] EndSession()
    {
        lock (_gate)
        {
            var audio = _session?.ToArray() ?? Array.Empty<float>();
            _session = null;
            return audio;
        }
    }

    public void AbandonSession()
    {
        lock (_gate) _session = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _device?.Dispose();
            _device = null;
        }
    }
}
