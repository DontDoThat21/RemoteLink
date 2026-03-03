# Build-Msix.ps1
# Builds the RemoteLink Desktop UI as an MSIX package for sideloading.
#
# Usage:
#   .\scripts\Build-Msix.ps1                      # Build + package (Release)
#   .\scripts\Build-Msix.ps1 -Configuration Debug # Build in Debug
#   .\scripts\Build-Msix.ps1 -SkipCertificate     # Skip cert creation (use existing)
#   .\scripts\Build-Msix.ps1 -GenerateIcons        # Regenerate icon assets first
#
# Prerequisites:
#   - .NET 10 SDK
#   - MAUI workload: dotnet workload install maui-windows
#   - Windows 10 SDK (10.0.19041.0+)

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [switch]$SkipCertificate,
    [switch]$GenerateIcons,
    [switch]$Unpackaged
)

$ErrorActionPreference = "Stop"
$RepoRoot = Resolve-Path "$PSScriptRoot\.."
$ProjectDir = "$RepoRoot\src\RemoteLink.Desktop.UI\RemoteLink.Desktop.UI"
$PublishDir = "$RepoRoot\publish\msix"
$CertSubject = "CN=RemoteLink"
$CertStorePath = "Cert:\CurrentUser\My"
$CertPfxPath = "$RepoRoot\scripts\RemoteLink-Dev.pfx"
$CertPassword = "RemoteLinkDev2026"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  RemoteLink MSIX Build Script" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ── Step 1: Generate icons if requested ──────────────────────────────────────
if ($GenerateIcons) {
    Write-Host "[1/5] Generating icon assets..." -ForegroundColor Yellow
    & "$PSScriptRoot\Generate-Icons.ps1"
    Write-Host ""
} else {
    Write-Host "[1/5] Icon generation skipped (use -GenerateIcons to regenerate)" -ForegroundColor DarkGray
}

# ── Step 2: Create self-signed certificate (if needed) ───────────────────────
if ($Unpackaged) {
    Write-Host "[2/5] Unpackaged build — no certificate needed" -ForegroundColor DarkGray
} elseif ($SkipCertificate) {
    Write-Host "[2/5] Certificate creation skipped (-SkipCertificate)" -ForegroundColor DarkGray
} else {
    Write-Host "[2/5] Checking development certificate..." -ForegroundColor Yellow

    # Check if cert already exists in the store
    $existingCert = Get-ChildItem $CertStorePath | Where-Object { $_.Subject -eq $CertSubject } | Select-Object -First 1

    if ($existingCert) {
        Write-Host "  Found existing certificate: $($existingCert.Thumbprint)" -ForegroundColor Green
    } else {
        Write-Host "  Creating new self-signed certificate..." -ForegroundColor Yellow
        $cert = New-SelfSignedCertificate `
            -Type Custom `
            -Subject $CertSubject `
            -KeyUsage DigitalSignature `
            -FriendlyName "RemoteLink Development" `
            -CertStoreLocation $CertStorePath `
            -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
            -NotAfter (Get-Date).AddYears(5)

        $existingCert = $cert
        Write-Host "  Created certificate: $($cert.Thumbprint)" -ForegroundColor Green

        # Export PFX for CI/CD use
        $securePassword = ConvertTo-SecureString -String $CertPassword -Force -AsPlainText
        Export-PfxCertificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" `
            -FilePath $CertPfxPath `
            -Password $securePassword | Out-Null
        Write-Host "  Exported PFX to: $CertPfxPath" -ForegroundColor Green
    }

    $thumbprint = $existingCert.Thumbprint
    Write-Host "  Thumbprint: $thumbprint" -ForegroundColor Cyan
    Write-Host ""
}

# ── Step 3: Restore dependencies ─────────────────────────────────────────────
Write-Host "[3/5] Restoring dependencies..." -ForegroundColor Yellow
dotnet restore "$ProjectDir\RemoteLink.Desktop.UI.csproj"
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet restore failed" -ForegroundColor Red
    exit 1
}
Write-Host ""

# ── Step 4: Build ────────────────────────────────────────────────────────────
Write-Host "[4/5] Building ($Configuration)..." -ForegroundColor Yellow

if ($Unpackaged) {
    # Build as unpackaged (no MSIX)
    dotnet publish "$ProjectDir\RemoteLink.Desktop.UI.csproj" `
        -c $Configuration `
        -f net10.0-windows10.0.19041.0 `
        /p:WindowsPackageType=None `
        /p:RuntimeIdentifier=win-x64 `
        -o "$RepoRoot\publish\standalone"

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Build failed" -ForegroundColor Red
        exit 1
    }
    Write-Host ""
    Write-Host "[5/5] Unpackaged build complete!" -ForegroundColor Green
    Write-Host "  Output: $RepoRoot\publish\standalone" -ForegroundColor Cyan
} else {
    # Build as MSIX package
    $buildArgs = @(
        "publish"
        "$ProjectDir\RemoteLink.Desktop.UI.csproj"
        "-c", $Configuration
        "-f", "net10.0-windows10.0.19041.0"
        "/p:RuntimeIdentifier=win-x64"
    )

    if ($thumbprint) {
        $buildArgs += "/p:PackageCertificateThumbprint=$thumbprint"
    }

    & dotnet @buildArgs

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Build failed" -ForegroundColor Red
        exit 1
    }
    Write-Host ""

    # ── Step 5: Summary ──────────────────────────────────────────────────────
    Write-Host "[5/5] MSIX package build complete!" -ForegroundColor Green
    Write-Host ""

    # Find the generated MSIX
    $msixFiles = Get-ChildItem -Path $PublishDir -Filter "*.msix" -ErrorAction SilentlyContinue
    if ($msixFiles) {
        foreach ($f in $msixFiles) {
            $sizeMB = [math]::Round($f.Length / 1MB, 2)
            Write-Host "  Package: $($f.FullName)" -ForegroundColor Cyan
            Write-Host "  Size:    $sizeMB MB" -ForegroundColor Cyan
        }
    } else {
        Write-Host "  Check $PublishDir for output files." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Installation Instructions" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
if (-not $Unpackaged) {
    Write-Host ""
    Write-Host "  For sideloading (development):" -ForegroundColor White
    Write-Host "    1. Double-click the .msix file" -ForegroundColor Gray
    Write-Host "    2. Or: Add-AppxPackage -Path <path>.msix" -ForegroundColor Gray
    Write-Host ""
    Write-Host "  For first-time installs, trust the certificate:" -ForegroundColor White
    Write-Host "    1. Right-click .msix > Properties > Digital Signatures" -ForegroundColor Gray
    Write-Host "    2. Select the signer > Details > View Certificate > Install" -ForegroundColor Gray
    Write-Host "    3. Place in 'Trusted People' store" -ForegroundColor Gray
    Write-Host "    Or run: Import-Certificate -FilePath scripts\RemoteLink-Dev.pfx -CertStoreLocation Cert:\LocalMachine\TrustedPeople" -ForegroundColor Gray
}
Write-Host ""
