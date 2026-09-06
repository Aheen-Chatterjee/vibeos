using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace VibeOS.App.Voice;

/// <summary>
/// Client for the OpenWhispr dictation-control protocol (PRD §26), implemented
/// by the M8 fork — see <c>docs/openwhispr-fork-m8.md</c> for the fork-side
/// contract. Until the fork lands, <see cref="IsConfigured"/> is false and
/// voice rides the phase-1 PTT bridge (M5).
///
/// Protocol:
/// <c>POST /v1/dictation/start</c> → <c>{ sessionId }</c>,
/// <c>POST /v1/dictation/stop|cancel</c> with <c>{ sessionId }</c>,
/// <c>GET /v1/dictation/events?sessionId=…</c> as Server-Sent Events with
/// <c>started | ready | inserted | failed</c> events and JSON payloads.
/// </summary>
public sealed class OpenWhisprIpcClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly Func<(string? Server, string? Token)> _bridge;
    private readonly Action<string> _log;
    private bool _disposed;

    public OpenWhisprIpcClient(Func<(string? Server, string? Token)> bridge, Action<string> log)
    {
        _bridge = bridge;
        _log = log;
    }

    public bool IsConfigured
    {
        get
        {
            var (server, token) = _bridge();
            return !string.IsNullOrEmpty(server) && !string.IsNullOrEmpty(token);
        }
    }

    private HttpRequestMessage? Authed(HttpMethod method, string path)
    {
        var (server, token) = _bridge();
        if (string.IsNullOrEmpty(server) || _disposed) return null;
        var request = new HttpRequestMessage(method, $"{server}{path}");
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    public async Task<string?> StartDictationAsync(CancellationToken ct)
    {
        if (_disposed) return null;
        try
        {
            using var request = Authed(HttpMethod.Post, "/v1/dictation/start");
            if (request is null) return null;
            request.Content = JsonContent.Create(new { source = "vibeos" });
            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var doc = await response.Content.ReadFromJsonAsync<JsonDocument>(ct);
            return doc?.RootElement.TryGetProperty("sessionId", out var id) == true
                ? id.GetString()
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log($"[VibeOS] IPC dictation start failed: {ex.Message}");
            return null;
        }
    }

    public async Task StopDictationAsync(string sessionId)
    {
        if (_disposed) return;
        try
        {
            using var request = Authed(HttpMethod.Post, "/v1/dictation/stop");
            if (request is null) return;
            request.Content = JsonContent.Create(new { sessionId });
            using var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _log($"[VibeOS] IPC dictation stop failed: {ex.Message}");
        }
    }

    public async Task CancelDictationAsync(string sessionId)
    {
        if (_disposed) return;
        try
        {
            using var request = Authed(HttpMethod.Post, "/v1/dictation/cancel");
            if (request is null) return;
            request.Content = JsonContent.Create(new { sessionId });
            using var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _log($"[VibeOS] IPC dictation cancel failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Streams dictation events until <c>inserted</c>/<c>failed</c>, cancellation,
    /// or <paramref name="timeout"/>. Yields <c>(type, payload)</c> tuples.
    /// </summary>
    public async IAsyncEnumerable<(string Type, JsonElement Payload)> WatchEventsAsync(
        string sessionId,
        TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (_disposed) yield break;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        HttpResponseMessage response;
        try
        {
            using var request = Authed(HttpMethod.Get, $"/v1/dictation/events?sessionId={Uri.EscapeDataString(sessionId)}");
            if (request is null) yield break;
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log($"[VibeOS] IPC event stream failed: {ex.Message}");
            yield break;
        }

        using (response)
        await using (var stream = await response.Content.ReadAsStreamAsync(cts.Token))
        using (var reader = new StreamReader(stream))
        {
            string? eventType = null;
            while (!reader.EndOfStream)
            {
                string? line;
                try { line = await reader.ReadLineAsync(cts.Token); }
                catch (OperationCanceledException) { yield break; }

                if (line is null) yield break;
                if (line.StartsWith("event:", StringComparison.Ordinal))
                    eventType = line["event:".Length..].Trim();
                else if (line.StartsWith("data:", StringComparison.Ordinal) && eventType is not null)
                {
                    JsonElement payload;
                    try { payload = JsonDocument.Parse(line["data:".Length..]).RootElement.Clone(); }
                    catch (JsonException) { eventType = null; continue; }

                    yield return (eventType, payload);
                    if (eventType is "inserted" or "failed") yield break;
                    eventType = null;
                }
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _http.Dispose();
    }
}
