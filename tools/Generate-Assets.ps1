<#
.SYNOPSIS
    Generates Code Radar marketplace assets (icon, preview, hero) procedurally
    using System.Drawing so no external tools are required.

.DESCRIPTION
    Produces:
      assets\icon.png             128x128    VSIX icon (source.extension.vsixmanifest)
      assets\preview.png          300x300    Marketplace preview tile
      assets\hero.png             1280x640   Marketplace listing hero banner
      Resources\CommandIcons.png  80x16      VSCT command-icon strip (5 slots)

    Art direction: a stylized microchip seen top-down with an orange-to-red
    gradient face and dark pin stubs around the edges. A teal debug bug sits on
    the chip, half-covered by a magnifying glass with an orange/red lens.
#>

param(
    [string]$OutDir = (Join-Path $PSScriptRoot "..\src\CodeRadar\assets")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

# --- Palette ---
$ColorDark       = [System.Drawing.Color]::FromArgb(255,  24,  22,  24)  # outline / pins
$ColorChipA      = [System.Drawing.Color]::FromArgb(255, 255, 138,  64)  # chip face gradient top-left
$ColorChipB      = [System.Drawing.Color]::FromArgb(255, 226,  58,  45)  # chip face gradient bottom-right
$ColorLensA      = [System.Drawing.Color]::FromArgb(255, 255, 150,  90)  # lens gradient top-left
$ColorLensB      = [System.Drawing.Color]::FromArgb(255, 232,  72,  58)  # lens gradient bottom-right
$ColorBugA       = [System.Drawing.Color]::FromArgb(255,  77, 215, 198)  # bug body highlight
$ColorBugB       = [System.Drawing.Color]::FromArgb(255,  42, 168, 156)  # bug body shadow
$ColorHighlight  = [System.Drawing.Color]::FromArgb(220, 255, 255, 255)  # glossy highlight
$ColorPanelBg    = [System.Drawing.Color]::FromArgb(255,  30,  30,  30)
$ColorPrimaryTxt = [System.Drawing.Color]::FromArgb(255, 241, 241, 241)
$ColorMutedTxt   = [System.Drawing.Color]::FromArgb(255, 181, 181, 181)
$ColorAccent     = [System.Drawing.Color]::FromArgb(255, 255, 200,  61)

function New-Canvas {
    param([int]$Width, [int]$Height, [bool]$Transparent = $true)
    $bmp = New-Object System.Drawing.Bitmap $Width, $Height
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    if ($Transparent) { $g.Clear([System.Drawing.Color]::Transparent) }
    return @{ Bitmap = $bmp; Graphics = $g }
}

function Get-RoundedRectPath {
    param([float]$X, [float]$Y, [float]$W, [float]$H, [float]$R)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $R * 2.0
    $path.AddArc($X,           $Y,           $d, $d, 180, 90)
    $path.AddArc($X + $W - $d, $Y,           $d, $d, 270, 90)
    $path.AddArc($X + $W - $d, $Y + $H - $d, $d, $d,   0, 90)
    $path.AddArc($X,           $Y + $H - $d, $d, $d,  90, 90)
    $path.CloseFigure()
    return $path
}

function New-SolidBrush { param($Color) return New-Object System.Drawing.SolidBrush $Color }

function New-Pen {
    param($Color, [float]$Width)
    $p = New-Object System.Drawing.Pen $Color, $Width
    $p.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $p.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $p.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    return $p
}

function Draw-Chip {
    <# Microchip: rounded-square dark frame with gradient face and pin stubs. #>
    param([System.Drawing.Graphics]$G, [int]$Size)

    $s = [float]$Size / 128.0

    $chipX = 12.0 * $s
    $chipY = 12.0 * $s
    $chipW = 104.0 * $s
    $chipH = 104.0 * $s
    $chipR = 18.0 * $s

    # Pins (dark stubs) protruding outward from each side.
    # Drawn before the chip body so the hidden portion merges cleanly.
    $pinBrush = New-SolidBrush $ColorDark
    $pinCount = 4
    $pinW = 8.0 * $s
    $pinOut = 7.0 * $s   # how far pins stick out past the chip edge
    $pinIn  = 4.0 * $s   # overlap into the chip (hidden by the chip face)
    $pinThickness = $pinOut + $pinIn
    $pinRadius = 2.0 * $s
    $edgeMargin = 14.0 * $s
    $travel = $chipW - (2.0 * $edgeMargin)
    $step = $travel / ($pinCount - 1)

    for ($i = 0; $i -lt $pinCount; $i++) {
        $offset = $edgeMargin + $step * $i - ($pinW / 2.0)

        # Top pin
        $p = Get-RoundedRectPath ($chipX + $offset) ($chipY - $pinOut) $pinW $pinThickness $pinRadius
        $G.FillPath($pinBrush, $p); $p.Dispose()

        # Bottom pin
        $p = Get-RoundedRectPath ($chipX + $offset) ($chipY + $chipH - $pinIn) $pinW $pinThickness $pinRadius
        $G.FillPath($pinBrush, $p); $p.Dispose()

        # Left pin
        $p = Get-RoundedRectPath ($chipX - $pinOut) ($chipY + $offset) $pinThickness $pinW $pinRadius
        $G.FillPath($pinBrush, $p); $p.Dispose()

        # Right pin
        $p = Get-RoundedRectPath ($chipX + $chipW - $pinIn) ($chipY + $offset) $pinThickness $pinW $pinRadius
        $G.FillPath($pinBrush, $p); $p.Dispose()
    }
    $pinBrush.Dispose()

    # Chip body: dark outer frame (stroked + filled rounded rect).
    $bodyPath = Get-RoundedRectPath $chipX $chipY $chipW $chipH $chipR

    $outerPen = New-Pen $ColorDark (7.0 * $s)
    $G.DrawPath($outerPen, $bodyPath)
    $outerPen.Dispose()

    # Gradient face (slightly inset so the dark frame is visible around it).
    $faceInset = 3.5 * $s
    $faceX = $chipX + $faceInset
    $faceY = $chipY + $faceInset
    $faceW = $chipW - (2.0 * $faceInset)
    $faceH = $chipH - (2.0 * $faceInset)
    $faceR = $chipR - ($faceInset * 0.9)
    $facePath = Get-RoundedRectPath $faceX $faceY $faceW $faceH $faceR

    $faceRect = New-Object System.Drawing.RectangleF $faceX, $faceY, $faceW, $faceH
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush `
        $faceRect, $ColorChipA, $ColorChipB, ([System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $G.FillPath($grad, $facePath)
    $grad.Dispose()

    # Soft top-left gloss.
    $glossBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush `
        $faceRect, ([System.Drawing.Color]::FromArgb(55, 255, 255, 255)), `
                   ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)), `
                   ([System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $G.FillPath($glossBrush, $facePath)
    $glossBrush.Dispose()

    $facePath.Dispose()
    $bodyPath.Dispose()
}

function Draw-Bug {
    <# Stylized teal debug bug. Centered at (Cx, Cy) scaled by S. #>
    param([System.Drawing.Graphics]$G, [float]$Cx, [float]$Cy, [float]$S)

    $outlineWidth = 5.0 * $S
    $legWidth     = 3.5 * $S
    $outlinePen   = New-Pen $ColorDark $outlineWidth
    $legPen       = New-Pen $ColorDark $legWidth

    # --- Legs (drawn first, under the body) ---
    # Two-segment lines per leg so they bend at the knee.
    $legSets = @(
        @{ Side = -1; YOff =  -6.0; KneeOut = 22.0; KneeDown =  -4.0; FootOut = 30.0; FootDown =   8.0 },
        @{ Side = -1; YOff =   6.0; KneeOut = 24.0; KneeDown =   0.0; FootOut = 32.0; FootDown =  14.0 },
        @{ Side = -1; YOff =  18.0; KneeOut = 22.0; KneeDown =   8.0; FootOut = 28.0; FootDown =  22.0 },
        @{ Side =  1; YOff =  -6.0; KneeOut = 22.0; KneeDown =  -4.0; FootOut = 30.0; FootDown =   8.0 },
        @{ Side =  1; YOff =   6.0; KneeOut = 24.0; KneeDown =   0.0; FootOut = 32.0; FootDown =  14.0 },
        @{ Side =  1; YOff =  18.0; KneeOut = 22.0; KneeDown =   8.0; FootOut = 28.0; FootDown =  22.0 }
    )
    foreach ($leg in $legSets) {
        $side = [float]$leg.Side
        $x0 = $Cx + $side * (8.0 * $S)
        $y0 = $Cy + ([float]$leg.YOff * $S)
        $xk = $Cx + $side * ([float]$leg.KneeOut * $S)
        $yk = $Cy + ([float]$leg.KneeDown * $S)
        $xf = $Cx + $side * ([float]$leg.FootOut * $S)
        $yf = $Cy + ([float]$leg.FootDown * $S)
        $G.DrawLine($legPen, $x0, $y0, $xk, $yk)
        $G.DrawLine($legPen, $xk, $yk, $xf, $yf)
    }

    # --- Antennae (curves from the head) ---
    $antPen = New-Pen $ColorDark (3.2 * $S)
    $headTopY = $Cy - (22.0 * $S)
    $antStartLX = $Cx - (5.0 * $S);  $antStartLY = $headTopY
    $antStartRX = $Cx + (5.0 * $S);  $antStartRY = $headTopY
    $antEndLX   = $Cx - (20.0 * $S); $antEndLY   = $Cy - (38.0 * $S)
    $antEndRX   = $Cx + (20.0 * $S); $antEndRY   = $Cy - (38.0 * $S)
    $antCtrlLX  = $Cx - (18.0 * $S); $antCtrlLY  = $Cy - (26.0 * $S)
    $antCtrlRX  = $Cx + (18.0 * $S); $antCtrlRY  = $Cy - (26.0 * $S)
    $G.DrawBezier($antPen, $antStartLX, $antStartLY, $antCtrlLX, $antCtrlLY, $antCtrlLX, $antCtrlLY, $antEndLX, $antEndLY)
    $G.DrawBezier($antPen, $antStartRX, $antStartRY, $antCtrlRX, $antCtrlRY, $antCtrlRX, $antCtrlRY, $antEndRX, $antEndRY)

    # Antenna bulbs
    $bulbR = 2.2 * $S
    $bulbBrush = New-SolidBrush $ColorDark
    $G.FillEllipse($bulbBrush, ($antEndLX - $bulbR), ($antEndLY - $bulbR), ($bulbR * 2.0), ($bulbR * 2.0))
    $G.FillEllipse($bulbBrush, ($antEndRX - $bulbR), ($antEndRY - $bulbR), ($bulbR * 2.0), ($bulbR * 2.0))
    $bulbBrush.Dispose()
    $antPen.Dispose()

    # --- Body (abdomen) ---
    $bodyW = 28.0 * $S
    $bodyH = 36.0 * $S
    $bodyX = $Cx - ($bodyW / 2.0)
    $bodyY = $Cy - (10.0 * $S)
    $bodyRect = New-Object System.Drawing.RectangleF $bodyX, $bodyY, $bodyW, $bodyH

    $bodyGrad = New-Object System.Drawing.Drawing2D.LinearGradientBrush `
        $bodyRect, $ColorBugA, $ColorBugB, ([System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $G.FillEllipse($bodyGrad, $bodyX, $bodyY, $bodyW, $bodyH)
    $bodyGrad.Dispose()
    $G.DrawEllipse($outlinePen, $bodyX, $bodyY, $bodyW, $bodyH)

    # Centerline down the body
    $centerPen = New-Pen $ColorDark (2.6 * $S)
    $G.DrawLine($centerPen, $Cx, $bodyY + (2.0 * $S), $Cx, $bodyY + $bodyH - (2.0 * $S))
    $centerPen.Dispose()

    # --- Head ---
    $headW = 22.0 * $S
    $headH = 18.0 * $S
    $headX = $Cx - ($headW / 2.0)
    $headY = $Cy - (24.0 * $S)
    $headRect = New-Object System.Drawing.RectangleF $headX, $headY, $headW, $headH
    $headGrad = New-Object System.Drawing.Drawing2D.LinearGradientBrush `
        $headRect, $ColorBugA, $ColorBugB, ([System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $G.FillEllipse($headGrad, $headX, $headY, $headW, $headH)
    $headGrad.Dispose()
    $G.DrawEllipse($outlinePen, $headX, $headY, $headW, $headH)

    # Eyes
    $eyeR = 2.4 * $S
    $eyeBrush = New-SolidBrush $ColorDark
    $G.FillEllipse($eyeBrush, $Cx - (5.5 * $S) - $eyeR, $Cy - (17.0 * $S) - $eyeR, $eyeR * 2.0, $eyeR * 2.0)
    $G.FillEllipse($eyeBrush, $Cx + (5.5 * $S) - $eyeR, $Cy - (17.0 * $S) - $eyeR, $eyeR * 2.0, $eyeR * 2.0)
    $eyeBrush.Dispose()

    # Body gloss highlight
    $highlightBrush = New-SolidBrush ([System.Drawing.Color]::FromArgb(160, 255, 255, 255))
    $glossW = 6.0 * $S
    $glossH = 14.0 * $S
    $G.FillEllipse($highlightBrush, $Cx - (9.0 * $S), $bodyY + (3.0 * $S), $glossW, $glossH)
    $highlightBrush.Dispose()

    $outlinePen.Dispose()
    $legPen.Dispose()
}

function Draw-MagnifyingGlass {
    <# Magnifying glass with orange/red lens, centered on (Cx, Cy) at scale S.
       The handle points towards the lower-right. #>
    param([System.Drawing.Graphics]$G, [float]$Cx, [float]$Cy, [float]$S)

    $lensOuterR = 22.0 * $S
    $lensInnerR = 17.0 * $S

    # Handle: a rounded rectangle rotated 45 degrees, anchored at the lens edge.
    $handleLen   = 28.0 * $S
    $handleWidth = 10.0 * $S

    $state = $G.Save()
    $angleDeg = 45.0
    $G.TranslateTransform($Cx, $Cy)
    $G.RotateTransform($angleDeg)

    # Handle starts just outside the lens ring.
    $handleStart = $lensOuterR - (1.5 * $S)
    $handlePath  = Get-RoundedRectPath $handleStart (-1.0 * $handleWidth / 2.0) $handleLen $handleWidth ($handleWidth / 2.0)

    $handleFill  = New-SolidBrush $ColorDark
    $G.FillPath($handleFill, $handlePath)
    $handleFill.Dispose()

    # Inner lighter stroke for depth (thin gray line along the handle).
    $handleInnerPath = Get-RoundedRectPath ($handleStart + 2.0 * $S) (-1.0 * ($handleWidth / 2.0) + 2.5 * $S) ($handleLen - 4.0 * $S) ($handleWidth - 5.0 * $S) (($handleWidth - 5.0 * $S) / 2.0)
    $handleInnerBrush = New-SolidBrush ([System.Drawing.Color]::FromArgb(255, 56, 48, 50))
    $G.FillPath($handleInnerBrush, $handleInnerPath)
    $handleInnerBrush.Dispose()
    $handleInnerPath.Dispose()
    $handlePath.Dispose()

    $G.Restore($state)

    # --- Lens ring (dark outer) ---
    $ringRect = New-Object System.Drawing.RectangleF ($Cx - $lensOuterR), ($Cy - $lensOuterR), ($lensOuterR * 2.0), ($lensOuterR * 2.0)
    $ringBrush = New-SolidBrush $ColorDark
    $G.FillEllipse($ringBrush, $ringRect)
    $ringBrush.Dispose()

    # --- Lens glass (gradient) ---
    $glassRect = New-Object System.Drawing.RectangleF ($Cx - $lensInnerR), ($Cy - $lensInnerR), ($lensInnerR * 2.0), ($lensInnerR * 2.0)
    $glassGrad = New-Object System.Drawing.Drawing2D.LinearGradientBrush `
        $glassRect, $ColorLensA, $ColorLensB, ([System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $G.FillEllipse($glassGrad, $glassRect)
    $glassGrad.Dispose()

    # --- Specular highlight on the lens ---
    $highlightBrush = New-SolidBrush $ColorHighlight
    $hR1 = $lensInnerR * 0.55
    $hR2 = $lensInnerR * 0.22
    $hx  = $Cx - $lensInnerR * 0.42
    $hy  = $Cy - $lensInnerR * 0.42
    # Elongated primary highlight
    $G.FillEllipse($highlightBrush, $hx - $hR1 / 2.0, $hy - $hR1 / 3.0, $hR1, $hR1 / 1.5)
    # Secondary dot
    $G.FillEllipse($highlightBrush, $hx + $hR1 * 0.3, $hy + $hR1 * 0.1, $hR2, $hR2)
    $highlightBrush.Dispose()
}

function New-AppIcon {
    param([int]$Size, [string]$Path)
    $c = New-Canvas -Width $Size -Height $Size
    $g = $c.Graphics

    $s = [float]$Size / 128.0

    # Soft drop shadow beneath the chip so the icon pops on any background.
    $shadowBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(40, 0, 0, 0))
    $sdx = 0.0
    $sdy = 4.0 * $s
    $shadowPath = Get-RoundedRectPath (12.0 * $s + $sdx) (12.0 * $s + $sdy) (104.0 * $s) (104.0 * $s) (18.0 * $s)
    $g.FillPath($shadowBrush, $shadowPath)
    $shadowPath.Dispose()
    $shadowBrush.Dispose()

    Draw-Chip -G $g -Size $Size

    # Bug sits slightly left of center, magnifier upper-right.
    $bugCx = 52.0 * $s
    $bugCy = 72.0 * $s
    Draw-Bug -G $g -Cx $bugCx -Cy $bugCy -S $s

    $lensCx = 82.0 * $s
    $lensCy = 58.0 * $s
    Draw-MagnifyingGlass -G $g -Cx $lensCx -Cy $lensCy -S $s

    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    $c.Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $c.Bitmap.Dispose()
    Write-Host "wrote $Path ($Size x $Size)"
}

function New-HeroImage {
    param([string]$Path, [int]$Width = 1280, [int]$Height = 640)
    $c = New-Canvas -Width $Width -Height $Height -Transparent $false
    $g = $c.Graphics

    # Dark gradient background.
    $bgRect = New-Object System.Drawing.RectangleF 0, 0, $Width, $Height
    $bgGrad = New-Object System.Drawing.Drawing2D.LinearGradientBrush `
        $bgRect, ([System.Drawing.Color]::FromArgb(255, 40, 40, 45)), `
                 ([System.Drawing.Color]::FromArgb(255, 22, 22, 22)), `
                 ([System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillRectangle($bgGrad, 0, 0, $Width, $Height)
    $bgGrad.Dispose()

    # Draw the app icon on the left side at a large size.
    $iconSize = 420
    $iconTmp  = New-Canvas -Width $iconSize -Height $iconSize
    $ig       = $iconTmp.Graphics
    $s = [float]$iconSize / 128.0

    Draw-Chip -G $ig -Size $iconSize
    Draw-Bug             -G $ig -Cx (52.0 * $s) -Cy (72.0 * $s) -S $s
    Draw-MagnifyingGlass -G $ig -Cx (82.0 * $s) -Cy (58.0 * $s) -S $s

    $iconX = 110
    $iconY = ($Height - $iconSize) / 2
    $g.DrawImage($iconTmp.Bitmap, $iconX, $iconY, $iconSize, $iconSize)
    $ig.Dispose(); $iconTmp.Bitmap.Dispose()

    # Right-side text block.
    $titleFont   = New-Object System.Drawing.Font "Segoe UI", 84, ([System.Drawing.FontStyle]::Bold),    ([System.Drawing.GraphicsUnit]::Pixel)
    $taglineFont = New-Object System.Drawing.Font "Segoe UI", 26, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
    $featureFont = New-Object System.Drawing.Font "Segoe UI", 20, ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)

    $titleBr   = New-SolidBrush $ColorPrimaryTxt
    $taglineBr = New-SolidBrush $ColorMutedTxt
    $featureBr = New-SolidBrush $ColorPrimaryTxt
    $accentBr  = New-SolidBrush $ColorAccent

    $textX = 600
    $g.DrawString("Code Radar", $titleFont, $titleBr, [float]$textX, 150.0)

    $accentPen = New-Pen $ColorAccent 4.0
    $g.DrawLine($accentPen, [float]$textX, 258.0, [float]($textX + 160), 258.0)
    $accentPen.Dispose()

    $g.DrawString("A debugging companion for Visual Studio",
                  $taglineFont, $taglineBr, [float]$textX, 288.0)

    $featureY = 370.0
    foreach ($line in @(
        "  LINQ chain decomposer",
        "  Snapshot & compare objects",
        "  Image preview from bytes / streams",
        "  Time-travel history per watch",
        "  Reveal properties as watches"
    )) {
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

# ==================================================================
# VSCT command-icon strip (16x16 per slot, 5 slots, transparent bg).
# Slot order matches VSCommandTable.vsct BitmapIds.
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
    $g.Clear([System.Drawing.Color]::Transparent)

    $penGold   = New-Object System.Drawing.Pen $ColorAccent, 1.5
    $brushGold = New-Object System.Drawing.SolidBrush $ColorAccent
    $penTeal   = New-Object System.Drawing.Pen $ColorBugA, 1.5
    $brushTeal = New-Object System.Drawing.SolidBrush $ColorBugA

    # Slot 0: Show Code Radar window (bug + mini magnifier)
    $x = 0
    $g.FillEllipse($brushTeal, $x + 2, 6, 7, 8)
    $g.DrawEllipse($penTeal,   $x + 2, 6, 7, 8)
    # mini magnifier
    $g.DrawEllipse($penGold, $x + 8, 2, 5, 5)
    $g.DrawLine($penGold, $x + 12.5, 6.5, $x + 15, 9)

    # Slot 1: Add watch (magnifier + plus)
    $x = $tile
    $g.DrawEllipse($penGold, $x + 1, 1, 9, 9)
    $g.DrawLine($penGold, $x + 9.5, 9.5, $x + 14, 14)
    $g.DrawLine($penGold, $x + 5.5, 3.5, $x + 5.5, 7.5)
    $g.DrawLine($penGold, $x + 3.5, 5.5, $x + 7.5, 5.5)

    # Slot 2: View / Export (document + down arrow)
    $x = $tile * 2
    $g.DrawRectangle($penGold, $x + 2, 1, 8, 9)
    $g.DrawLine($penGold, $x + 6, 6, $x + 6, 14)
    $arrowHead = @(
        (New-Object System.Drawing.PointF ($x + 6), 15),
        (New-Object System.Drawing.PointF ($x + 3), 12),
        (New-Object System.Drawing.PointF ($x + 9), 12)
    )
    $g.FillPolygon($brushGold, $arrowHead)

    # Slot 3: Decompose LINQ (decreasing bars)
    $x = $tile * 3
    $g.FillRectangle($brushGold, $x + 1, 3,  12, 2)
    $g.FillRectangle($brushGold, $x + 3, 7,  8,  2)
    $g.FillRectangle($brushGold, $x + 5, 11, 4,  2)

    # Slot 4: Show image (frame + sun + mountain)
    $x = $tile * 4
    $g.DrawRectangle($penGold, $x + 1, 2, 12, 10)
    $g.FillEllipse($brushGold, $x + 9, 4, 2, 2)
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

    $penGold.Dispose(); $brushGold.Dispose(); $penTeal.Dispose(); $brushTeal.Dispose()
    $g.Dispose()

    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "wrote $Path ($w x $h, $slots slots)"
}

# --- Run ---

$OutDir = [System.IO.Path]::GetFullPath($OutDir)
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

New-AppIcon   -Size 128 -Path (Join-Path $OutDir "icon.png")
New-AppIcon   -Size 300 -Path (Join-Path $OutDir "preview.png")
New-HeroImage           -Path (Join-Path $OutDir "hero.png")

$resourcesDir = [System.IO.Path]::GetFullPath((Join-Path $OutDir "..\Resources"))
New-CommandIconStrip -Path (Join-Path $resourcesDir "CommandIcons.png")

Write-Host ""
Write-Host "Done. Assets in $OutDir" -ForegroundColor Green
Write-Host "      Icon strip in $resourcesDir" -ForegroundColor Green
