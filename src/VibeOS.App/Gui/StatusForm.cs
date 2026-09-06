using System.Diagnostics;
using System.Drawing;

namespace VibeOS.App.Gui;

/// <summary>
/// Status window: state, controller, profile, voice, sticky latches, recent
/// log, and the common actions. Closing the window hides to tray; Quit exits.
/// All reads go through Program.GetStatus() (2 Hz poll); all writes through
/// the thread-safe Program.Request* methods.
/// </summary>
public sealed class StatusForm : Form
{
    private readonly Label _stateValue = new();
    private readonly Label _padValue = new();
    private readonly Label _profileValue = new();
    private readonly Label _voiceValue = new();
    private readonly Label _stickyValue = new();
    private readonly ListBox _log = new();
    private readonly Button _toggleButton = new();
    private readonly Button _reloadButton = new();
    private readonly Button _configButton = new();
    private readonly CheckBox _autostartBox = new();
    private readonly Button _quitButton = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private bool _quitting;

    public StatusForm()
    {
        Text = "VibeOS";
        Width = 460;
        Height = 560;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        Font = new Font("Segoe UI", 10f, GraphicsUnit.Pixel);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        var row = 0;
        void AddRow(string name, Control value)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { Text = name, AutoSize = true, ForeColor = Color.Gray }, 0, row);
            value.Dock = DockStyle.Fill;
            layout.Controls.Add(value, 1, row);
            row++;
        }

        _stateValue.Font = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
        AddRow("State", _stateValue);
        AddRow("Controller", _padValue);
        AddRow("Profile", _profileValue);
        AddRow("Voice", _voiceValue);
        AddRow("Sticky", _stickyValue);

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _log.Dock = DockStyle.Fill;
        _log.Font = new Font("Consolas", 10f, GraphicsUnit.Pixel);
        layout.Controls.Add(_log, 0, row);
        layout.SetColumnSpan(_log, 2);
        row++;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        _toggleButton.Text = "Suspend";
        _toggleButton.Click += (_, _) => Program.RequestToggle();
        _reloadButton.Text = "Reload";
        _reloadButton.Click += (_, _) => Program.RequestReload();
        _configButton.Text = "Config";
        _configButton.Click += (_, _) => OpenConfig();
        _autostartBox.Text = "Start with Windows";
        _autostartBox.CheckedChanged += (_, _) =>
        {
            if (_autostartBox.Focused) Autostart.SetEnabled(_autostartBox.Checked);
        };
        _quitButton.Text = "Quit";
        _quitButton.Click += (_, _) => Quit();
        buttons.Controls.AddRange(new Control[]
            { _toggleButton, _reloadButton, _configButton, _autostartBox, _quitButton });
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(buttons, 0, row);
        layout.SetColumnSpan(buttons, 2);

        Program.LogLine += OnLogLine;
        _timer.Interval = 500;
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();
        RefreshStatus();
    }

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
