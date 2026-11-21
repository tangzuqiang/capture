param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$DistDir,
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [string]$Notes = ""
)

# 生成 dist\latest.json：应用启动/点「检查更新」时拉这个文件判断有没有新版本。
# 安装包文件名是中文的，url 里按 percent-encoding 输出，应用直接拿去下载即可。
# 这里只用 .NET API（不用 Get-FileHash / ConvertTo-Json），避免 powershell 模块环境不可靠时报找不到命令。

$ErrorActionPreference = 'Stop'

$dir = (Resolve-Path -LiteralPath $DistDir).Path
$exe = $null
foreach ($file in [IO.Directory]::GetFiles($dir, '*.exe')) {
    $item = New-Object IO.FileInfo($file)
    if ($exe -eq $null -or $item.LastWriteTimeUtc -gt $exe.LastWriteTimeUtc) { $exe = $item }
}
if ($exe -eq $null) { throw "目录里没有安装包：$dir" }

$url = $BaseUrl.TrimEnd('/') + '/' + [uri]::EscapeDataString($exe.Name)

$sha = [Security.Cryptography.SHA256]::Create()
$stream = [IO.File]::OpenRead($exe.FullName)
try { $hash = $sha.ComputeHash($stream) } finally { $stream.Dispose(); $sha.Dispose() }
$hex = -join ($hash | ForEach-Object { $_.ToString('x2') })

function Esc([string]$text) {
    if ($null -eq $text) { return '' }
    return $text.Replace('\', '\\').Replace('"', '\"').Replace("`r", ' ').Replace("`n", ' ')
}

$json = '{"version":"' + (Esc $Version) + '","url":"' + (Esc $url) + '","sha256":"' + $hex +
    '","size":' + $exe.Length + ',"notes":"' + (Esc $Notes) + '"}'

$out = [IO.Path]::Combine($dir, 'latest.json')
[IO.File]::WriteAllText($out, $json, (New-Object Text.UTF8Encoding($false)))
$out
