using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;
using VibeOS.App.Actions;
using VibeOS.App.Profiles;
using VibeOS.Core.Input;

namespace VibeOS.App.Gui;

/// <summary>
/// VibeOS control window: Status (system at a glance + log), Controls (every
/// binding, wheel and the base layout), Customize (edit global bindings,
/// wheels and voice — saved to user.jsonc, never touching the commented
/// defaults). Closing hides to tray; Quit exits.
/// </summary>
public sealed class StatusForm : Form
{
    private const string KeyPrefix = "key:";

    // Status tab
    private readonly Label _stateValue = new();
    private readonly Label _padValue = new();
    private readonly Label _profileValue = new();
    private readonly Label _voiceValue = new();
    private readonly Label _stickyValue = new();
    private readonly ListBox _log = new();
    private readonly Button _toggleButton = new();
    private readonly CheckBox _autostartBox = new();

    // Controls tab
    private readonly ComboBox _scopeBox = new();
    private readonly DataGridView _bindingsGrid = new();
    private readonly DataGridView _wheelsGrid = new();

    // Customize tab
    private readonly DataGridView _editBindings = new();
    private readonly ComboBox _wheelBox = new();
    private readonly DataGridView _editWheels = new();
    private readonly ComboBox _voiceModel = new();
    private readonly TextBox _voiceLang = new();
    private readonly CheckBox _voiceCleanup = new();
    private readonly TextBox _voiceCleanupModel = new();
    private readonly NumericUpDown _voiceTimeout = new();
    private readonly TextBox _voiceOllama = new();
    private readonly Label _saveStatus = new();

    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly TabControl _tabs = new();
    private bool _quitting;
    private bool _customizeLoaded;

