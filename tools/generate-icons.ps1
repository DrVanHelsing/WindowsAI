# Generates the WindowsAI brand icon set from code (no design tool needed):
#   - Assets\AppIcon.ico        multi-size ICO (tray + window icon): gradient sparkle on dark glass tile
#   - MSIX logo PNGs            transparent-background gradient sparkle (Windows plates them)
# Run from the repo root:  powershell -File tools\generate-icons.ps1

Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot "..\src\WindowsAI.App\Assets"
$assets = [IO.Path]::GetFullPath($assets)

# Brand gradient (the app's GlowGradientBrush)
$gradientStops = @(
    @{ Pos = 0.00; Color = [Drawing.Color]::FromArgb(255, 0x4F, 0x8C, 0xFF) },
    @{ Pos = 0.30; Color = [Drawing.Color]::FromArgb(255, 0x9B, 0x6B, 0xFF) },
    @{ Pos = 0.55; Color = [Drawing.Color]::FromArgb(255, 0xFF, 0x6B, 0xD6) },
    @{ Pos = 0.80; Color = [Drawing.Color]::FromArgb(255, 0xFF, 0x9D, 0x5C) },
    @{ Pos = 1.00; Color = [Drawing.Color]::FromArgb(255, 0x4D, 0xE3, 0xFF) }
)

function New-StarPath([double]$cx, [double]$cy, [double]$outerR, [double]$innerR) {
    # Four-point concave sparkle: outer points N/E/S/W, inner points on the diagonals.
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $pts = New-Object 'Drawing.PointF[]' 8
    for ($i = 0; $i -lt 8; $i++) {
        $angle = (-90 + 45 * $i) * [Math]::PI / 180
        $rad = if ($i % 2 -eq 0) { $outerR } else { $innerR }
        $pts[$i] = New-Object Drawing.PointF ([float]($cx + $rad * [Math]::Cos($angle))), ([float]($cy + $rad * [Math]::Sin($angle)))
    }
    $path.AddPolygon($pts)
    return $path
}

function New-GradientBrush([Drawing.RectangleF]$bounds) {
    $brush = New-Object Drawing.Drawing2D.LinearGradientBrush(
        (New-Object Drawing.PointF $bounds.Left, $bounds.Top),
        (New-Object Drawing.PointF $bounds.Right, $bounds.Bottom),
        [Drawing.Color]::White, [Drawing.Color]::White)
    $blend = New-Object Drawing.Drawing2D.ColorBlend $gradientStops.Count
    $blend.Positions = [float[]]($gradientStops | ForEach-Object { $_.Pos })
    $blend.Colors = [Drawing.Color[]]($gradientStops | ForEach-Object { $_.Color })
    $brush.InterpolationColors = $blend
    return $brush
}

function Draw-Mark([Drawing.Graphics]$g, [double]$size, [bool]$withTile) {
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([Drawing.Color]::Transparent)

    if ($withTile) {
        # Dark glass tile with rounded corners + faint top sheen
        $radius = $size * 0.22
        $tile = New-Object Drawing.Drawing2D.GraphicsPath
        $d = [float]($radius * 2)
        $s = [float]$size
        $tile.AddArc(0, 0, $d, $d, 180, 90)
        $tile.AddArc($s - $d, 0, $d, $d, 270, 90)
        $tile.AddArc($s - $d, $s - $d, $d, $d, 0, 90)
        $tile.AddArc(0, $s - $d, $d, $d, 90, 90)
        $tile.CloseFigure()

        $bg = New-Object Drawing.Drawing2D.LinearGradientBrush(
            (New-Object Drawing.PointF 0, 0), (New-Object Drawing.PointF 0, $s),
            [Drawing.Color]::FromArgb(255, 0x1C, 0x14, 0x42),
            [Drawing.Color]::FromArgb(255, 0x0C, 0x09, 0x20))
        $g.FillPath($bg, $tile)
        $bg.Dispose()

        $half = [float]($s * 0.5)
        $sheenTop = New-Object Drawing.PointF(0, 0)
        $sheenBottom = New-Object Drawing.PointF(0, $half)
        $sheen = New-Object Drawing.Drawing2D.LinearGradientBrush(
            $sheenTop, $sheenBottom,
            [Drawing.Color]::FromArgb(38, 255, 255, 255),
            [Drawing.Color]::FromArgb(0, 255, 255, 255))
        $oldClip = $g.Clip
        $g.SetClip($tile)
        $g.FillRectangle($sheen, 0, 0, $s, $half)
        $g.Clip = $oldClip
        $sheen.Dispose()
        $tile.Dispose()
    }

    # Main sparkle (slightly lower-left), mini sparkle upper-right on larger sizes
    # NB: PowerShell variables are case-insensitive — do NOT name these $R/$r
    # (they would collide into one variable and yield a regular octagon).
    $cx = $size * 0.46
    $cy = $size * 0.55
    $outer = $size * 0.33
    $inner = $outer * 0.30

    $star = New-StarPath $cx $cy $outer $inner
    $brush = New-GradientBrush ($star.GetBounds())
    $g.FillPath($brush, $star)
    $brush.Dispose(); $star.Dispose()

    if ($size -ge 48) {
        $mini = New-StarPath ($size * 0.74) ($size * 0.26) ($size * 0.115) ($size * 0.038)
        $brush2 = New-GradientBrush ($mini.GetBounds())
        $g.FillPath($brush2, $mini)
        $brush2.Dispose(); $mini.Dispose()
    }
}

