using VibeOS.App.Windows;
using VibeOS.Core.Input;

namespace VibeOS.App.Overlay;

/// <summary>
/// Virtual keyboard state machine, owned by the input thread (PRD §33–34).
/// <code>
/// D-pad Up held ~350 ms → keyboard opens (tap stays free)
/// D-pad / left stick    → move selection (stick repeats)
/// A → type   B → close   X → backspace   RT → space
/// Start → enter   LB → shift latch   RB → symbols page
/// </code>
/// The overlay never steals focus: the underlying app's text field stays
/// active and characters arrive as synthetic key taps. While open the
/// controller reports <see cref="IsOpen"/>, freezing pointer and chords
/// (overlay precedence, PRD §46).
/// </summary>
public sealed class KeyboardController
{
    public const int OpenHoldMs = 350;
    private const double StickDeadzone = 0.5d;
    private const int StickRepeatMs = 180;

    private static readonly string[] LetterRows = { "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM" };
    private static readonly string[] ActionRow = { "Space", "Bksp", "Enter", "Sym", "Shift" };

    private static readonly SymKey[][] SymbolRows =
    {
        new[]
        {
            new SymKey("1", VirtualKey.D1, false), new SymKey("2", VirtualKey.D2, false),
            new SymKey("3", VirtualKey.D3, false), new SymKey("4", VirtualKey.D4, false),
            new SymKey("5", VirtualKey.D5, false), new SymKey("6", VirtualKey.D6, false),
            new SymKey("7", VirtualKey.D7, false), new SymKey("8", VirtualKey.D8, false),
            new SymKey("9", VirtualKey.D9, false), new SymKey("0", VirtualKey.D0, false),
        },
        new[]
        {
            new SymKey("-", VirtualKey.OemMinus, false), new SymKey("/", VirtualKey.Oem2, false),
            new SymKey(":", VirtualKey.Oem1, true), new SymKey(";", VirtualKey.Oem1, false),
            new SymKey("(", VirtualKey.D9, true), new SymKey(")", VirtualKey.D0, true),
        },
        new[]
        {
            new SymKey(".", VirtualKey.OemPeriod, false), new SymKey(",", VirtualKey.OemComma, false),
            new SymKey("?", VirtualKey.Oem2, true), new SymKey("!", VirtualKey.D1, true),
            new SymKey("'", VirtualKey.Oem7, false), new SymKey("\"", VirtualKey.Oem7, true),
        },
    };
    private static readonly string[] SymbolActionRow = { "Space", "Bksp", "Enter", "ABC", "_ " };

    private readonly object _gate = new();
    private long? _armedAt;
    private bool _open;
    private bool _symbols;
    private bool _shift;
    private int _row;
    private int _col;
    private long _lastStickMove;
    private int _lastStickDirX;
    private int _lastStickDirY;

    public bool IsOpen
    {
        get { lock (_gate) return _open; }
    }

    public KeyboardView View
    {
        get
        {
            lock (_gate)
            {
                var rows = CurrentLabels();
                return new KeyboardView(_open, _symbols ? "Symbols" : "Letters", rows, _row, _col, _shift);
            }
        }
    }

    public void Update(ControllerSnapshot previous, ControllerSnapshot current, long now, SendInputInjector injector)
    {
        lock (_gate)
        {
            if (!_open)
            {
                if (!current.IsDown(ButtonId.DpadUp))
                {
                    _armedAt = null;
                    return;
                }
                _armedAt ??= now;
                if (now - _armedAt.Value < OpenHoldMs) return;
                _armedAt = null;
                OpenLocked();
                return;
            }

            MoveLocked(previous, current, now);

            if (Edge(previous, current, ButtonId.B)) { CloseLocked(); return; }
            if (Edge(previous, current, ButtonId.RB)) { _symbols = !_symbols; ClampLocked(); return; }
            if (Edge(previous, current, ButtonId.LB)) { if (!_symbols) _shift = !_shift; return; }
            if (Edge(previous, current, ButtonId.X)) { injector.KeyTap(VirtualKey.Back); return; }
            if (Edge(previous, current, ButtonId.RT)) { injector.KeyTap(VirtualKey.Space); return; }
            if (Edge(previous, current, ButtonId.Start)) { injector.KeyTap(VirtualKey.Enter); return; }
            if (Edge(previous, current, ButtonId.A)) ActivateLocked(injector);
        }
    }

    public void ForceClose()
    {
        lock (_gate) CloseLocked();
    }

    private void OpenLocked()
    {
        _open = true;
        _symbols = false;
        _shift = false;
        _row = 0;
        _col = 0;
        _armedAt = null;
    }

