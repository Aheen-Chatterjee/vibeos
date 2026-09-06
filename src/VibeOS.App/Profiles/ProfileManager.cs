using System.Text.Json;
using VibeOS.App.Actions;
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

            _chords = chords;
            _gestures = gestures;
            _log($"[VibeOS] Config loaded: {chords.Count} bindings.");
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

            string actionId;
            var mode = ActivationMode.Press;

            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                actionId = prop.Value.GetString()!;
            }
            else if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                if (prop.Value.TryGetProperty("key", out var keyProp))
                {
                    var gestureText = keyProp.GetString()!;
                    if (!KeyGesture.TryParse(gestureText, out var gesture, out var gestureError) || gesture is null)
                        throw new InvalidOperationException($"{shortName}: binding '{prop.Name}': {gestureError}");
                    // Raw gestures become synthetic per-binding actions.
                    actionId = $"__gesture:{gestureText}";
                    gestures[actionId] = gesture;
                }
                else if (prop.Value.TryGetProperty("action", out var actionProp))
                {
                    actionId = actionProp.GetString()!;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"{shortName}: binding '{prop.Name}' needs \"key\" or \"action\"");
                }

                if (prop.Value.TryGetProperty("mode", out var modeProp) &&
                    !Enum.TryParse<ActivationMode>(modeProp.GetString(), ignoreCase: true, out mode))
                {
                    throw new InvalidOperationException(
                        $"{shortName}: binding '{prop.Name}' has unknown mode '{modeProp.GetString()}'");
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"{shortName}: binding '{prop.Name}' must be a string or object");
            }

            if (!ActionRouter.IsBuiltIn(actionId) &&
                !ActionRouter.IsVoiceStub(actionId) &&
                !actionId.StartsWith("__gesture:", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{shortName}: binding '{prop.Name}' references unknown action '{actionId}'");
            }

            chords.Add(new ChordDefinition(modifiers, trigger, mode, actionId, appContext));
        }
    }

    private Dictionary<string, KeyGesture> _gestures =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raw gestures materialized at load time, consumed by Program dispatch.</summary>
    public IReadOnlyDictionary<string, KeyGesture> GestureActions => _gestures;

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce.Dispose();
    }
}
