<#
.SYNOPSIS
    Generates the Code Radar marketplace assets (icon + preview + hero image)
    procedurally using System.Drawing so no external tools are required.

.DESCRIPTION
    Produces three PNGs suitable for the VS Marketplace:
      - assets\icon.png     (128x128)  Marketplace icon referenced by source.extension.vsixmanifest
      - assets\preview.png  (300x300)  Marketplace preview thumbnail (also in vsixmanifest)
      - assets\hero.png     (1280x640) Marketplace listing hero image

    Run this once before building, or let the MSBuild target in CodeRadar.csproj
    invoke it automatically when the files are missing.

.PARAMETER OutDir
    Directory to write the PNGs into. Defaults to .\src\CodeRadar\assets relative
    to the repo root.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\Generate-Assets.ps1

.NOTES
    Uses System.Drawing which ships with .NET Framework 4.8. Windows PowerShell 5.1
    (the default on Windows 10/11) runs this script with no extra setup.
#>

param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\src\CodeRadar\assets")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

# ------- Palette -------
$bgColor      = [System.Drawing.Color]::FromArgb(255,  30,  30,  30)  # #1E1E1E
$panelColor   = [System.Drawing.Color]::FromArgb(255,  37,  37,  38)  # #252526
$accentColor  = [System.Drawing.Color]::FromArgb(255, 255, 200,  61)  # #FFC83D
$primaryText  = [System.Drawing.Color]::FromArgb(255, 241, 241, 241)  # #F1F1F1
$mutedText    = [System.Drawing.Color]::FromArgb(255, 181, 181, 181)  # #B5B5B5

function New-RadarCanvas {
    param([int]$Width, [int]$Height)
    $bmp = New-Object System.Drawing.Bitmap $Width, $Height
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    return @{ Bitmap = $bmp; Graphics = $g }
}

function Draw-Radar {
    <#
        Draws a radar motif (concentric rings + crosshairs + sweep + center dot + blip)
        centered on (Cx, Cy) with the rings filling up to MaxR.
    #>
    param(
        [System.Drawing.Graphics]$G,
        [float]$Cx, [float]$Cy, [float]$MaxR,
        [float]$RingWidth = 2.0,
        [int]$Rings = 4,
        [bool]$DrawBlip = $true,
        [bool]$DrawSweep = $true
    )

    # Rings
    for ($i = 1; $i -le $Rings; $i++) {
        $r     = $MaxR * ($i / [float]$Rings)
        $alpha = [int](90 + (130 * ($i / [float]$Rings)))
        $color = [System.Drawing.Color]::FromArgb($alpha, $accentColor.R, $accentColor.G, $accentColor.B)
        $pen   = New-Object System.Drawing.Pen $color, $RingWidth
        $G.DrawEllipse($pen, $Cx - $r, $Cy - $r, $r * 2, $r * 2)
        $pen.Dispose()
    }

    # Crosshairs
    $crossColor = [System.Drawing.Color]::FromArgb(60, $accentColor.R, $accentColor.G, $accentColor.B)
    $crossPen   = New-Object System.Drawing.Pen $crossColor, 1
    $G.DrawLine($crossPen, ($Cx - $MaxR), $Cy, ($Cx + $MaxR), $Cy)
    $G.DrawLine($crossPen, $Cx, ($Cy - $MaxR), $Cx, ($Cy + $MaxR))
    $crossPen.Dispose()

    # Radar sweep (pie slice, soft alpha)
    if ($DrawSweep) {
        $sweepColor = [System.Drawing.Color]::FromArgb(70, $accentColor.R, $accentColor.G, $accentColor.B)
        $sweepBr    = New-Object System.Drawing.SolidBrush $sweepColor
        $G.FillPie($sweepBr, ($Cx - $MaxR), ($Cy - $MaxR), ($MaxR * 2), ($MaxR * 2), -105.0, 35.0)
        $sweepBr.Dispose()
    }

    # Center dot
    $centerBr = New-Object System.Drawing.SolidBrush $accentColor
    $dr       = [Math]::Max(3.0, $MaxR * 0.05)
    $G.FillEllipse($centerBr, ($Cx - $dr), ($Cy - $dr), ($dr * 2), ($dr * 2))

    # Blip
    if ($DrawBlip) {
        $angleRad = 35.0 * [Math]::PI / 180.0
        $blipR    = $MaxR * 0.62
        $bx       = $Cx + [Math]::Cos($angleRad) * $blipR
        $by       = $Cy - [Math]::Sin($angleRad) * $blipR
        $br       = [Math]::Max(4.0, $MaxR * 0.07)

        # Outer soft halo
        $haloColor = [System.Drawing.Color]::FromArgb(90, $accentColor.R, $accentColor.G, $accentColor.B)
        $haloBr    = New-Object System.Drawing.SolidBrush $haloColor
        $G.FillEllipse($haloBr, ($bx - $br * 2.2), ($by - $br * 2.2), ($br * 4.4), ($br * 4.4))
        $haloBr.Dispose()

        # Solid blip
        $G.FillEllipse($centerBr, ($bx - $br), ($by - $br), ($br * 2), ($br * 2))
    }

    $centerBr.Dispose()
}

