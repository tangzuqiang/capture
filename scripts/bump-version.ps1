param(
    [switch]$Print
)

# 版本号唯一来源：Shiying.csproj 里的 <Version>x.y.z</Version>
# -Print  只读出当前版本，不改文件（build.bat 用它给窗口标题写 version.txt）
# 默认    把 patch 位 +1 并写回，同时留一份 .releasebak 供构建失败时回滚

$ErrorActionPreference = 'Stop'

$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'Shiying.csproj'
if (-not (Test-Path $proj)) { throw "找不到 $proj" }

$text = [IO.File]::ReadAllText($proj)
if ($text -notmatch '<Version>(\d+)\.(\d+)\.(\d+)</Version>') {
    throw 'Shiying.csproj 里找不到 <Version>主.次.修订</Version>'
}

$major = $Matches[1]
$minor = $Matches[2]
$patch = [int]$Matches[3]

if ($Print) {
    "$major.$minor.$patch"
    exit 0
}

$new = "$major.$minor.$($patch + 1)"

# 备份放临时目录，避免版本备份文件被 git add -A 带进仓库
$backup = Join-Path $env:TEMP 'Shiying.csproj.releasebak'
Copy-Item -LiteralPath $proj -Destination $backup -Force

$text = [regex]::Replace($text, '<Version>[\d.]+</Version>', "<Version>$new</Version>")
[IO.File]::WriteAllText($proj, $text, (New-Object System.Text.UTF8Encoding($false)))

$new
