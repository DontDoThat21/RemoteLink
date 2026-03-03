# Generate-Icons.ps1
# Generates the required PNG icon assets for MSIX packaging.
# Uses System.Drawing to create branded purple icons with "RL" text.
# Run: powershell -ExecutionPolicy Bypass -File scripts\Generate-Icons.ps1

param(
    [string]$OutputDir = "$PSScriptRoot\..\src\RemoteLink.Desktop.UI\RemoteLink.Desktop.UI\Platforms\Windows\Assets"
)

Add-Type -AssemblyName System.Drawing

function New-Icon {
    param(
        [string]$Path,
        [int]$Width,
        [int]$Height
    )

    $bmp = New-Object System.Drawing.Bitmap($Width, $Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    # Fill with brand purple (#512BD4)
    $purple = [System.Drawing.Color]::FromArgb(81, 43, 212)
    $g.Clear($purple)

    # Draw "RL" text centered in white
    $fontSize = [math]::Max(8, [math]::Floor($Height * 0.45))
    $font = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold)
    $brush = [System.Drawing.Brushes]::White
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF(0, 0, $Width, $Height)
    $g.DrawString("RL", $font, $brush, $rect, $sf)

    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  Created: $Path ($Width x $Height)"
}

# Ensure output directory exists
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

Write-Host "Generating MSIX icon assets..."
Write-Host "Output: $OutputDir"
Write-Host ""

# Required MSIX assets (name → width x height)
New-Icon -Path "$OutputDir\StoreLogo.png"         -Width 50   -Height 50
New-Icon -Path "$OutputDir\Square44x44Logo.png"   -Width 44   -Height 44
New-Icon -Path "$OutputDir\Square150x150Logo.png" -Width 150  -Height 150
New-Icon -Path "$OutputDir\Wide310x150Logo.png"   -Width 310  -Height 150
New-Icon -Path "$OutputDir\SmallTile.png"          -Width 71   -Height 71
New-Icon -Path "$OutputDir\LargeTile.png"          -Width 310  -Height 310
New-Icon -Path "$OutputDir\SplashScreen.png"       -Width 620  -Height 300

# Scaled variants for high-DPI (optional but recommended)
New-Icon -Path "$OutputDir\Square44x44Logo.scale-200.png"   -Width 88   -Height 88
New-Icon -Path "$OutputDir\Square150x150Logo.scale-200.png" -Width 300  -Height 300
New-Icon -Path "$OutputDir\Wide310x150Logo.scale-200.png"   -Width 620  -Height 300
New-Icon -Path "$OutputDir\StoreLogo.scale-200.png"         -Width 100  -Height 100

Write-Host ""
Write-Host "Done! All MSIX icon assets generated."
