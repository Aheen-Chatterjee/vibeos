using System.Text.Json;
using VibeOS.App.Actions;
using VibeOS.App.Overlay;
using VibeOS.Core.Input;

namespace VibeOS.App.Profiles;

/// <summary>
/// Loads JSONC configuration (PRD §56–57): a global file plus optional
/// per-application overrides in an <c>apps/</c> subdirectory. Rebuilds the
/// chord list consumed by <see cref="ChordEngine"/> and hot-reloads on change,
/// retaining last-known-good when a file is malformed (spec §8).
///
/// Binding values accept three forms:
/// <list type="bullet">
/// <item><c>"copy"</c> — a built-in action id or <c>voice-*</c> stub.</item>
/// <item><c>{ "key": "CTRL+P" }</c> — a raw key gesture.</item>
/// <item><c>{ "action": "undo", "mode": "Release" }</c> — named action with an
/// explicit activation mode (default <c>Press</c>).</item>
/// </list>
/// </summary>
public sealed class ProfileManager : IDisposable
{
    private readonly string _configDir;
    private readonly Action<string> _log;
    private readonly FileSystemWatcher? _watcher;
    private readonly System.Threading.Timer _debounce;
    private int _reloadPending;

    private IReadOnlyList<ChordDefinition> _chords = Array.Empty<ChordDefinition>();

    public event Action? Reloaded;

    public IReadOnlyList<ChordDefinition> Chords => _chords;

