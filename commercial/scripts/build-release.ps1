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
$publishDirectory = Join-Path $artifactRoot 'win-x64'
$archivePath = Join-Path $artifactRoot 'AdbMirrorStudio-preview-win-x64.zip'

if (-not $artifactRoot.StartsWith($commercialRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw '拒绝清理商业项目目录以外的发布路径。'
}

if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

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
Copy-Item -LiteralPath (Join-Path $commercialRoot 'EULA.md') -Destination $publishDirectory
Copy-Item -LiteralPath (Join-Path $commercialRoot 'README.md') -Destination $publishDirectory
Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $archivePath -CompressionLevel Optimal

$hash = Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
[pscustomobject]@{
    Archive = $archivePath
    Sha256 = $hash.Hash
    SizeBytes = (Get-Item -LiteralPath $archivePath).Length
}
