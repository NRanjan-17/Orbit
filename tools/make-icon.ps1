# Generates src/Orbit/Assets/Orbit.ico (multi-size) and Orbit.png (256 px):
# a pair of headphones circled by a tilted orbit ring with a small satellite. Generic shapes, no brand marks.
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param([string]$OutDir = (Join-Path $PSScriptRoot "..\src\Orbit\Assets"))

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-OrbitBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [single]$size
    $indigo = [System.Drawing.Color]::FromArgb(255, 88, 86, 214)
    $violet = [System.Drawing.Color]::FromArgb(255, 150, 140, 255)
    $ringColor = [System.Drawing.Color]::FromArgb(255, 56, 189, 248)   # sky blue, contrasts with indigo

    # Orbit ring, back half (behind the headphones)
    $ringPen = New-Object System.Drawing.Pen($ringColor, [Math]::Max(1.2, $s * 0.055))
    $ringPen.StartCap = $ringPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $rw = $s * 0.47; $rh = $s * 0.17
    $state = $g.Save()
    $g.TranslateTransform($s / 2, $s * 0.56); $g.RotateTransform(-18)
    $g.DrawArc($ringPen, -$rw, -$rh, 2 * $rw, 2 * $rh, 180, 180)
    $g.Restore($state)

    # Headband
    $bandPen = New-Object System.Drawing.Pen($indigo, [Math]::Max(1.6, $s * 0.085))
    $bandPen.StartCap = $bandPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $bw = $s * 0.58; $bx = ($s - $bw) / 2; $by = $s * 0.12
    $g.DrawArc($bandPen, $bx, $by, $bw, $s * 0.66, 180, 180)

    # Ear cups (rounded rectangles) with gradient
    $cw = $s * 0.19; $ch = $s * 0.32; $cy = $s * 0.42; $cr = $s * 0.06
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, $cy)), (New-Object System.Drawing.PointF(0, ($cy + $ch))), $violet, $indigo)
    foreach ($cx in @(($bx - $cw * 0.45), ($bx + $bw - $cw * 0.55))) {
        $path = New-RoundRect $cx $cy $cw $ch $cr
        $g.FillPath($brush, $path)
    }

    # Orbit ring, front half + satellite
    $state = $g.Save()
    $g.TranslateTransform($s / 2, $s * 0.56); $g.RotateTransform(-18)
    $g.DrawArc($ringPen, -$rw, -$rh, 2 * $rw, 2 * $rh, 0, 180)
    $sr = [Math]::Max(1.4, $s * 0.06)
    $sx = $rw * [Math]::Cos(0.9); $sy = $rh * [Math]::Sin(0.9)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush($ringColor)), $sx - $sr, $sy - $sr, 2 * $sr, 2 * $sr)
    $g.Restore($state)

    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = New-OrbitBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($sz -eq 256) { $bmp.Save((Join-Path $OutDir "Orbit.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
    $pngs += , ($ms.ToArray())
    $bmp.Dispose()
}

# ICO container with PNG-compressed entries.
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $dim = if ($sz -ge 256) { 0 } else { $sz }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir "Orbit.ico"), $out.ToArray())

# Preview sheet (not shipped): every size side by side on light and dark backgrounds.
$sheet = New-Object System.Drawing.Bitmap 640, 300
$sg = [System.Drawing.Graphics]::FromImage($sheet)
$sg.FillRectangle([System.Drawing.Brushes]::White, 0, 0, 640, 150)
$sg.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(32, 32, 32))), 0, 150, 640, 150)
$x = 8
foreach ($sz in 16, 24, 32, 48, 64, 128) {
    $b = New-OrbitBitmap $sz
    $sg.DrawImage($b, $x, 10); $sg.DrawImage($b, $x, 160)
    $x += $sz + 16
}
$sheet.Save((Join-Path $env:TEMP "orbit-icon-preview.png"), [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Wrote $(Join-Path $OutDir 'Orbit.ico')"
