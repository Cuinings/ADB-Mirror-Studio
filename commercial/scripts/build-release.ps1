param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$commercialRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$workspaceRoot = (Resolve-Path (Join-Path $commercialRoot '..')).Path
$localDotnet = Join-Path $workspaceRoot '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }
$project = Join-Path $commercialRoot 'src\AdbMirrorStudio.App\AdbMirrorStudio.App.csproj'
$tests = Join-Path $commercialRoot 'tests\AdbMirrorStudio.UnitTests\AdbMirrorStudio.UnitTests.csproj'
$artifactRoot = Join-Path $commercialRoot 'artifacts\release'
[xml]$buildProperties = Get-Content (Join-Path $commercialRoot 'Directory.Build.props')
$versionNode = $buildProperties.SelectSingleNode('/Project/PropertyGroup/Version')
if ($versionNode -eq $null -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw 'Directory.Build.props 中缺少 Version。'
}
$productVersion = "V$($versionNode.InnerText.Trim())"
$archivePath = Join-Path $artifactRoot "AdbMirrorStudio-$productVersion-win-x64.zip"
$stagingDirectory = Join-Path $artifactRoot ".staging-$([Guid]::NewGuid().ToString('N'))"
$publishDirectory = Join-Path $stagingDirectory 'win-x64'

if (-not $artifactRoot.StartsWith($commercialRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
    -not $stagingDirectory.StartsWith($artifactRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw '拒绝清理项目目录以外的发布路径。'
}

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
try {
    & $dotnet test $tests --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw '测试失败，停止发布。' }

    & $dotnet publish $project `
        --configuration $Configuration `
        --runtime win-x64 `
        --self-contained true `
        --output $publishDirectory `
        /p:Unpackaged=true `
        /p:PublishSingleFile=false `
        /p:PublishTrimmed=false
    if ($LASTEXITCODE -ne 0) { throw '发布构建失败。' }

    Copy-Item -LiteralPath (Join-Path $commercialRoot 'THIRD-PARTY-NOTICES.md') -Destination $publishDirectory
    Copy-Item -LiteralPath (Join-Path $commercialRoot 'PRIVACY.md') -Destination $publishDirectory
    Copy-Item -LiteralPath (Join-Path $commercialRoot 'FREE-USE-LICENSE.md') -Destination $publishDirectory
    Copy-Item -LiteralPath (Join-Path $commercialRoot 'README.md') -Destination $publishDirectory
    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal

    $hash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
    [pscustomobject]@{
        Version = $productVersion
        Archive = $archivePath
        Sha256 = $hash.Hash
        SizeBytes = (Get-Item -LiteralPath $archivePath).Length
    }
}
finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
}