    private void CloseLocked()
    {
        _open = false;
        _armedAt = null;
        _shift = false;
    }

    private int RowCountLocked() => (_symbols ? SymbolRows.Length : LetterRows.Length) + 1;

    private int RowLengthLocked(int row)
    {
        if (!_symbols && row < LetterRows.Length) return LetterRows[row].Length;
        if (_symbols && row < SymbolRows.Length) return SymbolRows[row].Length;
        return (_symbols ? SymbolActionRow : ActionRow).Length;
    }

    private void ClampLocked()
    {
        _row = Math.Clamp(_row, 0, RowCountLocked() - 1);
        _col = Math.Clamp(_col, 0, RowLengthLocked(_row) - 1);
    }

    private void MoveLocked(ControllerSnapshot previous, ControllerSnapshot current, long now)
    {
        var dx = 0;
        var dy = 0;

        if (Edge(previous, current, ButtonId.DpadLeft)) dx--;
        if (Edge(previous, current, ButtonId.DpadRight)) dx++;
        if (Edge(previous, current, ButtonId.DpadUp)) dy--;
        if (Edge(previous, current, ButtonId.DpadDown)) dy++;

        if (dx == 0 && dy == 0)
        {
            // Left stick with repeat: first tilt steps immediately.
            var sx = current.LeftStick.X > StickDeadzone ? 1
                : current.LeftStick.X < -StickDeadzone ? -1 : 0;
            var sy = current.LeftStick.Y < -StickDeadzone ? 1
                : current.LeftStick.Y > StickDeadzone ? -1 : 0;
            if (sx == 0 && sy == 0)
            {
                _lastStickDirX = _lastStickDirY = 0;
                return;
            }
            if (sx != _lastStickDirX || sy != _lastStickDirY || now - _lastStickMove >= StickRepeatMs)
            {
                dx = sx;
                dy = sy;
                _lastStickDirX = sx;
                _lastStickDirY = sy;
                _lastStickMove = now;
            }
            else return;
        }

        // Vertical moves keep the preferred column; horizontal wraps in-row.
        if (dy != 0)
        {
            _row = (_row + dy + RowCountLocked()) % RowCountLocked();
            _col = Math.Min(_col, RowLengthLocked(_row) - 1);
        }
        if (dx != 0)
        {
            var len = RowLengthLocked(_row);
            _col = ((_col + dx) % len + len) % len;
        }
    }

    private void ActivateLocked(SendInputInjector injector)
    {
        var actionRow = _symbols ? SymbolActionRow : ActionRow;
        var isActionRow = _row == RowCountLocked() - 1;
        if (isActionRow)
        {
            switch (actionRow[_col])
            {
                case "Space": injector.KeyTap(VirtualKey.Space); return;
                case "Bksp": injector.KeyTap(VirtualKey.Back); return;
                case "Enter": injector.KeyTap(VirtualKey.Enter); return;
                case "Sym": _symbols = true; ClampLocked(); return;
                case "ABC": _symbols = false; ClampLocked(); return;
                case "Shift": if (!_symbols) _shift = !_shift; return;
            }
            return;
        }

        if (_symbols)
        {
            var key = SymbolRows[_row][_col];
            if (key.Shift) injector.SendChord(new[] { VirtualKey.Shift }, key.Key);
            else injector.KeyTap(key.Key);
            return;
        }

        var c = LetterRows[_row][_col];
        var vk = (VirtualKey)char.ToUpperInvariant(c);
        if (_shift)
        {
            injector.SendChord(new[] { VirtualKey.Shift }, vk);
            _shift = false;
        }
        else injector.KeyTap(vk);
    }

    private List<List<string>> CurrentLabels()
    {
        var rows = new List<List<string>>();
        if (_symbols)
        {
            foreach (var r in SymbolRows)
            {
                var row = new List<string>();
                foreach (var k in r) row.Add(k.Label);
                rows.Add(row);
            }
            rows.Add(new List<string>(SymbolActionRow));
        }
        else
        {
            foreach (var r in LetterRows)
            {
                var row = new List<string>();
                foreach (var c in r) row.Add(_shift ? char.ToUpperInvariant(c).ToString() : char.ToLowerInvariant(c).ToString());
                rows.Add(row);
            }
            rows.Add(new List<string>(ActionRow));
        }
        return rows;
    }

    private static bool Edge(ControllerSnapshot previous, ControllerSnapshot current, ButtonId button) =>
        current.IsDown(button) && !previous.IsDown(button);

    public sealed record SymKey(string Label, VirtualKey Key, bool Shift);

    public sealed record KeyboardView(
        bool Open,
        string Page,
        List<List<string>> Rows,
        int SelectedRow,
        int SelectedCol,
        bool Shift);
}