function New-MarkPng([int]$width, [int]$height, [int]$markSize, [bool]$withTile) {
    $bmp = New-Object Drawing.Bitmap $width, $height
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([Drawing.Color]::Transparent)

    # Render the mark on its own bitmap, then center it on the canvas
    $markBmp = New-Object Drawing.Bitmap $markSize, $markSize
    $mg = [Drawing.Graphics]::FromImage($markBmp)
    Draw-Mark $mg $markSize $withTile
    $mg.Dispose()

    $g.DrawImage($markBmp, [int](($width - $markSize) / 2), [int](($height - $markSize) / 2), $markSize, $markSize)
    $g.Dispose(); $markBmp.Dispose()
    return $bmp
}

function Save-Png([Drawing.Bitmap]$bmp, [string]$name) {
    $path = Join-Path $assets $name
    $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  wrote $name"
}

function Get-PngBytes([int]$size, [bool]$withTile) {
    $bmp = New-Object Drawing.Bitmap $size, $size
    $g = [Drawing.Graphics]::FromImage($bmp)
    Draw-Mark $g $size $withTile
    $g.Dispose()
    $ms = New-Object IO.MemoryStream
    $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $ms.ToArray()
}

# ── AppIcon.ico: PNG-compressed multi-size ICO ───────────────────────────────
$sizes = 16, 24, 32, 48, 64, 128, 256
$images = $sizes | ForEach-Object { ,(Get-PngBytes $_ $true) }

$icoPath = Join-Path $assets "AppIcon.ico"
$stream = [IO.File]::Create($icoPath)
$writer = New-Object IO.BinaryWriter $stream
$writer.Write([uint16]0)            # reserved
$writer.Write([uint16]1)            # type: icon
$writer.Write([uint16]$sizes.Count) # image count

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $writer.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # width (0 = 256)
    $writer.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))  # height
    $writer.Write([byte]0)   # palette
    $writer.Write([byte]0)   # reserved
    $writer.Write([uint16]1) # planes
    $writer.Write([uint16]32)# bpp
    $writer.Write([uint32]$images[$i].Length)
    $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $writer.Write($img) }
$writer.Dispose(); $stream.Dispose()
Write-Host "  wrote AppIcon.ico ($($sizes -join ', '))"

# ── MSIX logo PNGs: transparent background (Windows applies the plate) ───────
Save-Png (New-MarkPng 88 88 80 $false)        "Square44x44Logo.scale-200.png"
Save-Png (New-MarkPng 24 24 24 $false)        "Square44x44Logo.targetsize-24_altform-unplated.png"
Save-Png (New-MarkPng 48 48 44 $false)        "Square44x44Logo.targetsize-48_altform-lightunplated.png"
Save-Png (New-MarkPng 300 300 200 $false)     "Square150x150Logo.scale-200.png"
Save-Png (New-MarkPng 100 100 88 $false)      "StoreLogo.png"
Save-Png (New-MarkPng 620 300 180 $false)     "Wide310x150Logo.scale-200.png"
Save-Png (New-MarkPng 1240 600 260 $false)    "SplashScreen.scale-200.png"
Save-Png (New-MarkPng 48 48 44 $false)        "LockScreenLogo.scale-200.png"

# Side-by-side preview (tile + plain mark) for quick visual review — not shipped.
$preview = New-Object Drawing.Bitmap 532, 276
$pg = [Drawing.Graphics]::FromImage($preview)
$pg.Clear([Drawing.Color]::FromArgb(255, 32, 32, 40))
$tileBmp = New-Object Drawing.Bitmap 256, 256
$tg = [Drawing.Graphics]::FromImage($tileBmp); Draw-Mark $tg 256 $true; $tg.Dispose()
$markBmp = New-Object Drawing.Bitmap 256, 256
$mg2 = [Drawing.Graphics]::FromImage($markBmp); Draw-Mark $mg2 256 $false; $mg2.Dispose()
$pg.DrawImage($tileBmp, 10, 10); $pg.DrawImage($markBmp, 276, 10)
$pg.Dispose(); $tileBmp.Dispose(); $markBmp.Dispose()
$previewPath = Join-Path $env:TEMP "windowsai-icon-preview.png"
$preview.Save($previewPath, [Drawing.Imaging.ImageFormat]::Png); $preview.Dispose()
Write-Host "  preview: $previewPath"

Write-Host "Done. Assets written to $assets"
