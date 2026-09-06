# VibeOS

**Turn a game controller into a first-class Windows input system.** VibeOS lets you drive the mouse, fire shortcuts, dictate text, type on a virtual keyboard, and switch apps — all from an Xbox-compatible pad, no keyboard required.

```
Left stick → mouse          Right stick → scroll (simultaneous)
RT / LT    → left / right click (drag-ready)      LB → precision
A / B      → Enter / Escape      Y hold → dictate      RB+Y → dictate + submit
LB + RB    → radial wheel        D-pad up hold → keyboard
L3 + R3    → suspend / resume    D-pad → free (per-app)
```

---

## Features

### 🖱️ Physical mouse (M2)
- 120 Hz dedicated pointer thread — a stalled app can never freeze your cursor
- Radial deadzone + gamma acceleration: light tilt for pixels, full tilt to cross monitors
- Nonlinear scroll on the right stick, horizontal + vertical, live at the same time as the cursor
- True down/up clicks, so dragging, text selection, and window moves just work

### ⌨️ Shortcut engine (M4)
- Modifier-first chords (`LB+X` → copy, `RB+B` → quick open…) resolved centrally — no double-firing, no base-key leaks
- Tap / hold / double-tap discrimination per button
- Fully data-driven: edit `config/vibeos.jsonc` while running, changes hot-reload; malformed edits keep last-known-good
- GUI edits land in `config/user.jsonc` — commented defaults are never rewritten

### ☸️ Radial weapon wheel (M4.5)
- Hold LB+RB → five wheels: **Apps · This app · Edit · Keys · Nav**
- Right stick aims by continuous angle, D-pad cycles wheels, release executes, B cancels
- Every slot badged (`Ctrl+C`, `switch`, `latch`…); app slots **focus open windows instead of launching dupes**
- Keys wheel carries Backspace/Enter/Tab/Esc/Space plus **sticky Shift/Ctrl/Win** (tap to latch, next action consumes)

### 🎙️ In-house GPU dictation (M5–M8, OpenWhispr-free)
- Whisper STT on your GPU (Vulkan; verified on RTX 4050), model auto-downloads once and stays resident
- Optional local-LLM cleanup through Ollama with a developer-tuned prompt (code/paths/commands preserved character-exact)
- Terminal profile dictates **verbatim** (no LLM risk for commands)
- B cancels mid-hold; per-profile voice dictionaries ride as the STT prompt

### ⌨️ Virtual keyboard (M6)
- D-pad-Up hold opens a click-through overlay — focus never leaves your app
- Letters, numbers, symbols, arrows, Tab/Esc, Shift latch; D-pad or stick to move, A to type

### 🪟 App profiles (M7)
- Chrome, Edge, VS Code, Cursor, Terminal, Explorer — tab control, palettes, terminals, per-app wheels
- Switches on foreground-window change with a toast in the log

### 🖥️ GUI + tray
- Status window (state, controller, profile, voice, sticky, live log), full **Controls** reference, and **Customize** tab that edits bindings/wheels/voice live
- Tray icon (green = active), suspend/resume, reload, config folder, start-with-Windows toggle
- Single-instance guard, `--tray` start hidden

### 🛡️ Coexistence engineering
- Windows translates pad input into `VK_GAMEPAD_*` keys that XAML apps (Terminal, Settings) consume natively — VibeOS **suppresses that stream while active** (user-mode hook, no driver), so there's no double-driving and no suspend dance
- Every synthetic key is ledgered and force-released on suspend, disconnect, lock, or crash — stuck keys are architecturally impossible
- Rare-only haptics: suspend, wheel open/execute, voice lifecycle, sticky — never pointer, clicks, or traversal

---

## Run it

**Requirements:** Windows 10/11 · Xbox-compatible controller (XInput) · [.NET 9 SDK](https://dotnet.microsoft.com/download) (build only) · NVIDIA GPU recommended (CPU works, slower) · [Ollama](https://ollama.com) + `ollama pull qwen3:1.7b` for transcript cleanup (optional)

```powershell
cd D:\SideProjects\ControllerOS\vibeos-app
dotnet build src/VibeOS.App
dotnet run --project src/VibeOS.App
# or launch the exe directly:
# src\VibeOS.App\bin\Debug\net9.0-windows\VibeOS.exe [--tray]
```

First voice use downloads `base.en` (~140 MB) to `%LOCALAPPDATA%\VibeOS\models` once. Open the GUI from the tray icon; tick **Start with Windows** for login autostart (single instance is enforced).

## Configure

| File | Purpose |
|---|---|
| `config/vibeos.jsonc` | Global bindings, wheels, voice — commented, safe to hand-edit |
| `config/apps/*.jsonc` | Per-app overrides (`chrome`, `msedge`, `code`, `cursor`, `terminal`, `explorer`) |
| `config/user.jsonc` | GUI-managed overrides (git-ignored) — wins ties, replaces wheels, merges voice |

Chord grammar: `"LB+X"`, trigger last; values accept `"copy"`, `{ "key": "CTRL+P" }`, or `{ "action": "undo", "mode": "Release" }` (modes: Press/Release/Hold/DoubleTap). Run `powershell -NoProfile -File ..\scripts\validate-config.ps1` to lint.

## Layout

```
vibeos-app/
├── src/VibeOS.Core/      # pure logic: chords, holds, pointer curve, sticky, radial math (+41 xUnit tests)
├── src/VibeOS.App/       # Windows host
│   ├── Input/            # SDL3 pad source, rumble, haptics, OS key suppression
│   ├── Windows/          # SendInput injector, input ledger, window switching
│   ├── Pointer/          # 120 Hz pointer thread
│   ├── Actions/          # gestures, action router, slot hints
│   ├── Profiles/         # JSONC config + hot reload + user overrides
│   ├── Overlay/          # wheel + keyboard controllers and layered windows
│   ├── Voice/            # mic, local STT, cleanup, insertion, dictation engine
│   └── Gui/              # tray, status window, autostart
├── config/               # default configuration
└── tests/                # unit tests
```

## Status

M1 (input router) · M2 (mouse) · M4 (shortcuts) · M4.5 (wheel) · M5/M8 (local voice) · M6 (keyboard) · M7 (profiles) · GUI · suppression — **done and hardware-tested.** Deferred to V2: M3 semantic UIA navigation. See `../progress.md` and `../docs/superpowers/` for the full record.