function New-RadarIcon {
    param([int]$Size, [string]$Path)
    $c = New-RadarCanvas -Width $Size -Height $Size
    $g = $c.Graphics

    # Outer background
    $bgBrush = New-Object System.Drawing.SolidBrush $bgColor
    $g.FillRectangle($bgBrush, 0, 0, $Size, $Size)
    $bgBrush.Dispose()

    # Inset rounded-ish panel (a plain rect; rounded corners look noisy at small sizes).
    $panelBrush = New-Object System.Drawing.SolidBrush $panelColor
    $margin     = [int]($Size * 0.07)
    $g.FillRectangle($panelBrush, $margin, $margin, $Size - 2 * $margin, $Size - 2 * $margin)
    $panelBrush.Dispose()

    # Subtle accent edge
    $edgePen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, $accentColor.R, $accentColor.G, $accentColor.B)), 1
    $g.DrawRectangle($edgePen, $margin, $margin, $Size - 2 * $margin - 1, $Size - 2 * $margin - 1)
    $edgePen.Dispose()

    $cx   = $Size / 2.0
    $cy   = $Size / 2.0
    $maxR = ($Size / 2.0) - $margin - ($Size * 0.09)

    Draw-Radar -G $g -Cx $cx -Cy $cy -MaxR $maxR -RingWidth ([Math]::Max(1.0, $Size * 0.012)) -Rings 4

    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    $c.Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $c.Bitmap.Dispose()
    Write-Host "wrote $Path ($Size x $Size)"
}

