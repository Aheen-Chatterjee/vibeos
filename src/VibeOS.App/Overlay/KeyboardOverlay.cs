using System.Drawing;
using System.Drawing.Drawing2D;
using VibeOS.App.Overlay;

namespace VibeOS.App.Overlay;

/// <summary>
/// Hosts the keyboard <see cref="KbForm"/> on its own STA thread, mirroring
/// <see cref="RadialWheelOverlay"/>: painting can never stall input.
/// </summary>
public sealed class KeyboardOverlay : IDisposable
{
    private Thread? _thread;
    private KbForm? _form;
    private readonly ManualResetEventSlim _ready = new(false);
    private bool _disposed;

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(() =>
        {
            _form = new KbForm();
            _ready.Set();
            Application.Run();
        });
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Name = "VibeOS.KeyboardOverlay";
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    public void Update(KeyboardController.KeyboardView view)
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

    private sealed class KbForm : Form
    {
        private KeyboardController.KeyboardView _view =
            new(false, string.Empty, new List<List<string>>(), 0, 0, false);

        private readonly Font _keyFont;
        private readonly Font _titleFont;

        public KbForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            _keyFont = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
            _titleFont = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel);
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

        public void ShowView(KeyboardController.KeyboardView view)
        {
            _view = view;
            if (!view.Open || view.Rows.Count == 0)
            {
                if (Visible) Hide();
                return;
            }

            using var graphics = CreateGraphics();
            var scale = graphics.DpiX / 96f;
            var screen = Screen.FromPoint(Cursor.Position);
            var area = screen.WorkingArea;

            // Widest row sets the width; 4px gap + padding.
            var cell = (int)(64f * scale);
            var gap = (int)(6f * scale);
            var maxCols = 0;
            foreach (var r in view.Rows) maxCols = Math.Max(maxCols, r.Count);
            var width = maxCols * (cell + gap) + gap + (int)(32f * scale);
            var height = view.Rows.Count * (cell + gap) + gap + (int)(72f * scale);

            Size = new Size(width, height);
            Location = new Point(
                area.X + (area.Width - width) / 2,
                area.Y + area.Height - height - (int)(48f * scale));

            if (!Visible) Show();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (_view.Open && _view.Rows.Count > 0)
                PaintGrid(g);

            base.OnPaint(e);
        }

        private void PaintGrid(Graphics g)
        {
            var rows = _view.Rows;
            using var graphics = CreateGraphics();
            var scale = graphics.DpiX / 96f;
            var cell = 64f * scale;
            var gap = 6f * scale;
            var top = 56f * scale;
            var left = 16f * scale + gap;

            var title = $"Keyboard · {_view.Page}{(_view.Shift ? " · SHIFT" : "")}      A type · B close · X bksp";
            g.DrawString(title, _titleFont, Brushes.White, left, 12f * scale);

            for (var r = 0; r < rows.Count; r++)
            {
                // Centre each row.
                var rowWidth = rows[r].Count * (cell + gap) - gap;
                var x0 = (ClientSize.Width - rowWidth) / 2f;
                var y = top + r * (cell + gap);

                for (var c = 0; c < rows[r].Count; c++)
                {
                    var rect = new RectangleF(x0 + c * (cell + gap), y, cell, cell);
                    var selected = r == _view.SelectedRow && c == _view.SelectedCol;
                    using var brush = new SolidBrush(selected ? Color.SteelBlue : Color.FromArgb(48, 48, 52));
                    g.FillRectangle(brush, rect);
                    g.DrawRectangle(Pens.Gray, rect.X, rect.Y, rect.Width, rect.Height);

                    var label = rows[r][c];
                    var size = g.MeasureString(label, _keyFont);
                    g.DrawString(label, _keyFont, Brushes.White,
                        rect.X + (rect.Width - size.Width) / 2f,
                        rect.Y + (rect.Height - size.Height) / 2f);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _keyFont.Dispose();
                _titleFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
