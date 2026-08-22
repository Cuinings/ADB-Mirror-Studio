# ADB Mirror Studio

ADB Mirror Studio 是面向 Windows 的 Android 设备连接、屏幕镜像、录屏、文件传输和环境诊断工作台。界面采用 WinUI 3 与 Desktop Acrylic，ADB、scrcpy 和应用运行时均可随便携包分发。

> 当前状态：功能候选版。核心功能可以运行和内部验收，但在完成正式品牌、代码签名、商业授权、在线更新服务及最终法律审核前，不应作为正式商业产品公开销售。

## 系统要求

- Windows 10 1809（Build 17763）或更高版本
- x64 处理器
- Android 5.0 或更高版本；音频转发通常要求 Android 11 或更高版本
- USB 连接需要可用的数据线和正确的设备驱动
- 无线连接要求电脑与手机网络互通

便携发布包为自包含版本，无需另外安装 Python、.NET、ADB 或 scrcpy。

## 功能概览

### 设备连接

- 异步刷新 USB 与无线 ADB 设备
- 支持 IPv4、IPv6 和主机名连接
- 支持 Android 11+ mDNS 发现和六位配对码安全无线配对
- USB 设备切换至指定 ADB TCP/IP 端口
- 保存最后一次成功连接的无线地址并可在启动时自动重连
- 设备状态、连接类型、型号和授权状态显示
- 设备重启与无线连接断开

### 镜像与录屏

- 基于 scrcpy 4.1 的低延迟镜像和控制
- 流畅、均衡、高清、演示四档预设
- MP4 或 MKV 镜像录制
- 单设备会话去重、会话列表、停止操作和退出回收
- 应用退出时清理由本应用启动的镜像进程

| 预设 | 最大尺寸 | 帧率 | 视频码率 | 适用场景 |
|---|---:|---:|---:|---|
| 流畅 | 1280 | 60 FPS | 4 Mbps | 网络或设备性能有限 |
| 均衡 | 1920 | 60 FPS | 8 Mbps | 日常操作，默认选项 |
| 高清 | 2560 | 60 FPS | 16 Mbps | 演示、截图和高画质需求 |
| 演示 | 1920 | 30 FPS | 8 Mbps | 全屏、置顶、关闭设备屏幕、只读展示 |

### 文件与 APK

- APK 覆盖安装，使用 `adb install -r` 保留现有应用数据
- 单文件、多文件选择和资源管理器拖放
- 文件队列、大小及逐项状态显示
- 将文件推送到设备 `/sdcard/Download/`
- 任务取消、失败隔离和完成统计
- 中文、空格及特殊字符路径作为独立进程参数安全传递

同名文件推送时由 ADB 覆盖目标文件。安装来源不明的 APK 前，应先验证发布者和文件哈希。

### 诊断、设置与隐私

- 检查随包 ADB、scrcpy、mDNS 服务和 PATH 版本冲突
- 跟随系统、浅色和深色主题
- 每 5 秒自动刷新和历史无线设备自动重连开关
- 设置使用原子 JSON 写入，降低异常退出导致配置损坏的风险
- 未处理异常写入本机崩溃日志
- 默认不包含遥测、广告 SDK 或自动日志上传
- 首次运行展示设备权限和数据使用说明

## 解压后运行

1. 完整解压 `AdbMirrorStudio-preview-win-x64.zip`，不要直接在压缩包中运行。
2. 进入解压后的目录。
3. 双击 `AdbMirrorStudio.App.exe`。
4. 阅读首次运行说明并选择“同意并开始”。

若 Windows SmartScreen 显示“未知发布者”，请先核对发布渠道提供的 SHA256。技术预览包尚未配置正式代码签名；正式商业包不应要求用户绕过签名警告。

## 连接设备

### USB 连接

1. 在手机中启用“开发者选项”和“USB 调试”。
2. 使用支持数据传输的 USB 线连接电脑。
3. 在手机弹窗中允许此电脑进行 USB 调试。
4. 在“设备中心”点击“刷新设备”。

显示“未授权”时，请解锁手机并处理授权弹窗。必要时在手机开发者选项中撤销 USB 调试授权，然后重新连接。

### Android 11+ 无线配对

