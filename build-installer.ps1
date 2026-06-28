<#
.SYNOPSIS
    Builds the SwiftClean installer end-to-end.

.DESCRIPTION
    1. Publishes the SwiftClean app self-contained (win-x64).
    2. Zips the publish output into the installer's embedded payload (payload.zip).
    3. Publishes the installer as a single self-contained exe -> dist/SwiftCleanSetup.exe.

    Run from the repository root:  ./build-installer.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$installerProj = Join-Path $root 'installer/SwiftClean.Installer/SwiftClean.Installer.csproj'
$appProj = Join-Path $root 'SwiftClean.csproj'
$stage = Join-Path $root 'installer-stage/app'
$payload = Join-Path $root 'installer/SwiftClean.Installer/Resources/payload.zip'
$dist = Join-Path $root 'dist'

function Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

# ── 1. Publish the app (self-contained) ────────────────────────────────
Step '1/3  Publishing SwiftClean (self-contained)'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
dotnet publish $appProj -c $Configuration -r $Runtime --self-contained `
    -p:PublishSingleFile=false -p:DebugType=none -o $stage
if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }

# ── 2. Zip the payload ──────────────────────────────────────────────────
Step '2/3  Packing payload.zip'
New-Item -ItemType Directory -Force -Path (Split-Path $payload) | Out-Null
if (Test-Path $payload) { Remove-Item $payload -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $payload -CompressionLevel Optimal
$payloadMb = [math]::Round((Get-Item $payload).Length / 1MB, 1)
Write-Host "    payload.zip = $payloadMb MB"

# ── 3. Publish the installer (single self-contained exe) ───────────────
Step '3/3  Publishing installer (single file)'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
dotnet publish $installerProj -c $Configuration -o $dist
if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed.' }

$setup = Get-ChildItem $dist -Filter 'SwiftCleanSetup.exe' | Select-Object -First 1
if ($null -eq $setup) { throw 'SwiftCleanSetup.exe not found in dist/.' }
$setupMb = [math]::Round($setup.Length / 1MB, 1)

Write-Host "`nDone." -ForegroundColor Green
Write-Host "  Setup: $($setup.FullName)  ($setupMb MB)"