    public StatusForm()
    {
        Text = "VibeOS";
        Width = 780;
        Height = 660;
        MinimumSize = new Size(660, 540);
        StartPosition = FormStartPosition.CenterScreen;

        _tabs.Dock = DockStyle.Fill;
        Controls.Add(_tabs);

        _tabs.TabPages.Add(BuildStatusTab());
        _tabs.TabPages.Add(BuildControlsTab());
        _tabs.TabPages.Add(BuildCustomizeTab());
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedIndex == 1) RefreshControls();
            if (_tabs.SelectedIndex == 2 && !_customizeLoaded) LoadCustomize();
        };

        Program.LogLine += OnLogLine;
        _timer.Interval = 500;
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();
        RefreshStatus();
    }

    // ---------- Status tab ----------

    private TabPage BuildStatusTab()
    {
        var page = new TabPage("Status") { Padding = new Padding(12) };

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        page.Controls.Add(top);

        var system = new GroupBox { Text = "System", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };
        var systemRows = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        systemRows.Controls.Add(new Label { Text = "State", AutoSize = true, ForeColor = Color.Gray }, 0, 0);
        _stateValue.Font = new Font(Font.FontFamily, 18f, FontStyle.Bold);
        _stateValue.AutoSize = true;
        systemRows.Controls.Add(_stateValue, 1, 0);
        systemRows.Controls.Add(new Label { Text = "Sticky", AutoSize = true, ForeColor = Color.Gray }, 0, 1);
        _stickyValue.AutoSize = true;
        systemRows.Controls.Add(_stickyValue, 1, 1);
        system.Controls.Add(systemRows);
        top.Controls.Add(system, 0, 0);

        var input = new GroupBox { Text = "Input", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };
        var inputRows = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        inputRows.Controls.Add(new Label { Text = "Controller", AutoSize = true, ForeColor = Color.Gray }, 0, 0);
        _padValue.AutoSize = true;
        inputRows.Controls.Add(_padValue, 1, 0);
        inputRows.Controls.Add(new Label { Text = "Profile", AutoSize = true, ForeColor = Color.Gray }, 0, 1);
        _profileValue.AutoSize = true;
        inputRows.Controls.Add(_profileValue, 1, 1);
        inputRows.Controls.Add(new Label { Text = "Voice", AutoSize = true, ForeColor = Color.Gray }, 0, 2);
        _voiceValue.AutoSize = true;
        inputRows.Controls.Add(_voiceValue, 1, 2);
        input.Controls.Add(inputRows);
        top.Controls.Add(input, 1, 0);

        var logGroup = new GroupBox { Text = "Log", Dock = DockStyle.Fill, Padding = new Padding(8) };
        page.Controls.Add(logGroup);
        _log.Dock = DockStyle.Fill;
        _log.Font = new Font("Consolas", 10f);
        logGroup.Controls.Add(_log);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        page.Controls.Add(buttons);
        _toggleButton.Text = "Suspend";
        _toggleButton.AutoSize = true;
        _toggleButton.Click += (_, _) => Program.RequestToggle();
        var reload = new Button { Text = "Reload", AutoSize = true };
        reload.Click += (_, _) => Program.RequestReload();
        var config = new Button { Text = "Config folder", AutoSize = true };
        config.Click += (_, _) => OpenConfig();
        _autostartBox.Text = "Start with Windows";
        _autostartBox.AutoSize = true;
        _autostartBox.CheckedChanged += (_, _) =>
        {
            if (_autostartBox.Focused) Autostart.SetEnabled(_autostartBox.Checked);
        };
        var quit = new Button { Text = "Quit", AutoSize = true };
        quit.Click += (_, _) => Quit();
        buttons.Controls.AddRange(new Control[] { _toggleButton, reload, config, _autostartBox, quit });

        return page;
    }

    // ---------- Controls tab ----------

    private TabPage BuildControlsTab()
    {
        var page = new TabPage("Controls") { Padding = new Padding(12) };
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 300,
        };
        page.Controls.Add(split);

        var top = new Panel { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(top);
        var scopeRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        scopeRow.Controls.Add(new Label { Text = "Profile:", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
        _scopeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _scopeBox.Width = 220;
        _scopeBox.SelectedIndexChanged += (_, _) => RefreshControls();
        scopeRow.Controls.Add(_scopeBox);
        top.Controls.Add(scopeRow);

        SetupGrid(_bindingsGrid, "Chord", "Mode", "Action");
        _bindingsGrid.Dock = DockStyle.Fill;
        top.Controls.Add(_bindingsGrid);

        var bottom = new Panel { Dock = DockStyle.Fill };
        split.Panel2.Controls.Add(bottom);
        bottom.Controls.Add(new Label
        {
            Text = "Wheels (LB+RB hold, aim, release) — badge shows the shortcut",
            Dock = DockStyle.Top,
            AutoSize = true,
            ForeColor = Color.Gray,
        });
        SetupGrid(_wheelsGrid, "Wheel", "Slot", "Action");
        _wheelsGrid.Dock = DockStyle.Fill;
        bottom.Controls.Add(_wheelsGrid);

        return page;
    }

    private static void SetupGrid(DataGridView grid, params string[] columns)
    {
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        foreach (var c in columns) grid.Columns.Add(c, c);
    }

    private void RefreshControls()
    {
        var profiles = Program.Profiles;
        var scopes = new List<string> { "Global" };
        scopes.AddRange(profiles.AppWheels.Keys.OrderBy(k => k));
        foreach (var app in profiles.AppVoice.Keys)
            if (!scopes.Contains(app)) scopes.Add(app);
        foreach (var c in profiles.Chords.Select(c => c.AppContext).Distinct())
            if (c is not null && !scopes.Contains(c)) scopes.Add(c);

        var keep = _scopeBox.SelectedItem as string;
        _scopeBox.Items.Clear();
        foreach (var s in scopes.OrderBy(s => s)) _scopeBox.Items.Add(s);
        _scopeBox.SelectedItem = keep is not null && scopes.Contains(keep) ? keep : "Global";
        var scope = _scopeBox.SelectedItem as string;

        _bindingsGrid.Rows.Clear();
        foreach (var chord in profiles.Chords
                     .Where(c => (c.AppContext ?? "Global") == scope)
                     .OrderBy(c => c.Trigger.ToString())
                     .ThenBy(c => c.Modifiers.Count))
        {
            var chordText = string.Join("+",
                chord.Modifiers.OrderBy(m => m).Append(chord.Trigger));
            var action = chord.ActionId.StartsWith("__gesture:", StringComparison.Ordinal)
                ? KeyPrefix + chord.ActionId["__gesture:".Length..]
                : chord.ActionId;
            _bindingsGrid.Rows.Add(chordText, chord.Mode, action);
        }

        _wheelsGrid.Rows.Clear();
        var wheels = new List<(string Scope, Overlay.Wheel Wheel)>();
        foreach (var w in profiles.Wheels) wheels.Add(("Global", w));
        foreach (var kv in profiles.AppWheels)
            if (kv.Key == scope || scope == "Global") wheels.Add((kv.Key, kv.Value));
        foreach (var (wheelScope, wheel) in wheels)
            foreach (var slot in wheel.Slots)
                _wheelsGrid.Rows.Add($"{wheelScope}: {wheel.Name}", slot.Label,
                    slot.Action.StartsWith("__gesture:", StringComparison.Ordinal)
                        ? KeyPrefix + slot.Action["__gesture:".Length..]
                        : slot.Action);
    }

    // ---------- Customize tab ----------

    private TabPage BuildCustomizeTab()
    {
        var page = new TabPage("Customize") { Padding = new Padding(12) };
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 260 };
        page.Controls.Add(split);

        var top = new Panel { Dock = DockStyle.Fill };
        split.Panel1.Controls.Add(top);
        top.Controls.Add(new Label
        {
            Text = "Global bindings — Chord like LB+X, Action id or key:CTRL+P, Mode. Saves to user.jsonc (defaults untouched), live on save.",
            Dock = DockStyle.Top, AutoSize = true, ForeColor = Color.Gray,
        });
        _editBindings.Dock = DockStyle.Fill;
        _editBindings.RowHeadersVisible = false;
        _editBindings.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _editBindings.Columns.Add("Chord", "Chord");
        _editBindings.Columns.Add("Action", "Action");
        var modeCol = new DataGridViewComboBoxColumn
        {
            HeaderText = "Mode",
            Items = { "Press", "Release", "Hold", "DoubleTap" },
        };
        _editBindings.Columns.Add(modeCol);
        top.Controls.Add(_editBindings);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        split.Panel2.Controls.Add(bottom);

        var wheelPanel = new GroupBox { Text = "Wheels (replaces default set)", Dock = DockStyle.Fill, Padding = new Padding(6) };
        bottom.Controls.Add(wheelPanel, 0, 0);
        var wheelLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        wheelLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wheelLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        wheelPanel.Controls.Add(wheelLayout);
        _wheelBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _wheelBox.Dock = DockStyle.Top;
        _wheelBox.SelectedIndexChanged += (_, _) => LoadWheelGrid();
        wheelLayout.Controls.Add(_wheelBox, 0, 0);
        _editWheels.Dock = DockStyle.Fill;
        _editWheels.RowHeadersVisible = false;
        _editWheels.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _editWheels.Columns.Add("Label", "Label");
        _editWheels.Columns.Add("Action", "Action");
        wheelLayout.Controls.Add(_editWheels, 0, 1);

        var voicePanel = new GroupBox { Text = "Voice", Dock = DockStyle.Fill, Padding = new Padding(6) };
        bottom.Controls.Add(voicePanel, 1, 0);
        var voiceLayout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        voicePanel.Controls.Add(voiceLayout);
        var r = 0;
        void VRow(string name, Control c)
        {
            voiceLayout.Controls.Add(new Label { Text = name, AutoSize = true }, 0, r);
            c.Dock = DockStyle.Fill;
            voiceLayout.Controls.Add(c, 1, r);
            r++;
        }
        _voiceModel.Items.AddRange(new object[] { "tiny.en", "base.en", "small.en" });
        VRow("Model", _voiceModel);
        VRow("Language", _voiceLang);
        VRow("Cleanup", _voiceCleanup);
        VRow("Cleanup model", _voiceCleanupModel);
        _voiceTimeout.Minimum = 500;
        _voiceTimeout.Maximum = 15000;
        _voiceTimeout.Increment = 250;
        VRow("Cleanup timeout", _voiceTimeout);
        VRow("Ollama", _voiceOllama);

        var saveRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
        page.Controls.Add(saveRow);
        var save = new Button { Text = "Save customizations", AutoSize = true };
        save.Click += (_, _) => SaveCustomize();
        _saveStatus.AutoSize = true;
        _saveStatus.ForeColor = Color.Gray;
        saveRow.Controls.Add(save);
        saveRow.Controls.Add(_saveStatus);

        return page;
    }

    private void LoadCustomize()
    {
        _customizeLoaded = true;
        var profiles = Program.Profiles;

        _editBindings.Rows.Clear();
        foreach (var chord in profiles.Chords
                     .Where(c => c.AppContext is null)
                     .OrderBy(c => c.Trigger.ToString())
                     .ThenBy(c => c.Modifiers.Count))
        {
            var chordText = string.Join("+", chord.Modifiers.OrderBy(m => m).Append(chord.Trigger));
            var action = chord.ActionId.StartsWith("__gesture:", StringComparison.Ordinal)
                ? KeyPrefix + chord.ActionId["__gesture:".Length..]
                : chord.ActionId;
            _editBindings.Rows.Add(chordText, action, chord.Mode.ToString());
        }

        _wheelBox.Items.Clear();
        foreach (var w in profiles.Wheels) _wheelBox.Items.Add(w.Name);
        if (_wheelBox.Items.Count > 0) _wheelBox.SelectedIndex = 0;

        var voice = profiles.Voice;
        _voiceModel.SelectedItem = voice.Model;
        if (_voiceModel.SelectedItem is null) _voiceModel.Text = voice.Model;
        _voiceLang.Text = voice.Language;
        _voiceCleanup.Checked = voice.Cleanup;
        _voiceCleanupModel.Text = voice.CleanupModel;
        _voiceTimeout.Value = Math.Clamp(voice.CleanupTimeoutMs, 500, 15000);
        _voiceOllama.Text = voice.Ollama;
    }

    private void LoadWheelGrid()
    {
        _editWheels.Rows.Clear();
        var wheel = Program.Profiles.Wheels.FirstOrDefault(w => w.Name == (_wheelBox.SelectedItem as string));
        if (wheel is null) return;
        foreach (var slot in wheel.Slots)
        {
            var action = slot.Action.StartsWith("__gesture:", StringComparison.Ordinal)
                ? KeyPrefix + slot.Action["__gesture:".Length..]
                : slot.Action;
            _editWheels.Rows.Add(slot.Label, action);
        }
    }

    private void SaveCustomize()
    {
        try
        {
            var bindings = new JsonObject();
            foreach (DataGridViewRow row in _editBindings.Rows)
            {
                if (row.IsNewRow) continue;
                var chord = (row.Cells[0].Value as string ?? string.Empty).Trim();
                var action = (row.Cells[1].Value as string ?? string.Empty).Trim();
                var mode = (row.Cells[2].Value as string ?? "Press").Trim();
                if (string.IsNullOrEmpty(chord) && string.IsNullOrEmpty(action)) continue;
                if (!ChordParser.TryParse(chord, out _, out _, out var chordError))
                    throw new InvalidOperationException($"Binding '{chord}': {chordError}");
                bindings[chord] = ToActionValue(action, mode, $"Binding '{chord}'");
            }

            var wheels = new JsonArray();
            foreach (var wheel in Program.Profiles.Wheels)
            {
                // Persist the grid of the selected wheel, others verbatim.
                if (wheel.Name != (_wheelBox.SelectedItem as string))
                {
                    wheels.Add(WheelToJson(wheel));
                    continue;
                }
                var slots = new JsonArray();
                foreach (DataGridViewRow row in _editWheels.Rows)
                {
                    if (row.IsNewRow) continue;
                    var label = (row.Cells[0].Value as string ?? string.Empty).Trim();
                    var action = (row.Cells[1].Value as string ?? string.Empty).Trim();
                    if (string.IsNullOrEmpty(label) && string.IsNullOrEmpty(action)) continue;
                    if (string.IsNullOrEmpty(label))
                        throw new InvalidOperationException($"Wheel '{wheel.Name}' has a slot without a label.");
                    slots.Add(SlotToJson(label, action, $"Wheel '{wheel.Name}' slot '{label}'"));
                }
                wheels.Add(new JsonObject { ["name"] = wheel.Name, ["slots"] = slots });
            }

            var voice = new JsonObject
            {
                ["model"] = _voiceModel.Text.Trim(),
                ["language"] = _voiceLang.Text.Trim(),
                ["cleanup"] = _voiceCleanup.Checked,
                ["cleanupModel"] = _voiceCleanupModel.Text.Trim(),
                ["cleanupTimeoutMs"] = (int)_voiceTimeout.Value,
                ["ollama"] = _voiceOllama.Text.Trim(),
            };

            var root = new JsonObject
            {
                ["_note"] = "GUI-managed VibeOS overrides. Same grammar as vibeos.jsonc; wins ties by priority. Delete sections (or this file) to fall back to defaults.",
                ["bindings"] = bindings,
                ["wheels"] = wheels,
                ["voice"] = voice,
            };

            var path = Path.Combine(Program.ConfigDir, "user.jsonc");
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            _saveStatus.ForeColor = Color.Green;
            _saveStatus.Text = $"Saved {Path.GetFileName(path)} — live in a moment.";
        }
        catch (Exception ex)
        {
            _saveStatus.ForeColor = Color.Red;
            _saveStatus.Text = ex.Message;
        }
    }

    private static JsonNode ToActionValue(string action, string mode, string context)
    {
        if (action.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var gesture = action[KeyPrefix.Length..].Trim();
            if (!KeyGesture.TryParse(gesture, out _, out var error))
                throw new InvalidOperationException($"{context}: {error}");
            return new JsonObject { ["key"] = gesture };
        }
        if (!ActionRouter.IsBuiltIn(action) && !ActionRouter.IsVoiceStub(action) && action != "none")
            throw new InvalidOperationException($"{context}: unknown action '{action}'.");
        if (mode.Equals("Press", StringComparison.OrdinalIgnoreCase))
            return JsonValue.Create(action)!;
        if (!Enum.TryParse<ActivationMode>(mode, ignoreCase: true, out _))
            throw new InvalidOperationException($"{context}: unknown mode '{mode}'.");
        return new JsonObject { ["action"] = action, ["mode"] = mode };
    }

    private static JsonObject SlotToJson(string label, string action, string context)
    {
        var node = ToActionValue(action, "Press", context);
        if (node is JsonObject obj)
        {
            obj.Insert(0, "label", label);
            return obj;
        }
        return new JsonObject { ["label"] = label, ["action"] = action };
    }

    private static JsonObject WheelToJson(Overlay.Wheel wheel)
    {
        var slots = new JsonArray();
        foreach (var slot in wheel.Slots)
        {
            var action = slot.Action.StartsWith("__gesture:", StringComparison.Ordinal)
                ? KeyPrefix + slot.Action["__gesture:".Length..]
                : slot.Action;
            slots.Add(SlotToJson(slot.Label, action, $"Wheel '{wheel.Name}'"));
        }
        return new JsonObject { ["name"] = wheel.Name, ["slots"] = slots };
    }

    // ---------- shared ----------

    private static void OpenConfig()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Program.ConfigDir,
                UseShellExecute = true,
            });
        }
        catch { }
    }

    private void Quit()
    {
        _quitting = true;
        Program.RequestQuit();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_quitting)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    private void OnLogLine(string message)
    {
        try
        {
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                _log.Items.Add(message);
                while (_log.Items.Count > 300) _log.Items.RemoveAt(0);
                _log.TopIndex = _log.Items.Count - 1;
            });
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private void RefreshStatus()
    {
        Program.StatusSnapshot s;
        try { s = Program.GetStatus(); }
        catch { return; }

        var active = s.State == "Active";
        _stateValue.Text = active ? "ACTIVE" : "SUSPENDED";
        _stateValue.ForeColor = active ? Color.Green : Color.DarkOrange;
        _toggleButton.Text = active ? "Suspend" : "Resume";
        _padValue.Text = s.PadConnected ? s.PadName : "waiting — connect any time";
        _profileValue.Text = s.Profile;
        _voiceValue.Text = s.VoiceModel == "loading…" ? "loading model…" : $"{s.Voice} ({s.VoiceModel})";
        _stickyValue.Text = string.IsNullOrEmpty(s.Sticky) ? "—" : s.Sticky;
        if (!_autostartBox.Focused) _autostartBox.Checked = Autostart.IsEnabled();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Program.LogLine -= OnLogLine;
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }
}
