namespace VibeOS.App.Profiles;

/// <summary>Per-application voice settings.</summary>
public sealed record AppVoiceConfig(
    /// <summary>Submit key for voice+submit; null inherits the default (Enter).</summary>
    string? SubmitKey,
    /// <summary>Profile-specific ASR dictionary, fed as the whisper prompt.</summary>
    IReadOnlyList<string> Dictionary,
    /// <summary>polished (STT → cleanup → insert) or instant (raw insert).</summary>
    string Mode = "polished")
{
    public static readonly AppVoiceConfig Empty = new(null, Array.Empty<string>(), "polished");

    /// <summary>True unless the profile opts into verbatim instant mode.</summary>
    public bool Polished => !string.Equals(Mode, "instant", StringComparison.OrdinalIgnoreCase);
}
