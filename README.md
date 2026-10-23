# ScreenShift v1.3 — 屏幕裁切与兼容分辨率 / Screen cropping and compatible resolutions

将完整 Windows 桌面放进损坏屏幕仍可用的矩形区域。基于 [Bluremind/ScreenShift](https://github.com/Bluremind/ScreenShift)，原作者 Bluremind。需要 Windows 10+、一个活动显示器及兼容显卡驱动。

Fit the entire Windows desktop into the usable rectangle of a damaged screen. Based on [Bluremind/ScreenShift](https://github.com/Bluremind/ScreenShift), originally by Bluremind. Requires Windows 10+, one active display and a compatible graphics driver.

[中文](#中文) · [English](#english) · [Repository](https://github.com/wangcanhao-cmd/ScreenShift)

<a id="中文"></a>

## v1.3 更新说明

上次四边裁切贡献 [PR #1](https://github.com/Bluremind/ScreenShift/pull/1) 已被原作者合并。本次将之后的开发与测试改动统一整理为 **v1.3**，以下描述当前实现。

- **可选保持比例：** Fill 默认保持原屏幕比例；取消勾选可拉伸铺满选区，但会改变文字和图像比例。
- **兼容分辨率：** 从 Windows 枚举当前显示设备的模式，逐一验证分辨率与选区的组合，再保留最接近选区比例的一组。同组不同尺寸全部保留，显示 `4:3`、`5:4`、`16:9`、`9:16` 等比例；近似比例使用 `≈`。
- **避免不可用选项：** 不提供猜测的超宽预设。调整边界后重新验证，没有可用模式时显示提示。应用前再次验证，失败时显示错误码并保存诊断日志。
- **五种语言：** Language 下拉框顺序为 English、简体中文、Русский、日本語、한국어，可即时切换并保存选择。
- **竖屏适配：** 四种旋转方向均进行双向坐标转换；界面使用旋转后的尺寸，底层显示配置使用旋转前的尺寸与矩形，修复竖屏启动 1610 错误。
- **配置与构建：** 保存比例与分辨率选择，兼容旧配置；增加回归测试、可选代码签名和 SHA-256 校验。

四边裁切、Crisp / Fill、默认 4 像素移动、Shift 20 像素、Alt 1 像素、应用后 20 秒确认及超时恢复均保留。校准区域至少为 640×360；某维度更小的屏幕保留该维度全长。

### 操作方法

| 按键 | 操作 |
| --- | --- |
| ← / → | 移动左边界 |
| ↑ / ↓ | 移动下边界 |
| Ctrl + ← / → | 移动右边界 |
| Ctrl + ↑ / ↓ | 移动上边界 |
| Shift / Alt + 移动键 | 每次 20 / 1 像素 |
| Tab | 切换 Crisp / Fill |
| A | 切换保持原比例（Fill 且未启用选区分辨率时） |
| D | 切换选区兼容分辨率 |
| F6 / F7 | 聚焦分辨率 / 语言下拉框 |
| Enter | 应用；20 秒内点击 Keep 保存，否则恢复 |
| R / Esc | 重置 / 取消 |

Crisp 按 1:1 显示可容纳的原屏幕比例模式；Fill 可等比缩放或拉伸。下拉框聚焦时方向键用于选择，点击校准背景后继续调整边界。

改变系统方向前请关闭校准窗口，设置方向后重新打开并调整四边。保存的裁切值不会自动映射到旋转后的损坏边缘。程序使用 Windows 显示配置接口，不要求 NVIDIA 显卡；可用模式取决于当前设备和驱动，没有实现独立虚拟显示驱动。

### 编译与验证

使用 Windows 自带的 .NET Framework 4.x C# 编译器，无需 Visual Studio、现代 .NET SDK 或 NuGet：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\four-edges.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\compatibility-language.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\rotation.ps1
.\dist\ScreenShift.exe
```

测试不应用显示设置。AMD Radeon 780M 的 90° 竖屏全屏配置与 13 个系统模式在测试裁切区域通过 `SDC_VALIDATE`，不等于所有硬件都已验证。实际显示位置、应用与恢复仍需在目标设备交互确认。

### 配置与命令

配置位于 `%LOCALAPPDATA%\ScreenShift\settings.txt`，语言位于同目录 `language.txt`。

当前八项格式为 `左 上 右 下 crisp|fill aspect|stretch area|original 分辨率键`。枚举模式键为 `宽×10000+高`，例如 1920×1080 为 `19201080`。兼容旧三、五、六项格式及宽度值；建议通过界面保存，不手动猜测分辨率。

- `--apply`：直接应用保存配置，不显示确认窗口。
- `--reset`：恢复全屏并删除裁切配置。
- `--lang=zh`：指定语言，可用 `en`、`zh`、`ru`、`ja`、`ko`。

<a id="english"></a>

## v1.3 release notes

The previous four-edge contribution, [PR #1](https://github.com/Bluremind/ScreenShift/pull/1), was merged upstream. All subsequent development and testing changes are consolidated into **v1.3**. These notes describe the current implementation.

- **Optional aspect preservation:** Fill preserves proportions by default. Disable it to stretch across the selected area, which may distort text and images.
- **Compatible resolutions:** Enumerate the current device's Windows modes, validate each resolution and crop combination, then retain the closest accepted aspect family. Keep all sizes in that family, with labels such as `4:3`, `5:4`, `16:9` and `9:16`; rounded approximations use `≈`.
- **Validated choices:** No guessed ultrawide presets. Edge changes refresh validation. An empty list displays an explanation. Validate again before applying, with error codes and diagnostic logging on failure.
- **Five languages:** English, Simplified Chinese, Russian, Japanese and Korean, in that dropdown order, with live switching and saved preference.
- **Portrait support:** Convert visible geometry to and from pre-rotation display coordinates for all four orientations, fixing portrait startup error 1610.
- **Settings and builds:** Persist aspect and resolution choices while accepting legacy settings; include regression tests, optional signing and SHA-256 checksums.

Four-edge calibration, Crisp / Fill, 4-pixel steps, Shift for 20 pixels, Alt for 1 pixel, and 20-second confirmation/revert remain available. Calibration retains at least 640×360 pixels, or the full panel dimension if smaller.

### Controls

| Keys | Action |
| --- | --- |
| Left / Right | Move left edge |
| Up / Down | Move bottom edge |
| Ctrl + Left / Right | Move right edge |
| Ctrl + Up / Down | Move top edge |
| Shift / Alt + movement | Move 20 / 1 pixels |
| Tab | Toggle Crisp / Fill |
| A | Toggle aspect preservation in Fill without area resolution |
| D | Toggle compatible area resolution |
| F6 / F7 | Focus resolution / language dropdown |
| Enter | Apply; Keep within 20 seconds to save, otherwise revert |
| R / Esc | Reset / cancel |

Crisp uses a fitting mode with the panel's aspect at 1:1 pixels; Fill scales proportionally or stretches. Arrow keys navigate focused dropdowns; click the background to resume edge adjustment.

Close calibration before changing orientation in Windows, then reopen and adjust edges. Saved cuts are not remapped to rotated damage. The Windows display-configuration API works across graphics vendors; modes depend on the device and driver. No NVIDIA-specific API or custom virtual display driver is required.

### Build, validation and settings

Use the build and three test commands above in Windows PowerShell. The build uses the .NET Framework C# compiler and produces `dist\ScreenShift.exe`; Visual Studio, the modern .NET SDK and NuGet are unnecessary. Tests do not apply display settings. On an AMD Radeon 780M at 90°, identity and 13 enumerated modes passed `SDC_VALIDATE` for a sample crop. Visual placement, application and recovery still require interactive hardware testing.

Settings: `%LOCALAPPDATA%\ScreenShift\settings.txt`; language: `language.txt` beside it. Eight-value format: `left top right bottom crisp|fill aspect|stretch area|original resolution-key`. Explicit keys encode `width*10000+height`, e.g. `19201080` for 1920×1080. Legacy three-, five- and six-value settings and width-only keys remain readable. Prefer saving through the UI.

- `--apply`: apply saved settings without a confirmation dialog.
- `--reset`: restore the full screen and delete crop settings.
- `--lang=en`: select `en`, `zh`, `ru`, `ja` or `ko` at startup.

## 签名与校验 / Signing and verification

版本标识为 `1.3.0-four-edge`。普通构建输出未签名 EXE 和 SHA-256 文件。以下命令创建或复用本地开发证书，将签名副本放入 `dist\self-signed`：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\self-sign.ps1 -Publisher Hao
Get-AuthenticodeSignature .\dist\self-signed\ScreenShift.exe
Get-FileHash .\dist\self-signed\ScreenShift.exe -Algorithm SHA256
```

自签名使用当前用户证书库、不可导出私钥和 SHA-256，不安装受信任根证书、不添加可信时间戳。需要受信任签名时取得相应证书，并使用 `build.ps1 -Sign -CertificateThumbprint ... -SignToolPath ... -TimestampUrl ...`。不要提交私钥或密码。

The identifier is `1.3.0-four-edge`. Normal builds are unsigned with a SHA-256 checksum. The script creates/reuses a local certificate with a non-exportable private key and signs a separate copy. No trusted root or trusted timestamp is added. For trusted signing, configure an appropriate certificate and Windows SDK SignTool, then provide the build script's signing options. Never commit private keys or passwords. Signing does not bypass Gmail attachment restrictions or guarantee SmartScreen reputation.

## 下载、贡献与许可 / Downloads, contribution and license

原作者 Bluremind。Copyright (c) 2026 Bluremind。保留并遵循 [LICENSE](LICENSE) 与 [COMMERCIAL.md](COMMERCIAL.md)。本 fork 暂不发布修改版 EXE；修改版分发须取得许可要求的书面授权。原作者正式版本见 [上游 Releases](https://github.com/Bluremind/ScreenShift/releases)。上次 PR 已合并不代表后续版本获准分发。

Original author: Bluremind. Copyright (c) 2026 Bluremind. Original [LICENSE](LICENSE) and [COMMERCIAL.md](COMMERCIAL.md) apply. This fork offers no modified executable release. Obtain required written permission before distribution. Official builds are available from [upstream Releases](https://github.com/Bluremind/ScreenShift/releases). The previous merged PR does not imply permission to distribute later modified executables.
