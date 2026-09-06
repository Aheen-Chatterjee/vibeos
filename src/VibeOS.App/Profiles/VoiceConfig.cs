namespace VibeOS.App.Profiles;

/// <summary>In-house voice settings (voice plan §9).</summary>
public sealed record VoiceConfig(
    string Model,
    string Language,
    bool Cleanup,
    string CleanupModel,
    int CleanupTimeoutMs,
    string Ollama)
{
    public static VoiceConfig Default { get; } = new(
        Model: "base.en",
        Language: "en",
        Cleanup: true,
        CleanupModel: "qwen3:1.7b",
        CleanupTimeoutMs: 2500,
        Ollama: "http://localhost:11434");
}
