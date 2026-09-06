namespace VibeOS.App.Actions;

/// <summary>
/// One-line badges for wheel slots ("what does this do"): the key gesture
/// for key actions, "switch" for app launchers, "latch" for sticky modifiers.
/// Resolved through <see cref="Resolver"/>, which Program wires to the live
/// gesture tables (built-ins + config gestures).
/// </summary>
public static class ActionHints
{
    public static Func<string, KeyGesture?> Resolver { get; set; } = _ => null;

    public static string For(string actionId)
    {
        if (actionId.StartsWith("__gesture:", StringComparison.Ordinal))
            return actionId["__gesture:".Length..];
        if (actionId.StartsWith("launch-", StringComparison.OrdinalIgnoreCase))
            return "switch";
        if (Core.Input.StickyModifiers.IsStickyAction(actionId))
            return "latch";
        if (ActionRouter.IsVoiceStub(actionId))
            return "voice";
        if (string.Equals(actionId, "none", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var gesture = Resolver(actionId);
        return gesture?.ToString() ?? string.Empty;
    }
}
