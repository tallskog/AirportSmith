# Regenerates AirportSmith/AirportSmith.ico from the icon tile artwork.
# 1. Render art/AirportSmith-icon-tile.svg to a transparent 1024x1024
#    tile-1024.png in $Dir, e.g. with headless Edge:
#      msedge --headless=new --default-background-color=00000000
#        --window-size=1024,1024 --screenshot=<Dir>\tile-1024.png <page showing the SVG at 1024px>
# 2. powershell -File art\make-ico.ps1 -Dir <Dir>
# 3. Copy <Dir>\AirportSmith.ico to AirportSmith\AirportSmith.ico.
# AirportSmith/AirportSmith.png is art/AirportSmith-icon.svg (glow on black) rendered at 1024px.
param([string]$Dir)
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Bitmap]::FromFile((Join-Path $Dir 'tile-1024.png'))
"source: $($src.Width)x$($src.Height), corner alpha=$($src.GetPixel(2,2).A), center alpha=$($src.GetPixel(512,512).A)"

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = @()
foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $s, $s)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($s -eq 256) { $bmp.Save((Join-Path $Dir 'tile-256.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    if ($s -eq 32) { $bmp.Save((Join-Path $Dir 'tile-32.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    $frames += ,@($s, $ms.ToArray())
}
$src.Dispose()

# ICO container with PNG-compressed frames (supported since Windows Vista).
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = $f[0]; $data = $f[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$data.Length); $w.Write([UInt32]$offset)
    $offset += $data.Length
}
foreach ($f in $frames) { $w.Write([byte[]]$f[1]) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $Dir 'AirportSmith.ico'), $out.ToArray())
"ico: $($out.Length) bytes, $($frames.Count) frames"

# Sanity check: Windows can read the ICO back.
$ico = New-Object System.Drawing.Icon (Join-Path $Dir 'AirportSmith.ico'), 48, 48
"ico readback 48: $($ico.Width)x$($ico.Height)"
$ico.Dispose()
