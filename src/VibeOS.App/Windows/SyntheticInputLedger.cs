namespace VibeOS.App.Windows;

public enum MouseButton { Left, Right, Middle }

/// <summary>
/// The authoritative record of every synthetic input we have pressed but not
/// released. PRD §47 makes this mandatory: no stuck Ctrl, no stuck Shift, no
/// stuck mouse button, ever.
/// </summary>
public sealed class SyntheticInputLedger
{
    private readonly object _gate = new();
    private readonly HashSet<MouseButton> _mouseDown = new();
    private readonly HashSet<ushort> _keysDown = new();

    public event Action<MouseButton>? MouseReleaseRequested;
    public event Action<ushort>? KeyReleaseRequested;

    public void RegisterMouseDown(MouseButton button)
    {
        lock (_gate) _mouseDown.Add(button);
    }

    public void RegisterMouseUp(MouseButton button)
    {
        lock (_gate) _mouseDown.Remove(button);
    }

    public void RegisterKeyDown(ushort virtualKey)
    {
        lock (_gate) _keysDown.Add(virtualKey);
    }

    public void RegisterKeyUp(ushort virtualKey)
    {
        lock (_gate) _keysDown.Remove(virtualKey);
    }

    /// <summary>Releases everything still held. Safe to call repeatedly.</summary>
    public void ReleaseAll()
    {
        MouseButton[] mice;
        ushort[] keys;

        lock (_gate)
        {
            mice = _mouseDown.ToArray();
            keys = _keysDown.ToArray();
            _mouseDown.Clear();
            _keysDown.Clear();
        }

        foreach (var m in mice) MouseReleaseRequested?.Invoke(m);
        foreach (var k in keys) KeyReleaseRequested?.Invoke(k);
    }

    public bool IsAnythingHeld
    {
        get { lock (_gate) return _mouseDown.Count > 0 || _keysDown.Count > 0; }
    }
}
