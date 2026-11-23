# Downloads the models used by the voice command assistant (not bundled with the installer).
#   Vosk  small Chinese model  : command recognition (~42 MB)
#   VITS  Chinese TTS model    : local speech synthesis (~100-200 MB)
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts\fetch-voice-models.ps1
#   powershell -ExecutionPolicy Bypass -File scripts\fetch-voice-models.ps1 -SkipVits
#   powershell -ExecutionPolicy Bypass -File scripts\fetch-voice-models.ps1 -ModelsDir "$env:LOCALAPPDATA\Shiying\models"
param(
    [string]$VoskUrl = "https://alphacephei.com/vosk/models/vosk-model-small-cn-0.22.zip",
    [string]$VitsUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/vits-zh-hf-fanchen-C.tar.bz2",
    [string]$ModelsDir = "",
    [switch]$SkipVosk,
    [switch]$SkipVits
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$models = if ([string]::IsNullOrEmpty($ModelsDir)) { Join-Path $root "models" } else { $ModelsDir }
$cache = Join-Path $models ".cache"

function Download($url, $target) {
    if ((Test-Path $target) -and ((Get-Item $target).Length -gt 0)) {
        Write-Host "already downloaded: $target"
        return
    }
    New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
    Write-Host "download $url"
    & curl.exe -L --fail --retry 3 -o "$target.part" $url
    if ($LASTEXITCODE -ne 0) {
        throw "download failed: $url"
    }
    Move-Item "$target.part" $target -Force
}

# ---------- Vosk ----------
if (-not $SkipVosk) {
    $zip = Join-Path $cache "vosk-model-small-cn-0.22.zip"
    Download $VoskUrl $zip

    $voskRoot = (Join-Path $models "vosk")
    New-Item -ItemType Directory -Force $voskRoot | Out-Null
    Write-Host "extract vosk model"
    Expand-Archive -LiteralPath $zip -DestinationPath $voskRoot -Force

    $found = Get-ChildItem $voskRoot -Directory | Where-Object {
        (Test-Path (Join-Path $_.FullName "am")) -and (Test-Path (Join-Path $_.FullName "conf"))
    } | Select-Object -First 1
    if (-not $found) {
        throw "no vosk model (am/conf) under $voskRoot"
    }
    Write-Host "vosk ready: $($found.FullName)"
}

# ---------- VITS ----------
if (-not $SkipVits) {
    $tar = Join-Path $cache "vits-zh.tar.bz2"
    Download $VitsUrl $tar

    $vitsRoot = (Join-Path $models "vits")
    New-Item -ItemType Directory -Force $vitsRoot | Out-Null
    Write-Host "extract vits model"
    & tar.exe -xjf $tar -C $vitsRoot
    if ($LASTEXITCODE -ne 0) {
        throw "extract failed: $tar"
    }

    $model = Get-ChildItem $vitsRoot -Recurse -Filter "*.onnx" |
        Where-Object { Test-Path (Join-Path $_.DirectoryName "tokens.txt") } |
        Select-Object -First 1
    if (-not $model) {
        throw "no vits model (*.onnx with tokens.txt) under $vitsRoot"
    }

    Write-Host "vits ready: $($model.FullName)"
    $dir = $model.DirectoryName
    foreach ($name in @("tokens.txt", "lexicon.txt", "dict")) {
        $hit = Test-Path (Join-Path $dir $name)
        Write-Host ("  {0,-12} {1}" -f $name, $(if ($hit) { "yes" } else { "no" }))
    }
}

Write-Host ""
Write-Host "models dir : $models"
Write-Host "optional   : models\voice\voice.json (auto detection is used when absent)"