function New-HeroImage {
    param([string]$Path, [int]$Width = 1280, [int]$Height = 640)
    $c = New-RadarCanvas -Width $Width -Height $Height
    $g = $c.Graphics

    # Background gradient (charcoal → slightly darker)
    $gradientRect = New-Object System.Drawing.RectangleF 0, 0, $Width, $Height
    $topColor     = [System.Drawing.Color]::FromArgb(255, 40, 40, 45)
    $bottomColor  = [System.Drawing.Color]::FromArgb(255, 22, 22, 22)
    $bgGrad       = New-Object System.Drawing.Drawing2D.LinearGradientBrush `
                        $gradientRect, $topColor, $bottomColor, ([System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillRectangle($bgGrad, 0, 0, $Width, $Height)
    $bgGrad.Dispose()

    # Left-aligned big radar
    $cx   = $Height / 2.0 + 80
    $cy   = $Height / 2.0
    $maxR = $Height * 0.40
    Draw-Radar -G $g -Cx $cx -Cy $cy -MaxR $maxR -RingWidth 2.0 -Rings 5

    # Right column: title + tagline + features
    $titleFont   = New-Object System.Drawing.Font "Segoe UI", 80, ([System.Drawing.FontStyle]::Bold),    ([System.Drawing.GraphicsUnit]::Pixel)
    $taglineFont = New-Object System.Drawing.Font "Segoe UI", 26, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $featureFont = New-Object System.Drawing.Font "Segoe UI", 20, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)

    $titleBr   = New-Object System.Drawing.SolidBrush $primaryText
    $taglineBr = New-Object System.Drawing.SolidBrush $mutedText
    $featureBr = New-Object System.Drawing.SolidBrush $primaryText
    $accentBr  = New-Object System.Drawing.SolidBrush $accentColor

    $textX = [int]($Height + 40)
    $g.DrawString("Code Radar", $titleFont, $titleBr, [float]$textX, 150.0)

    # Accent underline bar
    $accentPen = New-Object System.Drawing.Pen $accentColor, 4
    $g.DrawLine($accentPen, [float]$textX, 250.0, [float]($textX + 140), 250.0)
    $accentPen.Dispose()

    $g.DrawString("A modern debugging companion for Visual Studio",
                  $taglineFont, $taglineBr, [float]$textX, 280.0)

    $featureY = 360.0
    foreach ($line in @(
        "  LINQ chain decomposer",
        "  Snapshot & compare objects",
        "  Search across watch trees",
        "  Time-travel history per watch",
        "  Reveal properties as watches"
    )) {
        # Bullet
        $g.FillEllipse($accentBr, [float]($textX + 4), [float]($featureY + 13), 8, 8)
        $g.DrawString($line, $featureFont, $featureBr, [float]($textX + 20), [float]$featureY)
        $featureY += 42
    }

    $titleFont.Dispose(); $taglineFont.Dispose(); $featureFont.Dispose()
    $titleBr.Dispose();   $taglineBr.Dispose();   $featureBr.Dispose(); $accentBr.Dispose()

    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    $c.Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $c.Bitmap.Dispose()
    Write-Host "wrote $Path ($Width x $Height)"
}

# Resolve to an absolute path so the script behaves the same regardless of CWD.
$OutDir = [System.IO.Path]::GetFullPath($OutDir)
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

New-RadarIcon -Size 128 -Path (Join-Path $OutDir "icon.png")
New-RadarIcon -Size 300 -Path (Join-Path $OutDir "preview.png")
New-HeroImage          -Path (Join-Path $OutDir "hero.png")

# ==================================================================
# VSCT command icon strip. One 16x16 slot per command, transparent
# background. Used by the editor context menu (Show Radar, Add Watch,
# View/Export, Decompose LINQ, Show Image).
# ==================================================================

function New-CommandIconStrip {
    param([string]$Path)

    $tile = 16
    $slots = 5
    $w = $tile * $slots
    $h = $tile

    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

    # Transparent background
    $g.Clear([System.Drawing.Color]::Transparent)

    $penGold   = New-Object System.Drawing.Pen $accentColor, 1.5
    $brushGold = New-Object System.Drawing.SolidBrush $accentColor
    $penSoft   = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(160, $accentColor.R, $accentColor.G, $accentColor.B)), 1

    # ----- Slot 0: Show Radar window (two concentric circles + dot + blip) -----
    $x = 0
    $g.DrawEllipse($penGold, $x + 1.5, 1.5, 12, 12)
    $g.DrawEllipse($penSoft, $x + 4,   4,   7,  7)
    $g.FillEllipse($brushGold, $x + 7, 7, 2, 2)
    $g.FillEllipse($brushGold, $x + 11, 4, 2, 2)   # blip

    # ----- Slot 1: Add watch (magnifier + plus) -----
    $x = $tile
    $g.DrawEllipse($penGold, $x + 1, 1, 9, 9)
    # Handle
    $g.DrawLine($penGold, $x + 9.5, 9.5, $x + 14, 14)
    # Plus inside the lens
    $g.DrawLine($penGold, $x + 5.5, 3.5, $x + 5.5, 7.5)
    $g.DrawLine($penGold, $x + 3.5, 5.5, $x + 7.5, 5.5)

    # ----- Slot 2: View/Export (document + down arrow) -----
    $x = $tile * 2
    $g.DrawRectangle($penGold, $x + 2, 1, 8, 9)
    # Arrow shaft
    $g.DrawLine($penGold, $x + 6, 6, $x + 6, 14)
    # Arrow head
    $arrowHead = @(
        (New-Object System.Drawing.PointF ($x + 6), 15),
        (New-Object System.Drawing.PointF ($x + 3), 12),
        (New-Object System.Drawing.PointF ($x + 9), 12)
    )
    $g.FillPolygon($brushGold, $arrowHead)

    # ----- Slot 3: Decompose LINQ (funnel / chain steps) -----
    $x = $tile * 3
    # 3 decreasing bars
    $g.FillRectangle($brushGold, $x + 1, 3,  12, 2)
    $g.FillRectangle($brushGold, $x + 3, 7,  8,  2)
    $g.FillRectangle($brushGold, $x + 5, 11, 4,  2)

    # ----- Slot 4: Show image (picture frame + mountain + sun) -----
    $x = $tile * 4
    $g.DrawRectangle($penGold, $x + 1, 2, 12, 10)
    # Sun in upper-right
    $g.FillEllipse($brushGold, $x + 9, 4, 2, 2)
    # Mountain
    $mountain = @(
        (New-Object System.Drawing.PointF ($x + 2),  11),
        (New-Object System.Drawing.PointF ($x + 5),  6),
        (New-Object System.Drawing.PointF ($x + 8),  11)
    )
    $g.FillPolygon($brushGold, $mountain)
    $smallMountain = @(
        (New-Object System.Drawing.PointF ($x + 6),  11),
        (New-Object System.Drawing.PointF ($x + 9),  8),
        (New-Object System.Drawing.PointF ($x + 12), 11)
    )
    $g.FillPolygon($brushGold, $smallMountain)

    $penGold.Dispose(); $brushGold.Dispose(); $penSoft.Dispose()
    $g.Dispose()

    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "wrote $Path ($w x $h, $slots slots)"
}

$resourcesDir = [System.IO.Path]::GetFullPath((Join-Path $OutDir "..\Resources"))
New-CommandIconStrip -Path (Join-Path $resourcesDir "CommandIcons.png")

Write-Host ""
Write-Host "Done. Assets in $OutDir" -ForegroundColor Green
Write-Host "      Icon strip in $resourcesDir" -ForegroundColor Green
