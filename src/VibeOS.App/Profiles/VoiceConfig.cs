using VibeOS.App.Actions;

namespace VibeOS.App.Profiles;

/// <summary>
/// Voice bridge settings (PRD §25). The hotkey must match the PTT binding
/// configured inside OpenWhispr. <see cref="Server"/> is the loopback bridge
/// URL for dictionary pushes (M7); null leaves dictionaries dormant.
/// </summary>
public sealed record VoiceConfig(KeyGesture Hotkey, string? Server = null)
{
    public static VoiceConfig Default { get; } = CreateDefault();

    private static VoiceConfig CreateDefault()
    {
        if (!KeyGesture.TryParse("CTRL+SHIFT+F11", out var hotkey, out _) || hotkey is null)
            throw new InvalidOperationException("Default voice hotkey is invalid.");
        return new VoiceConfig(hotkey);
    }
}
