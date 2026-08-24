param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipPortableBuild,
    [string]$NsisCompilerPath = $env:NSIS_COMPILER,
    [string]$SignCommand = $env:ADB_MIRROR_SIGN_COMMAND
)

$ErrorActionPreference = 'Stop'
$commercialRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$releaseScript = Join-Path $PSScriptRoot 'build-release.ps1'
$installerScript = Join-Path $commercialRoot 'installer\AdbMirrorStudio.nsi'
$installerRoot = Join-Path $commercialRoot 'artifacts\installer'
$releaseRoot = Join-Path $commercialRoot 'artifacts\release'
$stagingDirectory = Join-Path $installerRoot ".staging-$([Guid]::NewGuid().ToString('N'))"
$payloadDirectory = Join-Path $stagingDirectory 'payload'
$licensePath = Join-Path $stagingDirectory 'FREE-USE-LICENSE.txt'

[xml]$buildProperties = Get-Content (Join-Path $commercialRoot 'Directory.Build.props')
$versionNode = $buildProperties.SelectSingleNode('/Project/PropertyGroup/Version')
$fileVersionNode = $buildProperties.SelectSingleNode('/Project/PropertyGroup/FileVersion')
if ($versionNode -eq $null -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw 'Directory.Build.props 中缺少 Version。'
}
$version = $versionNode.InnerText.Trim()
$fileVersion = if ($fileVersionNode -ne $null) { $fileVersionNode.InnerText.Trim() } else { "$version.0" }
$productVersion = "V$version"
$portableArchive = Join-Path $releaseRoot "AdbMirrorStudio-$productVersion-win-x64.zip"
$installerName = "ADB-Mirror-Studio-Setup-$productVersion-win-x64.exe"
$installerPath = Join-Path $installerRoot $installerName

if (-not $installerRoot.StartsWith($commercialRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not $stagingDirectory.StartsWith($installerRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw '拒绝清理项目目录以外的安装器路径。'
}

if (-not $SkipPortableBuild) {
    & $releaseScript -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw '便携发行包构建失败，停止安装器构建。' }
}
if (-not (Test-Path -LiteralPath $portableArchive)) {
    throw "未找到便携发行包：$portableArchive"
}

if ([string]::IsNullOrWhiteSpace($NsisCompilerPath)) {
    $command = Get-Command 'makensis.exe' -ErrorAction SilentlyContinue
    if ($command) { $NsisCompilerPath = $command.Source }
}
if ([string]::IsNullOrWhiteSpace($NsisCompilerPath)) {
    $compilerCandidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\NSIS\makensis.exe'),
        (Join-Path $env:ProgramFiles 'NSIS\makensis.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe')
    )
    $NsisCompilerPath = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($NsisCompilerPath) -or -not (Test-Path -LiteralPath $NsisCompilerPath)) {
    throw '未找到 NSIS 编译器。请安装 NSIS 3，或通过 NSIS_COMPILER 指定 makensis.exe。'
}

New-Item -ItemType Directory -Path $payloadDirectory -Force | Out-Null
try {
    Expand-Archive -LiteralPath $portableArchive -DestinationPath $payloadDirectory -Force
    foreach ($requiredFile in @('AdbMirrorStudio.App.exe', 'Tools\adb.exe', 'Tools\scrcpy.exe', 'README.md', 'FREE-USE-LICENSE.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $payloadDirectory $requiredFile))) {
            throw "安装器负载缺少必要文件：$requiredFile"
        }
    }

    $privacyText = Get-Content (Join-Path $commercialRoot 'PRIVACY.md') -Raw
    $licenseText = Get-Content (Join-Path $commercialRoot 'FREE-USE-LICENSE.md') -Raw
    $installerNotice = "$privacyText`r`n`r`n========================================`r`n`r`n$licenseText"
    [System.IO.File]::WriteAllText($licensePath, ($installerNotice -replace "`r?`n", "`r`n"), [System.Text.UTF8Encoding]::new($true))
    $payloadBytes = (Get-ChildItem $payloadDirectory -Recurse -File | Measure-Object -Property Length -Sum).Sum
    $estimatedSizeKb = [Math]::Ceiling($payloadBytes / 1KB)

    New-Item -ItemType Directory -Path $installerRoot -Force | Out-Null
    if (Test-Path -LiteralPath $installerPath) {
        Remove-Item -LiteralPath $installerPath -Force
    }

    $compilerArguments = @(
        '/INPUTCHARSET',
        'UTF8',
        '/WX',
        '/V3',
        "/DAPP_VERSION=$version",
        "/DAPP_FILE_VERSION=$fileVersion",
        "/DSOURCE_DIR=$payloadDirectory",
        "/DLICENSE_FILE=$licensePath",
        "/DOUTPUT_DIR=$installerRoot",
        "/DESTIMATED_SIZE_KB=$estimatedSizeKb"
    )
    if (-not [string]::IsNullOrWhiteSpace($SignCommand)) {
        if (-not $SignCommand.Contains('%1', [System.StringComparison]::Ordinal)) {
            throw '签名命令必须包含由 NSIS 替换为目标文件路径的 %1 占位符。'
        }
        $compilerArguments += "/DSIGN_COMMAND=$SignCommand"
    }
    $compilerArguments += $installerScript

    & $NsisCompilerPath @compilerArguments
    if ($LASTEXITCODE -ne 0) { throw 'NSIS 编译失败。' }
    if (-not (Test-Path -LiteralPath $installerPath)) { throw "安装器未生成：$installerPath" }

    $hash = Get-FileHash -LiteralPath $installerPath -Algorithm SHA256
    $signature = Get-AuthenticodeSignature -LiteralPath $installerPath
    [pscustomobject]@{
        Version = $productVersion
        Installer = $installerPath
        Sha256 = $hash.Hash
        SizeBytes = (Get-Item -LiteralPath $installerPath).Length
        SignatureStatus = $signature.Status
    }
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}