1. 手机进入“开发者选项 → 无线调试”。
2. 选择“使用配对码配对设备”。
3. 在应用的安全无线配对区域输入手机显示的配对地址和六位配对码。
4. 配对成功后，使用无线调试主页面显示的连接地址建立连接。

配对端口与连接端口通常不同。

### USB 切换无线调试

1. 先确保 USB 设备在线且已授权。
2. 在设备卡片中点击“TCP/IP”。
3. 输入监听端口，默认 `5555`。
4. 获取手机当前局域网 IP，在无线地址中输入 `手机IP:端口` 后连接。
5. 无线连接成功后再拔出 USB 线。

不要把 ADB TCP/IP 端口暴露到公网。建议只在可信局域网中使用。

## 镜像与录屏

1. 展开设备中心的“镜像质量与录屏”。
2. 选择质量预设。
3. 如需录屏，点击“选择位置”并指定 `.mkv` 或 `.mp4` 文件。
4. 在在线设备卡片中点击“打开镜像”。
5. 可在“镜像会话”中查看或停止当前会话。

录屏由 scrcpy 进程完成。磁盘空间不足、输出文件被占用或设备断开会导致录制停止；异常详情会显示在应用状态区域。

## 文件和 APK 操作

1. 打开“文件与 APK”。
2. 选择目标设备。
3. 安装应用时点击“选择 APK”，再点击“安装 APK”。
4. 传输普通文件时点击“选择文件”进行多选，或将文件拖入本地文件卡片。
5. 点击“推送队列到 Download”。
6. 需要终止当前任务时点击“取消传输”。

取消会终止当前 ADB 子进程，已经完成的文件不会自动删除。

## 数据与日志

应用数据默认保存在：

```text
%LOCALAPPDATA%\AdbMirrorStudio\
  settings.json
  Crash\
    crash-yyyyMMdd-HHmmssfff.log
```

删除该目录会重置主题、自动刷新、自动重连、上次无线地址、镜像预设及首次运行状态。删除前请先退出应用。

相关文档：

- [隐私说明](PRIVACY.md)
- [最终用户许可协议模板](EULA.md)
- [第三方组件归属](THIRD-PARTY-NOTICES.md)

## 常见问题

### 应用无法启动

- 必须先完整解压，不能直接在 ZIP 内运行。
- 确认系统为 x64 Windows 10 1809 或更高版本。
- 检查 `%LOCALAPPDATA%\AdbMirrorStudio\Crash` 是否产生新日志。
- 杀毒软件可能隔离 ADB、scrcpy 或相关 DLL；应从可信渠道重新下载并核验哈希，不建议盲目添加全盘白名单。

### 未发现 USB 设备

- 更换支持数据传输的线缆或 USB 接口。
- 将手机 USB 用途切换为“文件传输”。
- 检查手机端 USB 调试授权。
- 安装设备厂商 USB 驱动。
- 在“诊断中心”检查 ADB 是否正常以及 PATH 中是否存在冲突版本。

### 无线连接失败

- 确认电脑与手机之间可以互相访问，访客 Wi-Fi 可能隔离设备。
- 确认使用的是连接端口，不是配对端口。
- Android 11+ 设备重启或关闭无线调试后，端口可能变化。
- VPN、防火墙和企业网络策略可能阻止连接。
- IPv6 地址应使用标准端点格式，例如 `[2001:db8::10]:5555`。

### 镜像窗口启动后立即退出

- 确认设备状态为“在线”。
- 在“诊断中心”检查 scrcpy 及其 DLL。
- 尝试“流畅”预设并关闭录屏。
- 某些厂商设备需要额外启用“USB 调试（安全设置）”才能注入触摸和键盘事件。

### APK 安装失败

- 检查设备剩余空间和 Android 版本要求。
- 签名不同的同包名应用不能直接覆盖安装。
- 企业设备策略可能禁止未知来源或 ADB 安装。
- `.apks`、`.xapk` 等分包容器不是单个 APK，当前安装入口不处理这类格式。

## 技术架构

```text
AdbMirrorStudio.Domain
  设备、镜像、设置领域模型
          ↓
AdbMirrorStudio.Application
  ADB、镜像、诊断、设置接口与业务协调
          ↓
AdbMirrorStudio.Infrastructure
  安全进程执行、ADB、scrcpy、JSON 持久化与诊断实现
          ↓
AdbMirrorStudio.App
  WinUI 3、Desktop Acrylic、页面和视图模型
```

