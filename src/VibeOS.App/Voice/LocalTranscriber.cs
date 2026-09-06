using System.Text;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace VibeOS.App.Voice;

/// <summary>
/// Local GPU transcription (voice plan §4). Whisper.net 1.9.x with the Vulkan
/// runtime (proven on this machine's RTX 4050 — zero extra installs, unlike
/// CUDA which needs toolkit DLLs). The model downloads once from HuggingFace
/// (no token needed) into %LOCALAPPDATA%\\VibeOS\\models and stays resident;
/// per-utterance processors are rebuilt cheaply so each profile dictionary
/// rides as the initial prompt.
/// </summary>
public sealed class LocalTranscriber : IDisposable
{
    private static readonly Dictionary<string, GgmlType> Models = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tiny.en"] = GgmlType.TinyEn,
        ["base.en"] = GgmlType.BaseEn,
        ["small.en"] = GgmlType.SmallEn,
    };

    private readonly string _modelsDir;
    private readonly string _model;
    private readonly string _language;
    private readonly Action<string> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WhisperFactory? _factory;
    private bool _backendLogged;
    private bool _disposed;

    static LocalTranscriber()
    {
        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Vulkan];
    }

    public LocalTranscriber(string modelsDir, string model, string language, Action<string> log)
    {
        _modelsDir = modelsDir;
        _model = Models.ContainsKey(model) ? model : "base.en";
        _language = string.IsNullOrEmpty(language) ? "en" : language;
        _log = log;
    }

    public string ModelName => _model;

    /// <summary>Downloads (first run) and loads the model. Safe to call often.</summary>
    public async Task EnsureModelAsync(CancellationToken ct = default)
    {
        if (_factory is not null || _disposed) return;
        await _gate.WaitAsync(ct);
        try
        {
            if (_factory is not null || _disposed) return;
            Directory.CreateDirectory(_modelsDir);
            var path = Path.Combine(_modelsDir, $"ggml-{_model}.bin");
            var part = path + ".part";
            if (!File.Exists(path))
            {
                // A killed run leaves a truncated .part — restart it, and only
                // move into place on completion so a partial file never loads.
                if (File.Exists(part)) File.Delete(part);
                _log($"[VibeOS] Downloading STT model {_model} (~{(await ModelBytesAsync(_model, ct) / 1048576):F0} MB, once)…");
                await DownloadAsync(_model, part, ct);
                File.Move(part, path);
            }
            _factory = WhisperFactory.FromPath(path);
            if (!_backendLogged)
            {
                _backendLogged = true;
                _log($"[VibeOS] STT ready: {_model} [{WhisperFactory.GetRuntimeInfo()}]");
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<string> TranscribeAsync(
        float[] audio, string? prompt, CancellationToken ct)
    {
        await EnsureModelAsync(ct);
        if (_factory is null || _disposed) return string.Empty;

        // One utterance at a time; processors are per-call (prompt varies).
        await _gate.WaitAsync(ct);
        try
        {
            using var processor = _factory.CreateBuilder()
                .WithLanguage(_language)
                .WithPrompt(prompt ?? string.Empty)
                .WithGreedySamplingStrategy()
                .Build();
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(audio, ct))
            {
                ct.ThrowIfCancellationRequested();
                text.Append(segment.Text);
            }
            return text.ToString().Trim();
        }
        finally { _gate.Release(); }
    }

    private static async Task<long> ModelBytesAsync(string model, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient();
            using var response = await http.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, ModelUrl(model)), ct);
            return response.Content.Headers.ContentLength ?? 0;
        }
        catch { return 0; }
    }

    private static string ModelUrl(string model) =>
        $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{model}.bin";

    private static async Task DownloadAsync(string model, string path, CancellationToken ct)
    {
        using var http = new HttpClient();
        using var response = await http.GetAsync(ModelUrl(model), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var net = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.OpenWrite(path);
        await net.CopyToAsync(file, ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _factory?.Dispose();
        _factory = null;
        _gate.Dispose();
    }
}
