using System.Diagnostics;
using VibeOS.App.Windows;
using VibeOS.Core.Input;
using VibeOS.Core.Pointer;

namespace VibeOS.App.Pointer;

/// <summary>
/// Owns the ~120 Hz pointer loop. Deliberately isolated on its own thread: a
/// slow accessibility call must never be able to stall the cursor (spec §5.2,
/// PRD §84.6).
///
/// Left stick drives the cursor, right stick scrolls, and both are live at the
/// same time — neither gates the other (spec §5.4).
///
/// Sub-pixel remainders are accumulated so slow stick movement produces smooth
/// motion rather than being truncated to zero every tick.
/// </summary>
public sealed class PointerEngine : IDisposable
{
    private const int TargetHz = 120;
    private const double ScrollNotchesPerSecond = 15d;
    private const double ScrollGamma = 2.0d;

    private readonly SendInputInjector _injector;
    private readonly PointerSettings _settings;

    private Thread? _thread;
    private volatile bool _running;
    private volatile bool _enabled = true;

    private readonly object _stickGate = new();
    private StickVector _cursorStick = StickVector.Zero;
    private StickVector _scrollStick = StickVector.Zero;
    private bool _precision;

    private double _remainderX, _remainderY;
    private double _scrollRemainderX, _scrollRemainderY;

    public PointerEngine(SendInputInjector injector, PointerSettings settings)
    {
        _injector = injector;
        _settings = settings;
    }

    public void UpdateInput(StickVector cursorStick, StickVector scrollStick, bool precision)
    {
        lock (_stickGate)
        {
            _cursorStick = cursorStick;
            _scrollStick = scrollStick;
            _precision = precision;
        }
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
        {
            lock (_stickGate)
            {
                _cursorStick = StickVector.Zero;
                _scrollStick = StickVector.Zero;
            }
            _remainderX = _remainderY = 0d;
            _scrollRemainderX = _scrollRemainderY = 0d;
        }
    }

    public void Start()
    {
        if (_thread is not null) return;
        _running = true;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "VibeOS.Pointer",
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(500);
        _thread = null;
    }

    private void Loop()
    {
        var clock = Stopwatch.StartNew();
        var lastTicks = clock.ElapsedTicks;
        var frameMs = (int)Math.Max(1d, 1000d / TargetHz);

        while (_running)
        {
            var nowTicks = clock.ElapsedTicks;
            var dt = (nowTicks - lastTicks) / (double)Stopwatch.Frequency;
            lastTicks = nowTicks;

            if (dt > 0.25d) dt = 0.25d; // don't lurch after a stall

            if (_enabled) Tick(dt);

            Thread.Sleep(frameMs);
        }
    }

    private void Tick(double dt)
    {
        StickVector cursorStick, scrollStick;
        bool precision;
        lock (_stickGate)
        {
            cursorStick = _cursorStick;
            scrollStick = _scrollStick;
            precision = _precision;
        }

        // Both sticks are live simultaneously — cursor on the left, scroll on
        // the right (spec §5.4). Neither gates the other.
        TickCursor(cursorStick, precision, dt);
        TickScroll(scrollStick, dt);
    }

    private void TickCursor(StickVector stick, bool precision, double dt)
    {
        var (vx, vy) = PointerCurve.Velocity(stick, _settings, precision);
        if (vx == 0d && vy == 0d)
        {
            _remainderX = _remainderY = 0d;
            return;
        }

        // Screen Y grows downward; the stick's Y is positive-up.
        var totalX = _remainderX + vx * dt;
        var totalY = _remainderY + (-vy) * dt;

        var stepX = (int)Math.Truncate(totalX);
        var stepY = (int)Math.Truncate(totalY);

        _remainderX = totalX - stepX;
        _remainderY = totalY - stepY;

        if (stepX != 0 || stepY != 0)
            _injector.MoveRelative(stepX, stepY);
    }

    private void TickScroll(StickVector stick, double dt)
    {
        if (stick.Magnitude <= _settings.Deadzone)
        {
            _scrollRemainderX = _scrollRemainderY = 0d;
            return;
        }

        // Nonlinear so a page can be crossed quickly without repeated flicks (PRD §16).
        var curveY = Math.Sign(stick.Y) * Math.Pow(Math.Abs(stick.Y), ScrollGamma);
        var curveX = Math.Sign(stick.X) * Math.Pow(Math.Abs(stick.X), ScrollGamma);

        var totalY = _scrollRemainderY + curveY * ScrollNotchesPerSecond * dt;
        var totalX = _scrollRemainderX + curveX * ScrollNotchesPerSecond * dt;

        var notchesY = (int)Math.Truncate(totalY);
        var notchesX = (int)Math.Truncate(totalX);

        _scrollRemainderY = totalY - notchesY;
        _scrollRemainderX = totalX - notchesX;

        if (notchesY != 0) _injector.ScrollVertical(notchesY);
        if (notchesX != 0) _injector.ScrollHorizontal(notchesX);
    }

    public void Dispose() => Stop();
}
