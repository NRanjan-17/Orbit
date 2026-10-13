# Generates src/Orbit/Assets/Orbit.ico (multi-size) and Orbit.png (256 px).
# Design: rounded-square tile with a blue-to-violet gradient, bold white headphones, and a small cyan
# "satellite" dot (the Orbit accent; omitted at 16-24 px). Drawn at 4x and downsampled for clean edges.
# Generic shapes, no brand marks. Usage: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param([string]$OutDir = (Join-Path $PSScriptRoot "..\src\Orbit\Assets"))

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $OutDir | Out-Null

function RoundRect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); return $p
}

function New-OrbitBitmap([int]$size) {
    $S = $size * 4
    $big = New-Object System.Drawing.Bitmap $S, $S
    $g = [System.Drawing.Graphics]::FromImage($big)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
    $s = [single]$S
    $small = $size -le 24

    # Tile
    $m = $s * 0.04
    $tile = RoundRect $m $m ($s - 2 * $m) ($s - 2 * $m) ($s * 0.24)
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF($s, $s)),
        [System.Drawing.Color]::FromArgb(255, 59, 91, 255),      # blue
        [System.Drawing.Color]::FromArgb(255, 139, 63, 252))     # violet
    $g.FillPath($bg, $tile)

    # Headphones: semicircle band + pill-shaped ear cups (heavier stroke at tray sizes)
    $white = [System.Drawing.Color]::White
    $pen = New-Object System.Drawing.Pen($white, [single]($s * $(if ($small) { 0.10 } else { 0.085 })))
    $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $bw = $s * 0.50; $bx = ($s - $bw) / 2; $by = $s * 0.24
    $g.DrawArc($pen, $bx, $by, $bw, $bw, 180, 180)
    $cw = $s * 0.15; $ch = $s * 0.27; $cy = $by + $bw / 2 - $s * 0.02
    $cup = New-Object System.Drawing.SolidBrush($white)
    foreach ($cx in @(($bx - $cw / 2), ($bx + $bw - $cw / 2))) { $g.FillPath($cup, (RoundRect $cx $cy $cw $ch ($cw / 2))) }

    # Satellite dot
    if (-not $small) {
        $r = $s * 0.055
        $g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 125, 211, 252))), $s * 0.70 - $r, $s * 0.24 - $r, 2 * $r, 2 * $r)
    }
    $g.Dispose()

    $out = New-Object System.Drawing.Bitmap $size, $size
    $g2 = [System.Drawing.Graphics]::FromImage($out)
    $g2.InterpolationMode = 'HighQualityBicubic'; $g2.PixelOffsetMode = 'HighQuality'; $g2.SmoothingMode = 'AntiAlias'
    $g2.DrawImage($big, 0, 0, $size, $size); $g2.Dispose(); $big.Dispose()
    return $out
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

# ICO container with PNG-compressed entries (read by the shell, taskbar and WinUI).
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
Write-Host "Wrote $(Join-Path $OutDir 'Orbit.ico')"
