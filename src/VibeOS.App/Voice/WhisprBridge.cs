using System.Text.Json;

namespace VibeOS.App.Voice;

/// <summary>
/// Discovers the OpenWhispr loopback bridge (M7/M8). OpenWhispr writes its
/// dynamic port and bearer token to <c>~/.openwhispr/cli-bridge.json</c>;
/// VibeOS reads that file (same user, loopback-only) so dictionaries and IPC
/// work with zero setup. Precedence: explicit config <c>voice.server</c> wins
/// for the URL; <c>VIBEOS_WHISPR_TOKEN</c> wins for the token. The token is
/// never logged.
/// </summary>
public static class WhisprBridge
{
    public static (string? Server, string? Token) Resolve(string? configuredServer)
    {
        string? fileServer = null;
        string? fileToken = null;

        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var path = Path.Combine(home, ".openwhispr", "cli-bridge.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("port", out var port))
                    fileServer = $"http://127.0.0.1:{port.GetInt32()}";
                if (doc.RootElement.TryGetProperty("token", out var tok))
                    fileToken = tok.GetString();
            }
        }
        catch
        {
            // Discovery is best-effort; callers degrade to dormant bridge mode.
        }

        var server = string.IsNullOrEmpty(configuredServer) ? fileServer : configuredServer.TrimEnd('/');
        var token = Environment.GetEnvironmentVariable("VIBEOS_WHISPR_TOKEN");
        if (string.IsNullOrEmpty(token)) token = fileToken;
        return (server, token);
    }
}
