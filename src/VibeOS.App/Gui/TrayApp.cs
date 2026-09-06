using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace VibeOS.App.Gui;

/// <summary>
/// Tray icon + status window host on its own STA thread (PRD §72). The icon
/// dot is green while Active, grey while Suspended; the tooltip carries pad
/// and profile. Double-click shows the status window.
/// </summary>
public sealed class TrayApp : IDisposable
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const int SW_HIDE = 0;

    public static void HideConsole()
    {
        var console = GetConsoleWindow();
        if (console != IntPtr.Zero) ShowWindow(console, SW_HIDE);
    }

    private Thread? _thread;
    private StatusForm? _form;
    private NotifyIcon? _icon;
    private Icon? _green;
    private Icon? _grey;
    private readonly ManualResetEventSlim _ready = new(false);
    private bool _disposed;

    public void Start(bool startHidden)
    {
        if (_thread is not null) return;
        _thread = new Thread(() =>
        {
            _green = MakeDot(Color.LimeGreen);
            _grey = MakeDot(Color.Gray);
            _form = new StatusForm();
            _icon = new NotifyIcon
            {
                Text = "VibeOS",
                Visible = true,
            };
            _icon.DoubleClick += (_, _) => ShowStatus();
            RebuildMenu();
            if (!startHidden) _form.Show();
            _ready.Set();
            Application.Run();
        });
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Name = "VibeOS.Gui";
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public void ShowStatus()
    {
        var form = _form;
        if (form is null || _disposed) return;
        try { form.BeginInvoke(() => { if (!form.Visible) form.Show(); form.Activate(); }); }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    private static Icon MakeDot(Color color)
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, 1, 1, 14, 14);
            g.DrawEllipse(Pens.Black, 1, 1, 14, 14);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    private void RebuildMenu()
    {
        var icon = _icon;
        if (icon is null) return;

        var menu = new ContextMenuStrip();
        var title = new ToolStripMenuItem("VibeOS") { Enabled = false };
        var enabled = new ToolStripMenuItem("Enabled", null, (_, _) => Program.RequestToggle());
        var pad = new ToolStripMenuItem("Controller") { Enabled = false };
        var voice = new ToolStripMenuItem("Voice") { Enabled = false };
        var show = new ToolStripMenuItem("Show status", null, (_, _) => ShowStatus());
        var reload = new ToolStripMenuItem("Reload profiles", null, (_, _) => Program.RequestReload());
        var config = new ToolStripMenuItem("Open configuration", null, (_, _) => OpenConfig());
        var autostart = new ToolStripMenuItem("Start with Windows", null, (_, _) =>
        {
            try { Autostart.SetEnabled(!Autostart.IsEnabled()); } catch { }
        });
        var exit = new ToolStripMenuItem("Exit", null, (_, _) => Program.RequestQuit());

        menu.Items.AddRange(new ToolStripItem[]
            { title, enabled, pad, voice, new ToolStripSeparator(),
              show, reload, config, autostart, new ToolStripSeparator(), exit });

        // Refresh dynamic rows on every open.
        menu.Opening += (_, _) =>
        {
            Program.StatusSnapshot s;
            try { s = Program.GetStatus(); }
            catch { return; }
            var active = s.State == "Active";
            enabled.Checked = active;
            pad.Text = "Controller: " + (s.PadConnected ? s.PadName : "waiting…");
            voice.Text = "Voice: " + (s.VoiceModel == "loading…" ? "loading…" : $"{s.Voice} ({s.VoiceModel})");
            autostart.Checked = Autostart.IsEnabled();
            icon.Icon = active ? _green : _grey;
            icon.Text = $"VibeOS — {s.State}, {s.Profile}";
        };

        icon.ContextMenuStrip = menu;
        icon.Icon = _green;
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

    public void Dispose()
    {
        _disposed = true;
        try
        {
            if (_icon is not null)
            {
                _icon.Visible = false;
                _icon.Dispose();
                _icon = null;
            }
            if (_green is not null) { DestroyIcon(_green.Handle); _green.Dispose(); }
            if (_grey is not null) { DestroyIcon(_grey.Handle); _grey.Dispose(); }
        }
        catch { }
        _ready.Dispose();
    }
}
