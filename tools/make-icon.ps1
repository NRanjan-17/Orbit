# Generates src/Orbit/Assets/Orbit.ico (and Orbit.png) - a tilted orbit ring with a planet and a satellite.
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
param([string]$OutDir = (Join-Path $PSScriptRoot "..\src\Orbit\Assets"))

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

function New-OrbitBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [single]$size
    $accent = [System.Drawing.Color]::FromArgb(255, 94, 92, 230)   # indigo
    $light  = [System.Drawing.Color]::FromArgb(255, 160, 158, 255)

    # Planet
    $r = $s * 0.22
    $planet = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF(($s/2 - $r), ($s/2 - $r))),
        (New-Object System.Drawing.PointF(($s/2 + $r), ($s/2 + $r))), $light, $accent)
    $g.FillEllipse($planet, $s/2 - $r, $s/2 - $r, 2*$r, 2*$r)

    # Tilted ring
    $penWidth = [Math]::Max(1.5, $s * 0.075)
    $pen = New-Object System.Drawing.Pen($accent, $penWidth)
    $g.TranslateTransform($s/2, $s/2)
    $g.RotateTransform(-28)
    $rw = $s * 0.44; $rh = $s * 0.17
    $g.DrawEllipse($pen, -$rw, -$rh, 2*$rw, 2*$rh)

    # Satellite on the ring
    $sr = [Math]::Max(1.5, $s * 0.075)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush($light)), $rw * 0.62 - $sr, $rh * 0.78 - $sr, 2*$sr, 2*$sr)
    $g.ResetTransform()
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = New-OrbitBitmap $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($sz -eq 256) { $bmp.Save((Join-Path $OutDir "Orbit.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
    $pngs += ,($ms.ToArray())
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
Write-Host "Wrote $(Join-Path $OutDir 'Orbit.ico')"
