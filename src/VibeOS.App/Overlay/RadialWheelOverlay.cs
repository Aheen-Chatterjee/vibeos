using System.Drawing;
using System.Drawing.Drawing2D;
using VibeOS.App.Overlay;

namespace VibeOS.App.Overlay;

/// <summary>
/// Hosts the <see cref="WheelForm"/> on its own STA thread so overlay painting
/// can never stall the input or pointer threads (spec §5.2). The input thread
/// pushes a <see cref="WheelController.WheelView"/> every tick; the form only
/// ever reads the latest one.
/// </summary>
public sealed class RadialWheelOverlay : IDisposable
{
    private Thread? _thread;
    private WheelForm? _form;
    private readonly ManualResetEventSlim _ready = new(false);
    private bool _disposed;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(() =>
        {
            _form = new WheelForm();
            // Force window-handle creation NOW. Without this, BeginInvoke from
            // the input thread throws (handle-less controls cannot marshal),
            // the exception was swallowed, and the wheel never rendered.
            _ = _form.Handle;
            _ready.Set();
            Application.Run();
        });
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Name = "VibeOS.WheelOverlay";
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public void Update(WheelController.WheelView view)
    {
        var form = _form;
        if (form is null || _disposed) return;
        try
        {
            form.BeginInvoke(() => form.ShowView(view));
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        _disposed = true;
        var form = _form;
        _form = null;
        try { form?.BeginInvoke(() => form.Close()); } catch { }
        _ready.Dispose();
    }

    private sealed class WheelForm : Form
    {
        private WheelController.WheelView _view =
            new(false, string.Empty, Array.Empty<WheelSlot>(), -1);

        private readonly Font _labelFont;
        private readonly Font _titleFont;
        private readonly Font _hintFont;

        public WheelForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            _labelFont = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
            _titleFont = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
            _hintFont = new Font("Segoe UI", 11f, GraphicsUnit.Pixel);
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_TRANSPARENT = 0x20;
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x80;
                const int WS_EX_TOPMOST = 0x8;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST;
                return cp;
            }
        }

        public void ShowView(WheelController.WheelView view)
        {
            _view = view;
            if (!view.Open || view.Slots.Count == 0)
            {
                if (Visible) Hide();
                return;
            }

            // Centre on the monitor holding the cursor.
            using var graphics = CreateGraphics();
            var scale = graphics.DpiX / 96f;
            var diameter = (int)(560f * scale);
            var screen = Screen.FromPoint(Cursor.Position);
            var area = screen.WorkingArea;
            Size = new Size(diameter, diameter);
            Location = new Point(
                area.X + (area.Width - diameter) / 2,
                area.Y + (area.Height - diameter) / 2);

            if (!Visible) Show();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var slots = _view.Slots;
            if (_view.Open && slots.Count > 0)
                PaintWheel(g, slots);

            base.OnPaint(e);
        }

        private void PaintWheel(Graphics g, IReadOnlyList<WheelSlot> slots)
        {
            var w = ClientSize.Width;
            var cx = w / 2f;
            var cy = w / 2f;
            var outer = w / 2f - 8f;
            var inner = w * 0.19f;

            var segment = 360f / slots.Count;
            var rect = new RectangleF(cx - outer, cy - outer, outer * 2f, outer * 2f);

            for (var i = 0; i < slots.Count; i++)
            {
                // Slot 0 centred at 12 o'clock, clockwise (matches RadialSelector).
                var start = i * segment - segment / 2f - 90f;
                using var dim = new SolidBrush(Color.FromArgb(48, 48, 52));
                var brush = i == _view.Highlighted ? Brushes.SteelBlue : dim;
                g.FillPie(brush, rect, start, segment - 2f);
                g.DrawPie(Pens.Gray, rect, start, segment - 2f);

                var mid = (i * segment - 90f) * Math.PI / 180d;
                var labelR = (outer + inner) / 2f;
                var lx = cx + (float)(Math.Cos(mid) * labelR);
                var ly = cy + (float)(Math.Sin(mid) * labelR);
                var size = g.MeasureString(slots[i].Label, _labelFont);
                g.DrawString(slots[i].Label, _labelFont, Brushes.White, lx - size.Width / 2f, ly - size.Height / 2f - 8f);

                var badge = Actions.ActionHints.For(slots[i].Action);
                if (!string.IsNullOrEmpty(badge))
                {
                    var badgeSize = g.MeasureString(badge, _hintFont);
                    g.DrawString(badge, _hintFont, Brushes.LightGray, lx - badgeSize.Width / 2f, ly + size.Height / 2f - 6f);
                }
            }

            // Hub covers the pie centres, leaving a ring.
            g.FillEllipse(Brushes.Magenta, cx - inner, cy - inner, inner * 2f, inner * 2f);
            g.DrawEllipse(Pens.Gray, cx - inner, cy - inner, inner * 2f, inner * 2f);

            var title = string.IsNullOrEmpty(_view.WheelName) ? "Wheel" : _view.WheelName;
            var titleSize = g.MeasureString(title, _titleFont);
            g.DrawString(title, _titleFont, Brushes.White, cx - titleSize.Width / 2f, cy - titleSize.Height - 4f);

            const string hint = "release = run · B = cancel";
            using var hintFont = new Font("Segoe UI", 11f, GraphicsUnit.Pixel);
            var hintSize = g.MeasureString(hint, hintFont);
            g.DrawString(hint, hintFont, Brushes.LightGray, cx - hintSize.Width / 2f, cy + 6f);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _labelFont.Dispose();
                _titleFont.Dispose();
                _hintFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
