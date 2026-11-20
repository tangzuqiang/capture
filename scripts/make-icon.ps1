# Draws the app icon and writes assets\capture.ico (16-256 px) plus assets\capture.png.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root "assets"
New-Item -ItemType Directory -Force $assets | Out-Null

function New-RoundPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $small = $s -le 24
    $inset = if ($small) { 0.5 } else { $s * 0.04 }
    $size = $s - $inset * 2
    $tile = New-RoundPath $inset $inset $size $size ($s * 0.23)

    $rect = New-Object System.Drawing.RectangleF 0, 0, $s, $s
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::Black), ([System.Drawing.Color]::Black), 45.0
    $blend = New-Object System.Drawing.Drawing2D.ColorBlend 3
    $blend.Colors = @(
        [System.Drawing.Color]::FromArgb(255, 0, 198, 255),
        [System.Drawing.Color]::FromArgb(255, 99, 102, 241),
        [System.Drawing.Color]::FromArgb(255, 217, 70, 239)
    )
    $blend.Positions = @(0.0, 0.55, 1.0)
    $bg.InterpolationColors = $blend
    $g.FillPath($bg, $tile)

    if (-not $small) {
        $g.SetClip($tile)
        $gloss = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF 0, 0, $s, ($s * 0.55)), ([System.Drawing.Color]::FromArgb(70, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)), 90.0
        $g.FillEllipse($gloss, -$s * 0.25, -$s * 0.62, $s * 1.5, $s * 1.05)
        $shade = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF 0, ($s * 0.5), $s, ($s * 0.5)), ([System.Drawing.Color]::FromArgb(0, 30, 0, 70)), ([System.Drawing.Color]::FromArgb(45, 30, 0, 70)), 90.0
        $g.FillRectangle($shade, 0, $s * 0.5, $s, $s * 0.5)
        $g.ResetClip()
        $edge = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(60, 255, 255, 255)), ([Math]::Max(1.0, $s / 128.0))
        $g.DrawPath($edge, $tile)
    }

    $white = [System.Drawing.Color]::White
    $stroke = if ($small) { [Math]::Max(1.6, $s * 0.11) } else { $s * 0.072 }
    $pen = New-Object System.Drawing.Pen $white, $stroke
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $a = if ($small) { $s * 0.22 } else { $s * 0.235 }
    $b = $s - $a
    $len = if ($small) { $s * 0.2 } else { $s * 0.17 }
    foreach ($corner in @(@($a, $a, 1, 1), @($b, $a, -1, 1), @($b, $b, -1, -1), @($a, $b, 1, -1))) {
        $cx = $corner[0]; $cy = $corner[1]; $dx = $corner[2]; $dy = $corner[3]
        $pts = [System.Drawing.PointF[]]@(
            (New-Object System.Drawing.PointF ($cx), ($cy + $dy * $len)),
            (New-Object System.Drawing.PointF ($cx), ($cy)),
            (New-Object System.Drawing.PointF ($cx + $dx * $len), ($cy))
        )
        $g.DrawLines($pen, $pts)
    }

    $c = $s / 2.0
    if ($small) {
        $dot = New-Object System.Drawing.SolidBrush $white
        $r = $s * 0.13
        $g.FillEllipse($dot, $c - $r, $c - $r, $r * 2, $r * 2)
    }
    else {
        $ringR = $s * 0.155
        $ring = New-Object System.Drawing.Pen $white, ($s * 0.05)
        $g.DrawEllipse($ring, $c - $ringR, $c - $ringR, $ringR * 2, $ringR * 2)
        $coreR = $s * 0.075
        $core = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.RectangleF ($c - $coreR), ($c - $coreR), ($coreR * 2), ($coreR * 2)), ([System.Drawing.Color]::FromArgb(255, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(255, 186, 230, 255)), 45.0
        $g.FillEllipse($core, $c - $coreR, $c - $coreR, $coreR * 2, $coreR * 2)

        if ($s -ge 48) {
            $sx = $s * 0.675; $sy = $s * 0.325; $sr = $s * 0.06; $sw = $s * 0.014
            $spark = New-Object System.Drawing.Drawing2D.GraphicsPath
            $spark.AddPolygon([System.Drawing.PointF[]]@(
                (New-Object System.Drawing.PointF $sx, ($sy - $sr)),
                (New-Object System.Drawing.PointF ($sx + $sw), ($sy - $sw)),
                (New-Object System.Drawing.PointF ($sx + $sr), $sy),
                (New-Object System.Drawing.PointF ($sx + $sw), ($sy + $sw)),
                (New-Object System.Drawing.PointF $sx, ($sy + $sr)),
                (New-Object System.Drawing.PointF ($sx - $sw), ($sy + $sw)),
                (New-Object System.Drawing.PointF ($sx - $sr), $sy),
                (New-Object System.Drawing.PointF ($sx - $sw), ($sy - $sw))
            ))
            $g.FillPath((New-Object System.Drawing.SolidBrush $white), $spark)
        }
    }

    $g.Dispose()
    return $bmp
}

function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $s = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    $w.Write([int]40); $w.Write([int]$s); $w.Write([int]($s * 2)); $w.Write([int16]1); $w.Write([int16]32)
    $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0); $w.Write([int]0)
    $data = $bmp.LockBits((New-Object System.Drawing.Rectangle 0, 0, $s, $s), [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $row = New-Object byte[] ($s * 4)
    for ($y = $s - 1; $y -ge 0; $y--) {
        [System.Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($data.Scan0, $y * $data.Stride), $row, 0, $s * 4)
        $w.Write($row)
    }
    $bmp.UnlockBits($data)
    $maskRow = [int]([Math]::Ceiling($s / 32.0) * 4)
    $w.Write((New-Object byte[] ($maskRow * $s)))
    $w.Flush()
    return , $ms.ToArray()
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = @()
foreach ($size in $sizes) {
    $bmp = Draw-Icon $size
    if ($size -eq 256) {
        $png = New-Object System.IO.MemoryStream
        $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png)
        $images += , $png.ToArray()
        $bmp.Save((Join-Path $assets "capture.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    else {
        [byte[]]$dib = Get-DibBytes $bmp
        $images += , $dib
    }
    $bmp.Dispose()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([int16]0); $w.Write([int16]1); $w.Write([int16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([int16]1); $w.Write([int16]32); $w.Write([int]$images[$i].Length); $w.Write([int]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write([byte[]]$img) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets "capture.ico"), $out.ToArray())
Write-Host "capture.ico $($out.Length) bytes"
