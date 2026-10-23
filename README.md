<div align="center">

<img src="docs/icon.png" width="96" alt="BetterDarkScreen icon" />

# BetterDarkScreen

**Dim your screens below their lowest brightness. Per monitor, on a schedule, also in games.**

A tiny Windows tray app written in C#. Minimal Win11 look, near-zero CPU, no telemetry.

![Windows 10/11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows)
![.NET 8](https://img.shields.io/badge/.NET-8-512BD4?logo=dotnet)
![License: MIT](https://img.shields.io/badge/License-MIT-green)

</div>

---

## Why

Monitors at 0 % brightness are often still too bright at night. Dark mode helps apps, but not videos, games or the desktop.
BetterDarkScreen puts one dimming control per monitor in your tray. It picks the best way to dim each screen, and it can turn itself on and off by the clock or with the sun.

## Features

- 🌙 **Per-monitor dimming**: 0–95 %, separate on/off switches, optionally linked sliders
- 🧠 **Four ways to dim**, chosen globally or **per monitor**:

  | Method | How it works | Best for | Limits |
  |---|---|---|---|
  | **Overlay** | Click-through black layer composited by DWM | Desktop, video, windowed and borderless games | Can't cover exclusive fullscreen, Start menu, UAC, cursor |
  | **Gamma** | Scales the GPU gamma ramp (keeps your color calibration) | Exclusive-fullscreen games | Windows stops near 50 % (one-click unlock), no effect in HDR |
  | **Hardware** | Monitor's own backlight via DDC/CI (laptops: Windows brightness API) | Best image quality, no color loss | Monitor must support DDC/CI; slower response |
  | **Auto** *(default)* | Overlay normally, gamma for the game's monitor in exclusive fullscreen | Set it and forget it | — |

  Choose **Custom** to give every monitor its own method. Unsupported methods are detected and marked, and the app falls back to overlay.
- 🕘 **Schedule**: always on, custom hours (e.g. 22:00 to 07:00), or sunset to sunrise. Sun times are calculated offline from latitude and longitude, with offsets.
- 🌅 **Smooth fades** at the start and end of the schedule (0–90 min), plus soft transitions when you move a slider
- ⏸️ **Pause** for 15 min / 1 h / 3 h / until you resume
- 🎮 **Game friendly**: no injection, no hooks into games, so it's safe with anti-cheat. Detects exclusive-fullscreen D3D apps.
- 🎥 **Hidden from capture**: screenshots, OBS and Discord streams stay bright (overlay mode)
- ⌨️ **Configurable global hotkeys**, with conflicts shown
- 🎨 **Native look**: follows light/dark mode and your accent color, Fluent icons, rounded corners
- 🪶 **Lightweight**: idle timer ticks every 15–30 s. Windows are created only when needed. Memory is trimmed after you close the UI.
- 🔒 **Private**: no network access, no telemetry, settings in one JSON file

## Install

### Download

Get `BetterDarkScreen.exe` from the [Releases](../../releases) page. It is a single self-contained file, so you don't need to install .NET.
Run it and a moon appears in your tray. Turn on **Settings → General → Start with Windows** if you want it at login.

### Build from source

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (**SDK 8.0.x → Windows x64 installer**).

```powershell
git clone <this repo>
cd BetterDarkScreen
.\build.ps1              # -> publish\BetterDarkScreen.exe  (self-contained, single file)
.\build.ps1 -Small       # compressed exe: ~3x smaller file, slightly more RAM
dotnet run --project src # debug run
```

## Usage

| Where | What |
|---|---|
| **Left-click** the tray moon | Quick panel: per-monitor sliders and switches, method, pause, status |
| **Right-click** the tray moon | Menu: open panel, on/off, pause, settings, exit |
| `BetterDarkScreen.exe --settings` | Open settings directly |
| Run the exe again while it's running | Opens the quick panel |

### Default hotkeys

All of these can be changed (or cleared) in **Settings → Hotkeys**.

| Action | Default |
|---|---|
| Dim more | `Ctrl` + `Alt` + `PgDn` |
| Dim less | `Ctrl` + `Alt` + `PgUp` |
| Turn dimming on / off | `Ctrl` + `Alt` + `End` |
| Pause 1 hour / resume | *not set* |
| Open quick panel | *not set* |

The step size (1–20 %) is adjustable.

### Settings window

| Page | Contents |
|---|---|
| **Schedule** | Always / custom hours / sunset to sunrise, location, offsets, fade length |
| **Dimming** | Global method, linked sliders, hide from capture, gamma unlock |
| **Displays** | Per-monitor method and detected support (✓ / ✗ for hardware and gamma) |
| **Hotkeys** | Record shortcuts, step size |
| **General** | Start with Windows, settings file location |

Settings live in `%AppData%\BetterDarkScreen\settings.json`.

## Troubleshooting

<details>
<summary><b>Gamma won't go darker than about 50 %</b></summary>

Windows refuses gamma ramps that deviate too much from normal. Open **Settings → Dimming → Deeper gamma dimming → Unlock**. This sets `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM\GdiIcmGammaRange = 256` (admin prompt). Sign out or reboot for it to take effect. The quick panel shows a warning when you hit the limit.
</details>

<details>
<summary><b>My monitor shows "Hardware ✗" / "no DDC/CI"</b></summary>

Turn on **DDC/CI** in the monitor's on-screen menu (often under *System* or *Other*), then replug the cable or restart the app. Some monitors, docks, KVMs and HDMI adapters don't pass DDC/CI at all. Overlay or gamma still work there.
</details>

<details>
<summary><b>The overlay doesn't show in a game</b></summary>

The game runs in exclusive fullscreen. Use **Auto** (switches that monitor to gamma automatically) or **Gamma**, or set the game to borderless windowed.
</details>

<details>
<summary><b>Gamma does nothing</b></summary>

Gamma ramps are ignored in **HDR** mode and by some drivers. Use overlay or hardware for that monitor (Settings → Displays).
</details>

<details>
<summary><b>Input lag in borderless games?</b></summary>

A topmost overlay can stop DirectFlip/MPO, which may add about one frame of latency. If that matters, use Gamma or Hardware for that monitor.
</details>

<details>
<summary><b>The app crashed. Is my screen stuck dark?</b></summary>

No. Gamma is reset on any crash. Hardware brightness is remembered in the settings file and restored the next time the app starts.
</details>

## How it works

```
TrayApp ── NotifyIcon, hotkeys, popup menu, panel and settings windows
   │
DimController ── schedule × level × pause → target per monitor, smooth animation,
   │             fullscreen detection (SHQueryUserNotificationState + WinEvent hook)
   ├── OverlayWindow  WS_EX_LAYERED | TRANSPARENT | TOPMOST | NOACTIVATE, one alpha value,
   │                  WDA_EXCLUDEFROMCAPTURE
   ├── GammaRamp      SetDeviceGammaRamp per display, scales the original (calibrated) ramp,
   │                  finds the OS limit by binary search, re-applied every 3 s while active
   └── HwBrightness   DDC/CI VCP 0x10 (dxva2) and \\.\LCD IOCTLs on a background thread,
                      coalesced writes, crash-safe restore
```

### Project layout

```
src/
  Program.cs          single instance, crash safety
  TrayApp.cs          tray icon, menu, hotkeys, startup
  DimController.cs    core engine
  OverlayWindow.cs    overlay method
  GammaRamp.cs        gamma method
  HwBrightness.cs     hardware method
  Displays.cs         monitor enumeration + friendly names
  Schedule.cs         schedule + offline sunrise/sunset (NOAA)
  Settings.cs         JSON settings (source-generated)
  Hotkey.cs           hotkey parse / format
  Native.cs           P/Invoke
  Ui/                 theme, owner-drawn controls, panel, popup menu, settings
```

The UI is drawn by hand on WinForms (no WPF, no third-party packages), so the app starts fast and stays small.

## Contributing

Issues and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).
Useful things to report: monitor model and whether DDC/CI worked, GPU and driver, Windows build, HDR on or off.

## License

[MIT](LICENSE) © 2026 Alex Makarov
