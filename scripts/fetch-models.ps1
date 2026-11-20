# Downloads the PP-OCRv5 mobile ONNX models (RapidOCR export) into models\ocr.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dir = Join-Path $root "models\ocr"
New-Item -ItemType Directory -Force $dir | Out-Null

$base = "https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/master/"
$files = [ordered]@{
    "det.onnx" = @("onnx/PP-OCRv5/det/ch_PP-OCRv5_det_mobile.onnx", 4819576)
    "rec.onnx" = @("onnx/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile.onnx", 16631306)
    "keys.txt" = @("paddle/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile/ppocrv5_dict.txt", 74012)
}

foreach ($name in $files.Keys) {
    $target = Join-Path $dir $name
    $source = $files[$name][0]
    $size = $files[$name][1]
    if ((Test-Path $target) -and ((Get-Item $target).Length -eq $size)) {
        continue
    }

    Write-Host "download $name"
    $temp = "$target.part"
    Invoke-WebRequest -Uri ($base + $source) -OutFile $temp -UseBasicParsing -TimeoutSec 600
    if ((Get-Item $temp).Length -ne $size) {
        Remove-Item $temp -Force
        throw "$name size mismatch"
    }

    Move-Item $temp $target -Force
}