关键设计原则：

- 不通过 shell 拼接 ADB 或 scrcpy 命令，全部使用独立 `ArgumentList`。
- 所有外部进程支持超时、取消和进程树清理。
- 设备刷新使用序列协调器丢弃过期结果。
- 单设备只保留一个由本应用管理的镜像会话。
- 设置先写临时文件，再以替换方式提交。
- 应用使用 Per-Monitor V2 DPI 感知，并依据当前显示器工作区限制默认窗口大小。

项目结构：

```text
commercial/
  src/
    AdbMirrorStudio.Domain/
    AdbMirrorStudio.Application/
    AdbMirrorStudio.Infrastructure/
    AdbMirrorStudio.App/
  tests/AdbMirrorStudio.UnitTests/
  scripts/build-release.ps1
  artifacts/release/
```

## 开发环境

- .NET 10 SDK
- 支持 .NET 10 和 WinUI 3 的 Visual Studio
- Windows 10/11 SDK
- PowerShell 7 或 Windows PowerShell 5.1

仓库根目录已有项目专用 SDK 时，可运行：

```powershell
.\.tools\dotnet\dotnet.exe --info
```

## 构建与测试

在仓库根目录运行测试：

```powershell
.\.tools\dotnet\dotnet.exe test `
  .\commercial\tests\AdbMirrorStudio.UnitTests\AdbMirrorStudio.UnitTests.csproj `
  --configuration Release
```

构建未打包 WinUI 应用：

```powershell
.\.tools\dotnet\dotnet.exe build `
  .\commercial\src\AdbMirrorStudio.App\AdbMirrorStudio.App.csproj `
  --configuration Release `
  --runtime win-x64 `
  /p:Unpackaged=true
```

在 `commercial` 目录生成完整便携包：

```powershell
.\scripts\build-release.ps1
```

发布脚本会：

1. 清理 `commercial/artifacts/release`。
2. 运行 Release 单元测试，失败时终止发布。
3. 生成 Windows x64 自包含发布目录。
4. 复制 README、隐私说明、EULA 和第三方归属文件。
5. 创建 `AdbMirrorStudio-preview-win-x64.zip`。
6. 输出 ZIP 的 SHA256 和字节大小。

发布产物：

```text
commercial\artifacts\release\
  win-x64\
    AdbMirrorStudio.App.exe
    Tools\
    Licenses\
    README.md
    PRIVACY.md
    EULA.md
    THIRD-PARTY-NOTICES.md
  AdbMirrorStudio-preview-win-x64.zip
```

## 第三方组件

- Android Debug Bridge 1.0.41 / Platform Tools 37.0.0
- scrcpy 4.1
- SDL 3.4.12
- FFmpeg 8.1.2 对应的 62.x 动态库
- libusb 1.0.30

正式发行必须保留第三方版权声明、适用许可文本和源码获取方式。不得删除或隐藏开源组件归属。

## 正式商用发布检查表

- [ ] 确认正式产品名、公司法定名称、联系信息和品牌资产
- [ ] 将 MSIX Publisher 与代码签名证书主体保持一致
- [ ] 使用可信代码签名证书签署 EXE、MSIX 或安装程序
- [ ] 确定永久、订阅或试用授权模式并配置验证公钥或服务
- [ ] 配置 HTTPS 更新清单和已签名安装包下载地址
- [ ] 完成中文、英文资源及语言切换验收
- [ ] 补充适用地区、退款、支持、责任限制和最终 EULA
- [ ] 补齐 SDL、FFmpeg、libusb 的许可原文和源码获取义务
- [ ] 运行 Windows App Certification Kit
- [ ] 在 Windows 10/11、100%–250% DPI、多显示器环境测试
- [ ] 覆盖 Android 5–16、USB、Android 11+ 无线调试和主流厂商设备
- [ ] 执行弱网、拔线、设备重启、磁盘不足、权限拒绝及长时间运行测试
- [ ] 从干净系统完成安装、升级、降级和卸载验证
- [ ] 发布 SHA256、版本说明、隐私政策和支持渠道

未完成以上项目时，构建产物应继续标记为 Preview，不应宣称为正式商用发行版。
