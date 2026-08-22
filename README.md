# ADB Mirror Studio

面向 Windows 的 Android 设备连接、屏幕镜像、录屏、文件传输与环境诊断工作台。

![ADB Mirror Studio 文件与 APK 页面](commercial/artifacts/files-feature-smoke.png)

## 下载

当前版本：`V1.0.0`

- [GitHub Release](https://github.com/Cuinings/ADB-Mirror-Studio/releases/tag/V1.0.0)
- [下载 Windows x64 自包含版](https://github.com/Cuinings/ADB-Mirror-Studio/releases/download/V1.0.0/AdbMirrorStudio-V1.0.0-win-x64.zip)

SHA256：

```text
A4EC87B780F285BC8F8CEF172DCD429DF3F7E1A079DEE20836D57C8089660A4F
```

## 主要功能

- USB、IPv4、IPv6、主机名连接
- Android 11+ mDNS 发现与六位配对码无线配对
- 流畅、均衡、高清、演示四档 scrcpy 镜像预设
- MP4/MKV 录屏和镜像会话管理
- APK 覆盖安装
- 多选、拖放、逐项状态和可取消文件传输队列
- USB 设备切换 ADB TCP/IP
- 设备重启与历史无线地址自动重连
- ADB、scrcpy、mDNS 和 PATH 冲突诊断
- WinUI 3、Desktop Acrylic、浅色/深色主题和 DPI 自适应
- 本地设置、首次运行说明与崩溃日志

## 系统要求

- Windows 10 1809（Build 17763）或更高版本
- x64 处理器
- Android 5.0 或更高版本
- USB 调试或无线调试已启用

发布包已包含 .NET、Windows App SDK、ADB 和 scrcpy，无需另外安装 Python 或 .NET。

## 快速开始

1. 下载并完整解压 `AdbMirrorStudio-V1.0.0-win-x64.zip`。
2. 双击 `AdbMirrorStudio.App.exe`。
3. 在手机中开启“开发者选项”和“USB 调试”。
4. 首次连接时在手机端允许 USB 调试授权。
5. 在应用中刷新设备并点击“打开镜像”。

请勿直接在 ZIP 压缩包中运行程序。

## 无线配对

Android 11+：

1. 打开“开发者选项 → 无线调试”。
2. 选择“使用配对码配对设备”。
3. 在应用中输入配对地址和六位配对码。
4. 配对完成后，使用无线调试主页面显示的连接地址建立连接。

配对端口与连接端口通常不同。不要把 ADB TCP/IP 端口暴露到公网。

## 构建

需要 .NET 10 SDK 和 Windows 10/11 SDK。

运行测试：

```powershell
.\.tools\dotnet\dotnet.exe test `
  .\commercial\tests\AdbMirrorStudio.UnitTests\AdbMirrorStudio.UnitTests.csproj `
  --configuration Release
```

生成自包含发行包：

```powershell
.\commercial\scripts\build-release.ps1
```

输出目录：

```text
commercial\artifacts\release\
```

## 技术架构

- `AdbMirrorStudio.Domain`：设备、镜像和设置领域模型
- `AdbMirrorStudio.Application`：业务接口与协调逻辑
- `AdbMirrorStudio.Infrastructure`：ADB、scrcpy、进程、诊断和持久化实现
- `AdbMirrorStudio.App`：WinUI 3 页面与视图模型
- `AdbMirrorStudio.UnitTests`：命令参数、解析、设置和并发刷新测试

外部命令使用独立参数列表执行，不通过 shell 拼接；支持超时、取消和进程树清理。

## 文档

- [完整使用、开发和发布手册](commercial/README.md)
- [V1.0.0 发行说明](commercial/RELEASE-NOTES-V1.0.0.md)
- [隐私说明](commercial/PRIVACY.md)
- [最终用户许可协议模板](commercial/EULA.md)
- [第三方组件归属](commercial/THIRD-PARTY-NOTICES.md)

## 发行状态

V1.0.0 是功能候选版本。正式商业分发前仍需配置可信代码签名、正式发行主体、最终法律文本、完整第三方许可材料、商业授权和在线更新服务。

本项目仅应用于用户拥有或已获明确授权的 Android 设备。不得用于未经授权的访问、监控或数据复制。
