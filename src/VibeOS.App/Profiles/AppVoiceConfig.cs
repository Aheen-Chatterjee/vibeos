namespace VibeOS.App.Profiles;

/// <summary>Per-application voice settings (PRD §27, §31).</summary>
public sealed record AppVoiceConfig(
    /// <summary>Submit key for voice+submit; null inherits the default (Enter).</summary>
    string? SubmitKey,
    /// <summary>Profile-specific ASR dictionary pushed on switch (M7).</summary>
    IReadOnlyList<string> Dictionary)
{
    public static readonly AppVoiceConfig Empty = new(null, Array.Empty<string>());
}
