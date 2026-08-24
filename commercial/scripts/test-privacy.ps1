param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$originalLocation = Get-Location

try {
    Set-Location -LiteralPath $repositoryRoot
    $trackedFiles = @(git ls-files)
    if ($LASTEXITCODE -ne 0) { throw '无法读取 Git 跟踪文件列表。' }

    $forbiddenPathPattern = '(?i)(^|/)(\.env($|\.)|settings\.json$|credentials\.json$|secrets\.json$|[^/]+\.(pdb|dbg|dmp|log|etl|pfx|p12|pem|key|snk|jks|keystore)$)'
    $forbiddenPaths = @($trackedFiles | Where-Object { $_ -match $forbiddenPathPattern })
    if ($forbiddenPaths.Count -gt 0) {
        Write-Error "检测到不应被 Git 跟踪的文件：$($forbiddenPaths -join ', ')"
    }

    $textExtensions = @(
        '', '.cs', '.csproj', '.gitignore', '.gitattributes', '.json', '.md', '.nsi',
        '.props', '.ps1', '.sln', '.targets', '.txt', '.xaml', '.xml', '.yaml', '.yml'
    )
    $sensitivePatterns = [ordered]@{
        '访问令牌' = '(?i)(github_pat_[A-Za-z0-9_]+|gh[pousr]_[A-Za-z0-9]{20,}|sk-[A-Za-z0-9_-]{20,}|AKIA[0-9A-Z]{16})'
        '私钥' = '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----'
        '硬编码凭据' = '(?i)(password|passwd|secret|token|api[_-]?key)\s*[:=]\s*["''][^"'']{8,}'
        '本机用户路径' = '(?i)[A-Z]:\\Users\\[^\\\s]+'
    }
    $contentViolations = [System.Collections.Generic.List[string]]::new()
    foreach ($relativePath in $trackedFiles) {
        $extension = [IO.Path]::GetExtension($relativePath).ToLowerInvariant()
        if ($textExtensions -notcontains $extension) { continue }
        $fullPath = Join-Path $repositoryRoot $relativePath
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { continue }
        $content = [IO.File]::ReadAllText($fullPath)
        foreach ($entry in $sensitivePatterns.GetEnumerator()) {
            if ($content -match $entry.Value) {
                $contentViolations.Add("$relativePath [$($entry.Key)]")
            }
        }
    }
    if ($contentViolations.Count -gt 0) {
        Write-Error "检测到潜在敏感内容（仅显示文件和类别）：$($contentViolations -join ', ')"
    }

    Write-Output "隐私审计通过：已检查 $($trackedFiles.Count) 个 Git 跟踪文件。"
}
finally {
    Set-Location -LiteralPath $originalLocation
}
