<div align="center">

# ScreenShift

**Your laptop screen is partly dead? Put the whole Windows desktop back on the part that still works.**

A tiny, free, open-source tool that moves and fits the *entire* desktop (wallpaper, icons, Start button, taskbar) into the working area of a damaged panel. No drivers. No installer. One 18 KB file.

[![Release](https://img.shields.io/github/v/release/Bluremind/ScreenShift?style=flat-square&color=2ea043)](https://github.com/Bluremind/ScreenShift/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue?style=flat-square)](LICENSE)
![Windows 10+](https://img.shields.io/badge/Windows-10%20%2B-0078d4?style=flat-square)
![Size](https://img.shields.io/badge/size-18%20KB-8957e5?style=flat-square)
![No telemetry](https://img.shields.io/badge/telemetry-none-lightgrey?style=flat-square)

[**Download**](https://github.com/Bluremind/ScreenShift/releases/latest) · [Русский](README.ru.md) · [How it works](#how-it-works) · [FAQ](#faq)

<img src="assets/hero.svg" alt="Before: part of the desktop is lost in a dead strip. After: the whole desktop fits the working area." width="860">

</div>

---

## The problem

A cracked corner, a failing display cable, a MacBook with the infamous "flexgate" dead strip, a panel that went black on one side. The laptop still works, but the edge of the picture is gone, and with it the **Start button, the leftmost desktop icons and half of every maximized window**.

Windows has no setting for "this part of my screen is dead". Moving windows around doesn't help, because the taskbar and the desktop still sit under the dead strip.

## The fix

ScreenShift asks Windows' display stack to draw the desktop inside a smaller rectangle of the panel. Everything is moved together, so nothing hides under the dead area. The dead strips simply stay black.

- **Everything fits.** Taskbar, Start button, icons, maximized windows.
- **Calibrate in 30 seconds.** Drag two yellow lines with the arrow keys until they touch the dead strip.
- **Safe by design.** After applying, you have 20 seconds to press **Keep**. Do nothing and the old picture comes back on its own.
- **Remembers your setup.** Re-apply it any time with `ScreenShift.exe --apply`.
- **Two quality modes.** *Crisp* shows the desktop 1:1 at the largest standard resolution that fits. *Fill* uses the whole good area and scales the picture slightly.
- **Tiny and private.** One small exe, no installer, no network access, no telemetry. The code is a single readable C# file.

## Quick start

1. Download **`ScreenShift.exe`** from the [latest release](https://github.com/Bluremind/ScreenShift/releases/latest).
2. Run it. A calibration screen opens.
3. Use the arrow keys to move the **yellow lines** next to the dead strips. Hold **Shift** to move faster.
4. Press **Tab** to choose *Crisp* or *Fill*, then **Enter** to apply.
5. Press **Keep** within 20 seconds. Done.

> Windows may show a SmartScreen warning because the exe is not code-signed. See [Trust and verification](#trust-and-verification).

## Commands

| Command | What it does |
| --- | --- |
| `ScreenShift.exe` | Open calibration. Enter applies, R removes the shift, Esc exits. |
| `ScreenShift.exe --apply` | Re-apply the saved shift without asking. Handy for startup. |
| `ScreenShift.exe --reset` | Remove the shift and delete the saved settings. |

Settings are stored in `%LOCALAPPDATA%\ScreenShift\settings.txt` as three values: pixels cut on the left, pixels cut at the bottom, and the mode.

### Re-apply after every boot

Windows or the GPU driver can drop the shift after a restart, sleep or driver reset. To restore it automatically, put a shortcut to `ScreenShift.exe --apply` in your Startup folder (press `Win + R`, type `shell:startup`).

## How it works

ScreenShift uses the Windows display configuration API (`SetDisplayConfig`, virtual-mode aware). It keeps the physical panel mode and changes two things:

1. the size of the **desktop** Windows renders, and
2. the **rectangle on the panel** where that desktop is shown.

The GPU then scales or places the desktop into that rectangle in hardware. Windows itself believes it has a normal screen, so apps and the taskbar behave as usual. Calibration happens on an overlay that uses real panel pixels, so the lines you see are exactly where the picture will start.

## Compatibility

| | |
| --- | --- |
| **OS** | Windows 10 (version 1607 or newer) |
| **Runtime** | .NET Framework 4.x, which is already part of Windows 10 |
| **Displays** | One active display. With an external monitor connected, ScreenShift stops with a message and changes nothing. |
| **Tested on** | MacBook Pro (2016) running Windows 10 via Boot Camp, AMD Radeon Pro 460 |

Support for the virtual display modes depends on the GPU driver. If Windows rejects the mode, ScreenShift tells you and leaves your display unchanged. Reports from other hardware are very welcome, please open an issue.

## FAQ

**Does this fix the broken screen?**
No. It is a software workaround that makes a damaged screen usable. The dead strip stays dead. Replace the panel or cable when you can.

**Is it safe? Can I get stuck on a black screen?**
The change is applied as a trial. Without your confirmation it reverts after 20 seconds. If you ever need to undo it manually, run `ScreenShift.exe --reset`, or just reboot if you never pressed **Keep**.

**Will text look blurry?**
In *Crisp* mode the desktop is shown 1:1 at a standard resolution, so text stays sharp. *Fill* mode scales the picture by a few percent. Use Tab during calibration to compare.

**Why not just lower the resolution?**
A lower resolution is stretched across the whole panel, so the dead strip still eats part of the picture. ScreenShift decides *where* on the panel the picture goes.

**Why not AMD or NVIDIA scaling options?**
Those usually center the picture, which wastes good pixels on the opposite side, and are often missing for built-in laptop panels. ScreenShift lets you cut each side by exactly the amount you need.

**The interface is in Russian. Will there be English?**
The calibration screen is currently in Russian. The keys are shown on screen and listed above. Translations are a great first contribution, see [Contributing](#contributing).

## Trust and verification

ScreenShift changes display settings, so you should be able to check what you run.

- The whole program is one file: [`ScreenShift.cs`](ScreenShift.cs), about 420 lines. It makes no network calls and writes only `settings.txt`.
- Build it yourself in seconds with the C# compiler that ships with Windows, no SDK needed:

  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
  ```

- The release page lists the **SHA-256** of the published exe.
- The exe is not code-signed, so SmartScreen may warn on first run. Choose *More info*, then *Run anyway*, or build from source.

## Contributing

Issues and pull requests are welcome. Good first contributions:

- English (and other) interface translations
- Support for multiple displays
- Reports of which GPUs and laptops work
- An optional "start with Windows" switch

## License

[MIT](LICENSE). Free for personal and commercial use.

<div align="center">

If ScreenShift saved your laptop, a ⭐ helps other people find it.

</div>
