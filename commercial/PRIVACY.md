# ADB Mirror Studio 隐私说明

ADB Mirror Studio 默认不收集、不上传用户数据，也不包含广告或第三方遥测 SDK。

应用仅在用户主动操作时通过本机 ADB 与已授权的 Android 设备通信。设备序列号、连接地址、所选文件路径和镜像内容不会发送给开发者。主题、刷新偏好等设置保存在 `%LOCALAPPDATA%\AdbMirrorStudio\settings.json`；未处理异常的日志保存在同目录的 `Crash` 文件夹。用户可以随时删除该目录清除本地数据。

如果未来版本增加可选在线更新、授权或崩溃上报，必须在启用前展示独立说明并取得用户同意。
