using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace VibeOS.App.Voice;

/// <summary>
/// Pushes per-profile ASR dictionaries to OpenWhispr over its existing
/// loopback bridge — no fork needed (spec §2.4, PRD §31):
/// <c>GET /v1/dictionary/list</c>, <c>POST /v1/dictionary/update</c> with
/// <c>{ add: [...] }</c> / <c>{ remove: [...] }</c>.
///
/// Only words VibeOS itself added are ever removed (tracked in
/// <c>_lastPushed</c>), so switching profiles can never delete the user's own
/// dictionary. The bridge port is dynamic, so the server URL is configured
/// (see config <c>voice.server</c>); the bearer token comes from the
/// <c>VIBEOS_WHISPR_TOKEN</c> environment variable, never a config file.
/// Every failure is a log line, never an input-loop crash (spec §8).
/// </summary>
public sealed class DictionaryClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly string? _server;
    private readonly Action<string> _log;
    private HashSet<string> _lastPushed = new(StringComparer.OrdinalIgnoreCase);
    private bool _misconfigured;
    private bool _disposed;

    public DictionaryClient(string? server, Action<string> log)
    {
        _server = server?.TrimEnd('/');
        _log = log;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_server);

    /// <summary>Fire-and-forget from the profile-switch handler.</summary>
    public void SwitchTo(IReadOnlyList<string> desired)
    {
        if (_disposed) return;
        if (!IsConfigured)
        {
            if (!_misconfigured)
            {
                _misconfigured = true;
                _log("[VibeOS] Voice dictionaries dormant: set voice.server to the OpenWhispr bridge URL.");
            }
            return;
        }
        _ = Task.Run(() => PushAsync(desired));
    }

    private async Task PushAsync(IReadOnlyList<string> desired)
    {
        try
        {
            var token = Environment.GetEnvironmentVariable("VIBEOS_WHISPR_TOKEN");
            if (string.IsNullOrEmpty(token))
            {
                if (!_misconfigured)
                {
                    _misconfigured = true;
                    _log("[VibeOS] Voice dictionaries dormant: VIBEOS_WHISPR_TOKEN is not set.");
                }
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_server}/v1/dictionary/list");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var listResponse = await _http.SendAsync(request);
            listResponse.EnsureSuccessStatusCode();

            var current = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var doc = await listResponse.Content.ReadFromJsonAsync<JsonDocument>();
            if (doc?.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var e in doc.RootElement.EnumerateArray())
                    if (e.GetString() is string w) current.Add(w);

            var desiredSet = new HashSet<string>(desired, StringComparer.OrdinalIgnoreCase);
            var add = desiredSet.Where(w => !current.Contains(w)).ToList();
            var remove = _lastPushed.Where(w => !desiredSet.Contains(w) && current.Contains(w)).ToList();

            if (add.Count == 0 && remove.Count == 0)
            {
                _lastPushed = desiredSet;
                return;
            }

            using var update = new HttpRequestMessage(HttpMethod.Post, $"{_server}/v1/dictionary/update")
            {
                Content = JsonContent.Create(new { add, remove }),
            };
            update.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var updateResponse = await _http.SendAsync(update);
            updateResponse.EnsureSuccessStatusCode();

            _lastPushed = desiredSet;
            _log($"[VibeOS] Voice dictionary updated (+{add.Count}/-{remove.Count}).");
        }
        catch (Exception ex)
        {
            _log($"[VibeOS] Voice dictionary push failed (backend keeps working): {ex.Message}");
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _http.Dispose();
    }
}
