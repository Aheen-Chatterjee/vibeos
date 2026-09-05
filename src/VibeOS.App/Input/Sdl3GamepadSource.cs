using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using VibeOS.Core.Input;
using static SDL3.SDL;

namespace VibeOS.App.Input;

/// <summary>
/// Reads the first connected gamepad through SDL3's gamepad abstraction, so the
/// mapping is controller-independent (PRD §49). Produces immutable snapshots
/// that the pure core consumes.
/// </summary>
public sealed class Sdl3GamepadSource : IDisposable
{
    private const short AxisMax = 32767;
    private const float TriggerButtonThreshold = 0.5f;

    private IntPtr _gamepad = IntPtr.Zero;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public event Action<bool>? ConnectionChanged;

    public bool IsConnected => _gamepad != IntPtr.Zero;

    public bool Initialize()
    {
        // Keep receiving controller input while VibeOS is not the focused app.
        SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");

        if (!SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD))
        {
            Console.Error.WriteLine($"[VibeOS] SDL_Init failed: {SDL_GetError()}");
            return false;
        }

        TryOpenFirstGamepad();
        return true;
    }

    private void TryOpenFirstGamepad()
    {
        // SDL_GetGamepads returns a malloc'd array of SDL_JoystickID (uint32)
        // that the caller owns.
        var listPtr = SDL_GetGamepads(out var count);
        if (listPtr == IntPtr.Zero) return;

        try
        {
            if (count <= 0) return;

            var instanceId = unchecked((uint)Marshal.ReadInt32(listPtr));
            _gamepad = SDL_OpenGamepad(instanceId);

            if (_gamepad != IntPtr.Zero)
            {
                Console.WriteLine($"[VibeOS] Gamepad connected: {SDL_GetGamepadName(_gamepad)}");
                ConnectionChanged?.Invoke(true);
            }
            else
            {
                Console.Error.WriteLine($"[VibeOS] SDL_OpenGamepad failed: {SDL_GetError()}");
            }
        }
        finally
        {
            SDL_free(listPtr);
        }
    }

    public ControllerSnapshot Poll()
    {
        // Drain SDL's event queue so hotplug is noticed.
        while (SDL_PollEvent(out var e))
        {
            switch ((SDL_EventType)e.type)
            {
                case SDL_EventType.SDL_EVENT_GAMEPAD_ADDED:
                    if (_gamepad == IntPtr.Zero) TryOpenFirstGamepad();
                    break;

                case SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED:
                    if (_gamepad != IntPtr.Zero)
                    {
                        SDL_CloseGamepad(_gamepad);
                        _gamepad = IntPtr.Zero;
                        Console.WriteLine("[VibeOS] Gamepad disconnected.");
                        ConnectionChanged?.Invoke(false);
                    }
                    break;
            }
        }

        if (_gamepad == IntPtr.Zero)
            return ControllerSnapshot.Empty;

        var pressed = ImmutableHashSet.CreateBuilder<ButtonId>();

        void Btn(SDL_GamepadButton sdlButton, ButtonId id)
        {
            if (SDL_GetGamepadButton(_gamepad, sdlButton)) pressed.Add(id);
        }

        Btn(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH, ButtonId.A);
        Btn(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST, ButtonId.B);
        Btn(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST, ButtonId.X);
        Btn(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH, ButtonId.Y);
        Btn(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER, ButtonId.LB);
        Btn(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER, ButtonId.RB);
        Btn(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK, ButtonId.Back);
        Btn(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START, ButtonId.Start);
        Btn(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK, ButtonId.L3);
        Btn(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK, ButtonId.R3);
        Btn(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP, ButtonId.DpadUp);
        Btn(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN, ButtonId.DpadDown);
        Btn(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT, ButtonId.DpadLeft);
        Btn(SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT, ButtonId.DpadRight);

        var lt = Normalize01(SDL_GetGamepadAxis(_gamepad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER));
        var rt = Normalize01(SDL_GetGamepadAxis(_gamepad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER));
        if (lt >= TriggerButtonThreshold) pressed.Add(ButtonId.LT);
        if (rt >= TriggerButtonThreshold) pressed.Add(ButtonId.RT);

        var left = ReadStick(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY);
        var right = ReadStick(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY);

        return new ControllerSnapshot(
            pressed.ToImmutable(), left, right, lt, rt,
            _clock.ElapsedMilliseconds, connected: true);
    }

    private StickVector ReadStick(SDL_GamepadAxis xAxis, SDL_GamepadAxis yAxis)
    {
        var x = SDL_GetGamepadAxis(_gamepad, xAxis) / (float)AxisMax;
        // SDL reports Y positive-down; the rest of VibeOS uses positive-up.
        var y = -SDL_GetGamepadAxis(_gamepad, yAxis) / (float)AxisMax;
        return new StickVector(Math.Clamp(x, -1f, 1f), Math.Clamp(y, -1f, 1f));
    }

    private static float Normalize01(short raw) => Math.Clamp(raw / (float)AxisMax, 0f, 1f);

    public void Dispose()
    {
        if (_gamepad != IntPtr.Zero)
        {
            SDL_CloseGamepad(_gamepad);
            _gamepad = IntPtr.Zero;
        }
        SDL_Quit();
    }
}
