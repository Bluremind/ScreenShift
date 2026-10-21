# ScreenShift — 四边独立裁切版本

本仓库是 [Bluremind/ScreenShift](https://github.com/Bluremind/ScreenShift) 的 fork，增加 **左、上、右、下四个边界独立调整**，用于将完整 Windows 桌面放到损坏屏幕仍可用的矩形区域内。原作者：Bluremind；原版权声明和许可证保持不变。

> 本版本的程序界面仍为英文／俄文。下方保留的原项目介绍、截图和官方下载链接描述的是上游版本；上游 EXE 不包含本 fork 的四边修改。本 fork 目前没有发布修改版 EXE。

## 本版本的改动

- 四边裁切计算、四条边界预览、四边像素数显示及配置保存。
- 兼容原来的三项配置，读取时将上、右裁切设为 0。
- 支持 1 像素微调；取消每边最多裁切三分之一的限制。
- 校准界面保留至少 640×360 的可用区域；屏幕某维度更小时保留该维度全长。
- 保留应用后 20 秒内确认、超时恢复和手动重置功能。

## 操作方法

| 按键 | 操作 |
| --- | --- |
| ← / → | 移动左边界 |
| ↑ / ↓ | 移动下边界 |
| Ctrl + ← / → | 移动右边界 |
| Ctrl + ↑ / ↓ | 移动上边界 |
| Shift + 上述移动按键 | 每次 20 像素（默认 4 像素） |
| Alt + 上述移动按键 | 每次 1 像素 |
| Tab | 切换 Crisp / Fill |
| Enter | 应用；20 秒内点击 Keep 保存，否则恢复 |
| R | 移除裁切并删除保存配置 |
| Esc | 取消校准 |

边界按箭头方向移动。Crisp 在可用区域内居中放置能容纳的最大标准分辨率，按 1:1 显示；Fill 保持原屏幕比例缩放，可能仍有额外黑边。仅支持矩形区域和一个活动显示器，实际可用性取决于显卡驱动。

## 从源码编译

使用 Windows 的 .NET Framework 4.x C# 编译器，无需安装 Visual Studio、.NET SDK 或 NuGet 包。在仓库目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\dist\ScreenShift.exe
```

输出文件为 `dist\ScreenShift.exe`。验证几何计算、边界快捷键和预览绘制：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\four-edges.ps1
```

这些检查不会改变显示设置。实际显卡应用与超时恢复流程需要在目标设备上交互验证。

## 保存配置

文件位置：`%LOCALAPPDATA%\ScreenShift\settings.txt`。

新格式为 `左 上 右 下 模式`，例如 `100 50 200 150 fill`。旧格式 `左 下 模式` 仍然兼容。初次运行默认四边均为 0。

- `ScreenShift.exe --apply`：直接应用已保存配置，不显示确认窗口。
- `ScreenShift.exe --reset`：恢复完整屏幕并删除配置。
- 修改配置文件后建议先打开校准界面预览，再按 Enter 确认。

## 下载与许可

GitHub Releases 可以附加 EXE 供下载，和 Git 源码提交相互独立；`.gitignore` 忽略 `*.exe` 不影响 Release 附件。

但本项目的 [LICENSE](LICENSE) 第 3、4 条限制修改版分发和提供下载，需取得作者书面许可。本 fork 暂不提供修改版 EXE，先向上游提交功能贡献供审查。原作者的正式版本可从 [上游 Releases](https://github.com/Bluremind/ScreenShift/releases) 下载。公开源码不等于允许任意再分发，个人使用及其他用途请遵循原许可证。

---

## 上游项目介绍（保留原文）

<div align="center">

# ScreenShift

**Your laptop screen is partly dead? Put the whole Windows desktop back on the part that still works.**

A tiny tool, free for personal use, that moves and fits the *entire* desktop (wallpaper, icons, Start button, taskbar) into the working area of a damaged panel. No drivers. No installer. One 22 KB file, in English and Russian.

[![Release](https://img.shields.io/github/v/release/Bluremind/ScreenShift?style=flat-square&color=2ea043)](https://github.com/Bluremind/ScreenShift/releases/latest)
[![License: free for personal use](https://img.shields.io/badge/license-free%20for%20personal%20use-orange?style=flat-square)](LICENSE)
[![Commercial license](https://img.shields.io/badge/all%20other%20use-paid%20license-blueviolet?style=flat-square)](COMMERCIAL.md)
![Windows 10+](https://img.shields.io/badge/Windows-10%20%2B-0078d4?style=flat-square)
![Size](https://img.shields.io/badge/size-22%20KB-8957e5?style=flat-square)
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
- **Calibrate in 30 seconds.** Move four yellow lines with the arrow keys until they touch the dead strip.
- **Safe by design.** After applying, you have 20 seconds to press **Keep**. Do nothing and the old picture comes back on its own.
- **Remembers your setup.** Re-apply it any time with `ScreenShift.exe --apply`.
- **Two quality modes.** *Crisp* shows the desktop 1:1 at the largest standard resolution that fits. *Fill* uses the whole good area and scales the picture slightly.
- **English and Russian.** The interface follows your Windows language. Force one with `--lang=en` or `--lang=ru`.
- **Tiny and private.** One small exe, no installer, no network access, no telemetry. The code is a single readable C# file.

## Quick start

1. Download **`ScreenShift.exe`** from the [latest release](https://github.com/Bluremind/ScreenShift/releases/latest).
2. Run it. A calibration screen opens.
3. Use the arrow keys to move the **yellow lines** next to the dead strips. Hold **Shift** to move faster.
4. Press **Tab** to choose *Crisp* or *Fill*, then **Enter** to apply.
5. Press **Keep** within 20 seconds. Done.

<div align="center">
<img src="assets/calibration-en.png" alt="The calibration screen: yellow lines mark the new edges of the picture, red hatching marks what will turn black." width="720">
<br>
<sub>The calibration screen (rendered by the program itself, shown at half size). Yellow lines are the new edges of the picture.</sub>
</div>

> Windows may show a SmartScreen warning because the exe is not code-signed. See [Trust and verification](#trust-and-verification).

## Commands

| Command | What it does |
| --- | --- |
| `ScreenShift.exe` | Open calibration. Enter applies, R removes the shift, Esc exits. |
| `ScreenShift.exe --apply` | Re-apply the saved shift without asking. Handy for startup. |
| `ScreenShift.exe --reset` | Remove the shift and delete the saved settings. |
| `--lang=en` or `--lang=ru` | Add to any command to choose the interface language. |

Settings are stored in `%LOCALAPPDATA%\ScreenShift\settings.txt` as five values: left, top, right, bottom (pixels), and the mode (`fill` or `crisp`). Original three-value settings (left, bottom, mode) are still accepted, with top and right set to zero.

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

**Is it free?**
Yes, for personal use: you, on your own computer, for your own private purposes. Everything else needs a paid commercial license: companies, freelancers, schools, charities and other organizations, work computers, and any service such as repairing other people's laptops. See [License](#license).

**Which languages does it speak?**
English and Russian. ScreenShift picks Russian when Windows is set to Russian and English otherwise. Use `--lang=en` or `--lang=ru` to override it. All texts live in one small `L.T("ru", "en")` table in the source, so adding another language is easy, see [Contributing](#contributing).

## Trust and verification

ScreenShift changes display settings, so you should be able to check what you run.

- The whole program is one file: [`ScreenShift.cs`](ScreenShift.cs), about 450 lines. It makes no network calls and writes only `settings.txt`.
- Build it yourself in seconds with the C# compiler that ships with Windows, no SDK needed:

  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
  ```

- The release page lists the **SHA-256** of the published exe.
- The exe is not code-signed, so SmartScreen may warn on first run. Choose *More info*, then *Run anyway*, or build from source.

## Contributing

Issues and pull requests are welcome. By submitting a contribution you agree that it is distributed under the project's [license](#license), including the commercial licenses offered by the author. Good first contributions:

- More interface languages (English and Russian are built in)
- Support for multiple displays
- Reports of which GPUs and laptops work
- An optional "start with Windows" switch

## License

| | |
| --- | --- |
| **Personal use** | **Free.** An individual, on their own computer, for their own private purposes, under the [ScreenShift Personal Use License](LICENSE). |
| **Everything else** | **Paid license.** Companies, freelancers, schools, charities and other organizations, work computers, any service, redistribution. See [COMMERCIAL.md](COMMERCIAL.md). |

The source code is public so you can read and verify it, but this is not an open-source license in the OSI sense.

**Contact for a commercial license:** [bluremind@gmail.com](mailto:bluremind@gmail.com?subject=ScreenShift%20commercial%20license) · [GitHub issues](https://github.com/Bluremind/ScreenShift/issues) for questions and bug reports.
<div align="center">

If ScreenShift saved your laptop, a ⭐ helps other people find it.

</div>

## Four-edge calibration (local modification)

All four edges can be adjusted independently. The usable area is rectangular;
Fill preserves the panel aspect ratio and may leave additional black margins.
Crisp centers the largest fitting standard resolution inside the selected area.

| Keys | Action |
| --- | --- |
| Left / Right | Move the left boundary |
| Up / Down | Move the bottom boundary |
| Ctrl + Left / Right | Move the right boundary |
| Ctrl + Up / Down | Move the top boundary |
| Shift + movement | Move 20 pixels instead of 4 |
| Alt + movement | Move 1 pixel |
| Tab | Toggle Crisp / Fill |
| Enter | Apply, then confirm Keep within 20 seconds |
| R / Esc | Reset / cancel |

Boundaries move in the direction of the arrow. Calibration retains at least
640 x 360 pixels (or the whole panel dimension if smaller) for the instructions.
There is no longer a one-third-of-panel limit on each edge.
New installations start with zero cuts. The example settings `100 50 200 150 fill`
exclude 100 pixels left, 50 top, 200 right, and 150 bottom. Editing the settings
file alone does not change the display; open calibration to preview and confirm.
The older screenshots above show the original two-edge UI.

Build and run the geometry/UI checks from the project directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\four-edges.ps1
.\dist\ScreenShift.exe
```

The checks do not apply display configurations. Actual GPU acceptance and the
confirmation/revert flow must be checked interactively on the target display.