# Generates src/Orbit/Assets/Orbit.ico (multi-size) and Orbit.png (256 px).
# Design: indigo->violet circular badge, white headphones, a light orbit ring with a small satellite.
# The ring is omitted at 16-24 px so the tray-size icon stays crisp. Generic shapes, no brand marks.
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
    $small = $size -le 24

    # Badge
    $inset = [Math]::Max(0.5, $s * 0.03)
    $badge = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF($s, $s)),
        [System.Drawing.Color]::FromArgb(255, 79, 70, 229),     # indigo
        [System.Drawing.Color]::FromArgb(255, 147, 51, 234))    # violet
    $g.FillEllipse($badge, $inset, $inset, $s - 2 * $inset, $s - 2 * $inset)

    $white = [System.Drawing.Color]::White

    # Orbit ring (larger sizes only): thin, translucent, tilted, around the headphones
    if (-not $small) {
        $ring = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(150, 255, 255, 255), [single]($s * 0.035))
        $state = $g.Save()
        $g.TranslateTransform($s / 2, $s * 0.54); $g.RotateTransform(-20)
        $rw = $s * 0.40; $rh = $s * 0.13
        $g.DrawEllipse($ring, -$rw, -$rh, 2 * $rw, 2 * $rh)
        $sr = $s * 0.045
        # Satellite on the lower front of the ring, clear of the ear cups.
        $g.FillEllipse((New-Object System.Drawing.SolidBrush($white)), -$rw * 0.30 - $sr, $rh * 0.95 - $sr, 2 * $sr, 2 * $sr)
        $g.Restore($state)
    }

    # Headphones: headband arc + rounded ear cups, white
    $scale = if ($small) { 1.12 } else { 1.0 }
    $bw = $s * 0.46 * $scale; $bx = ($s - $bw) / 2; $by = $s * (0.25 - ($scale - 1) * 0.12)
    $bandPen = New-Object System.Drawing.Pen($white, [single]([Math]::Max(1.4, $s * 0.075)))
    $bandPen.StartCap = $bandPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($bandPen, $bx, $by, $bw, $s * 0.48 * $scale, 180, 180)

    $cw = $s * 0.14 * $scale; $ch = $s * 0.24 * $scale; $cy = $by + $s * 0.20 * $scale; $cr = $cw * 0.42
    $cupBrush = New-Object System.Drawing.SolidBrush($white)
    foreach ($cx in @(($bx - $cw * 0.35), ($bx + $bw - $cw * 0.65))) {
        $g.FillPath($cupBrush, (New-RoundRect $cx $cy $cw $ch $cr))
    }

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

# ICO container with PNG-compressed entries (the shell, taskbar and WinUI all read these).
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

# Preview sheet (not shipped): sizes on light and dark backgrounds.
$sheet = New-Object System.Drawing.Bitmap 560, 300
$sg = [System.Drawing.Graphics]::FromImage($sheet)
$sg.FillRectangle([System.Drawing.Brushes]::White, 0, 0, 560, 150)
$sg.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(32, 32, 32))), 0, 150, 560, 150)
$x = 8
foreach ($sz in 16, 20, 24, 32, 48, 128) {
    $b = New-OrbitBitmap $sz
    $sg.DrawImage($b, $x, 10); $sg.DrawImage($b, $x, 160)
    $x += $sz + 16
}
$sheet.Save((Join-Path $env:TEMP "orbit-icon-preview.png"), [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Wrote $(Join-Path $OutDir 'Orbit.ico')"
