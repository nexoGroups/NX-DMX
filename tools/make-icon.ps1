# Draws the NX-DMX app icon at several sizes and packs it into an .ico (PNG-compressed entries).
# Usage: powershell -File tools/make-icon.ps1 -OutDir src/NxDmx.App/Assets
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\src\NxDmx.App\Assets')
)

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = [single]$size
    $pad = $s * 0.04
    $radius = $s * 0.22

    # Rounded square background with a vertical gradient.
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $x0 = $pad; $y0 = $pad; $w = $s - 2 * $pad
    $path.AddArc($x0, $y0, $d, $d, 180, 90)
    $path.AddArc($x0 + $w - $d, $y0, $d, $d, 270, 90)
    $path.AddArc($x0 + $w - $d, $y0 + $w - $d, $d, $d, 0, 90)
    $path.AddArc($x0, $y0 + $w - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $rect = New-Object System.Drawing.RectangleF $x0, $y0, $w, $w
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect,
        ([System.Drawing.Color]::FromArgb(255, 30, 44, 78)),
        ([System.Drawing.Color]::FromArgb(255, 14, 17, 24)), 90
    $g.FillPath($bg, $path)

    # Three light beams fanning out from the top edge.
    $g.SetClip($path)
    $accent = [System.Drawing.Color]::FromArgb(255, 76, 141, 255)
    $apex = New-Object System.Drawing.PointF ($s * 0.5), ($s * 0.02)
    foreach ($beam in @(@(-0.24, 0.13, 60), @(0.0, 0.15, 100), @(0.24, 0.13, 60))) {
        $cx = $s * (0.5 + $beam[0]); $half = $s * $beam[1]
        $tri = [System.Drawing.PointF[]]@(
            $apex,
            (New-Object System.Drawing.PointF ($cx - $half), ($s * 0.62)),
            (New-Object System.Drawing.PointF ($cx + $half), ($s * 0.62)))
        # A purely vertical gradient: every point of the beam lies inside it, so nothing wraps around (Tile is the default).
        $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $apex,
            (New-Object System.Drawing.PointF ($s * 0.5), ($s * 0.62)),
            ([System.Drawing.Color]::FromArgb($beam[2] + 60, $accent)),
            ([System.Drawing.Color]::FromArgb(0, $accent))
        $g.FillPolygon($brush, $tri)
        $brush.Dispose()
    }
    $g.ResetClip()

    # Wordmark.
    $font = New-Object System.Drawing.Font 'Segoe UI Black', ($s * 0.30), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = 'Center'; $format.LineAlignment = 'Center'
    $textRect = New-Object System.Drawing.RectangleF 0, ($s * 0.50), $s, ($s * 0.40)
    $g.DrawString('NX', $font, [System.Drawing.Brushes]::White, $textRect, $format)

    $g.Dispose()
    return $bmp
}

New-Item -ItemType Directory -Force $OutDir | Out-Null

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($size in $sizes) {
    $bmp = New-IconBitmap $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($size -eq 256) { $bmp.Save((Join-Path $OutDir 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    [pscustomobject]@{ Size = $size; Bytes = $ms.ToArray() }
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$images.Count)
$offset = 6 + 16 * $images.Count
foreach ($image in $images) {
    $dim = if ($image.Size -ge 256) { 0 } else { $image.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$image.Bytes.Length); $w.Write([uint32]$offset)
    $offset += $image.Bytes.Length
}
foreach ($image in $images) { $w.Write($image.Bytes) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'app.ico'), $out.ToArray())
"Wrote $(Join-Path $OutDir 'app.ico') and icon.png"