    public ProfileManager(string configDir, Action<string> log)
    {
        _configDir = configDir;
        _log = log;

        Load();

        _debounce = new System.Threading.Timer(_ =>
        {
            if (Interlocked.Exchange(ref _reloadPending, 0) == 0) return;
            if (Load())
                Reloaded?.Invoke();
        }, null, Timeout.Infinite, Timeout.Infinite);

        if (Directory.Exists(_configDir))
        {
            _watcher = new FileSystemWatcher(_configDir, "*.jsonc")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            };
            _watcher.Changed += (_, _) => ScheduleReload();
            _watcher.Created += (_, _) => ScheduleReload();
            _watcher.Renamed += (_, _) => ScheduleReload();
            _watcher.EnableRaisingEvents = true;
        }
    }

    private void ScheduleReload()
    {
        Interlocked.Exchange(ref _reloadPending, 1);
        _debounce.Change(250, Timeout.Infinite);
    }

    /// <returns>True when the bindings were replaced.</returns>
    private bool Load()
    {
        try
        {
            var global = Path.Combine(_configDir, "vibeos.jsonc");
            if (!File.Exists(global))
            {
                _log($"[VibeOS] No config at {global} — running pointer-only.");
                return false;
            }

            // Build into locals so a failed reload keeps last-known-good for
            // both the chord list and the gesture table (spec §8).
            var chords = new List<ChordDefinition>();
            var gestures = new Dictionary<string, KeyGesture>(StringComparer.OrdinalIgnoreCase);

            ParseFile(global, appContext: null, chords, gestures);

            var appsDir = Path.Combine(_configDir, "apps");
            if (Directory.Exists(appsDir))
            {
                foreach (var file in Directory.GetFiles(appsDir, "*.jsonc").OrderBy(f => f))
                    ParseAppFile(file, chords, gestures);
            }

            var wheels = ParseWheels(global, gestures);
            var voice = ParseVoice(global);

            _chords = chords;
            _gestures = gestures;
            _wheels = wheels;
            _voice = voice;
            _log($"[VibeOS] Config loaded: {chords.Count} bindings, {wheels.Count} wheels.");
            return true;
        }
        catch (Exception ex)
        {
            _log($"[VibeOS] Config reload failed, keeping previous bindings: {ex.Message}");
            return false;
        }
    }

    private void ParseAppFile(string file, List<ChordDefinition> chords, Dictionary<string, KeyGesture> gestures)
    {
        using var doc = LoadJson(file);
        if (!doc.RootElement.TryGetProperty("match", out var match) ||
            !match.TryGetProperty("process", out var processes))
        {
            throw new InvalidOperationException($"{Path.GetFileName(file)}: missing match.process");
        }

        var names = new List<string>();
        if (processes.ValueKind == JsonValueKind.Array)
            names.AddRange(processes.EnumerateArray().Select(e => e.GetString()!).Where(s => s is not null));
        else if (processes.ValueKind == JsonValueKind.String)
            names.Add(processes.GetString()!);

        if (names.Count == 0)
            throw new InvalidOperationException($"{Path.GetFileName(file)}: match.process is empty");

        foreach (var name in names)
            ParseBindings(doc.RootElement, appContext: name, chords, gestures, file);
    }

    private void ParseFile(string file, string? appContext, List<ChordDefinition> chords, Dictionary<string, KeyGesture> gestures)
    {
        using var doc = LoadJson(file);
        ParseBindings(doc.RootElement, appContext, chords, gestures, file);
    }

    private static JsonDocument LoadJson(string file)
    {
        using var stream = File.OpenRead(file);
        return JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
    }

    private void ParseBindings(JsonElement root, string? appContext, List<ChordDefinition> chords, Dictionary<string, KeyGesture> gestures, string file)
    {
        if (!root.TryGetProperty("bindings", out var bindings) ||
            bindings.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var shortName = appContext is null ? Path.GetFileName(file) : $"{Path.GetFileName(file)} [{appContext}]";
        foreach (var prop in bindings.EnumerateObject())
        {
            if (!ChordParser.TryParse(prop.Name, out var modifiers, out var trigger, out var chordError))
                throw new InvalidOperationException($"{shortName}: binding '{prop.Name}': {chordError}");

            var (actionId, mode) = ParseActionValue(prop.Value, gestures, $"{shortName}: binding '{prop.Name}'");
            chords.Add(new ChordDefinition(modifiers, trigger, mode, actionId, appContext));
        }
    }

    /// <summary>Shared value grammar for bindings and wheel slots.</summary>
    private static (string ActionId, ActivationMode Mode) ParseActionValue(
        JsonElement value, Dictionary<string, KeyGesture> gestures, string context)
    {
        string actionId;
        var mode = ActivationMode.Press;

        if (value.ValueKind == JsonValueKind.String)
        {
            actionId = value.GetString()!;
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            if (value.TryGetProperty("key", out var keyProp))
            {
                var gestureText = keyProp.GetString()!;
                if (!KeyGesture.TryParse(gestureText, out var gesture, out var gestureError) || gesture is null)
                    throw new InvalidOperationException($"{context}: {gestureError}");
                // Raw gestures become synthetic per-binding actions.
                actionId = $"__gesture:{gestureText}";
                gestures[actionId] = gesture;
            }
            else if (value.TryGetProperty("action", out var actionProp))
            {
                actionId = actionProp.GetString()!;
            }
            else
            {
                throw new InvalidOperationException($"{context} needs \"key\" or \"action\"");
            }

            if (value.TryGetProperty("mode", out var modeProp) &&
                !Enum.TryParse<ActivationMode>(modeProp.GetString(), ignoreCase: true, out mode))
            {
                throw new InvalidOperationException(
                    $"{context} has unknown mode '{modeProp.GetString()}'");
            }
        }
        else
        {
            throw new InvalidOperationException($"{context} must be a string or object");
        }

        if (!ActionRouter.IsBuiltIn(actionId) &&
            !ActionRouter.IsVoiceStub(actionId) &&
            !actionId.StartsWith("__gesture:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{context} references unknown action '{actionId}'");
        }

        return (actionId, mode);
    }

    private static VoiceConfig ParseVoice(string globalFile)
    {
        using var doc = LoadJson(globalFile);
        if (!doc.RootElement.TryGetProperty("voice", out var voiceProp) ||
            voiceProp.ValueKind != JsonValueKind.Object)
        {
            return VoiceConfig.Default;
        }

        var hotkeyText = voiceProp.TryGetProperty("hotkey", out var hotkeyProp)
            ? hotkeyProp.GetString() ?? "CTRL+SHIFT+F11"
            : "CTRL+SHIFT+F11";

        if (!KeyGesture.TryParse(hotkeyText, out var hotkey, out var error) || hotkey is null)
            throw new InvalidOperationException($"voice.hotkey: {error}");

        return new VoiceConfig(hotkey);
    }

    private static List<Wheel> ParseWheels(string globalFile, Dictionary<string, KeyGesture> gestures)
    {
        var wheels = new List<Wheel>();
        using var doc = LoadJson(globalFile);

        if (!doc.RootElement.TryGetProperty("wheels", out var wheelsProp) ||
            wheelsProp.ValueKind != JsonValueKind.Array)
        {
            return wheels;
        }

        foreach (var wheelProp in wheelsProp.EnumerateArray())
        {
            var name = wheelProp.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString() ?? "Wheel"
                : "Wheel";

            if (!wheelProp.TryGetProperty("slots", out var slotsProp) ||
                slotsProp.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException($"wheel '{name}' needs a slots array");
            }

            var slots = new List<WheelSlot>();
            foreach (var slotProp in slotsProp.EnumerateArray())
            {
                if (!slotProp.TryGetProperty("label", out var labelProp))
                    throw new InvalidOperationException($"wheel '{name}' has a slot without a label");
                var (actionId, _) = ParseActionValue(slotProp, gestures, $"wheel '{name}' slot '{labelProp.GetString()}'");
                slots.Add(new WheelSlot(labelProp.GetString()!, actionId));
            }

            // Wheel slots carry label+action in one object; ParseActionValue
            // reads "key"/"action" members and ignores "label"/"mode".
            wheels.Add(new Wheel(name, slots));
        }

        return wheels;
    }

    private Dictionary<string, KeyGesture> _gestures =
        new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<Wheel> _wheels = Array.Empty<Wheel>();

    /// <summary>Radial wheels from the global config (spec §5.9).</summary>
    public IReadOnlyList<Wheel> Wheels => _wheels;

    private VoiceConfig _voice = VoiceConfig.Default;

    /// <summary>Voice bridge settings (PRD §25).</summary>
    public VoiceConfig Voice => _voice;

    /// <summary>Raw gestures materialized at load time, consumed by Program dispatch.</summary>
    public IReadOnlyDictionary<string, KeyGesture> GestureActions => _gestures;

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce.Dispose();
    }
}
