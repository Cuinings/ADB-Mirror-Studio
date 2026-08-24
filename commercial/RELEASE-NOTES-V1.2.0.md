# ADB Mirror Studio V1.2.0（开发中）

`V1.2.0` 是保持现有安装、设置和操作方式兼容的免费功能版本。

## 新增

- 检测到新版本后，可以在“关于 → 免费更新”中直接下载 Windows x64 安装包。
- 显示实时下载进度，并允许用户取消尚未完成的下载。
- 下载完成后启动安装程序并正常关闭当前应用，安装程序继续执行覆盖升级。
- 已验证的完整安装包会保存在 `%LOCALAPPDATA%\AdbMirrorStudio\Updates\版本号`，再次操作时可以复用。

## 安全与回退

- 只接受固定 GitHub 仓库通过 HTTPS 提供、名称符合正式规则的 Windows x64 安装包。
- 必须同时核对 GitHub Release API 提供的文件大小和 SHA256；任何一项不一致都会删除临时文件并停止安装。
- 下载使用 `.download` 临时文件，完整校验通过后才更名为 `.exe`，不会启动不完整文件。
- Release 缺少安装包或 SHA256 时禁用直接安装，仍可打开发布页面手动处理。
- 安装程序尚未配置 Authenticode 代码签名，Windows 可能显示“未知发布者”；界面会在下载前明确提醒。

## 版本说明

- 当前公开稳定版仍为 `V1.1.0`。
- 本功能属于用户可感知的新流程，按版本规则升级为次版本 `V1.2.0`。

## 本地验证

- Release 单元测试：57/57 通过。
- WinUI x64 Release 与 XAML 编译：0 个警告、0 个错误。
- 已使用真实公开 V1.1.0 Release 验证安装包名称、状态、大小、HTTPS 地址和 GitHub SHA256 元数据均可被识别。
- NSIS 安装器编译成功，产品版本为 `V1.2.0`，文件版本为 `1.2.0.0`。

```text
ADB-Mirror-Studio-Setup-V1.2.0-win-x64.exe
大小：79997685 字节
SHA256：A832870A8F0C3B5AF57D38545720E55BA03CB18537506E0301A52874D2F0F7E9

AdbMirrorStudio-V1.2.0-win-x64.zip
大小：117279477 字节
SHA256：AF9EF562AE1891201C3F805CBA3FA069A1D5EBF0D26C9F3AB4B2F6A83B2DE719
```
