<#
.SYNOPSIS
    Publishes WindowsAI as an unpackaged, self-contained app and builds the Inno Setup installer.

.DESCRIPTION
    1. dotnet publish with WindowsPackageType=None + self-contained WindowsAppSDK, so the app runs
       on a clean PC with no MSIX identity and no separately-installed .NET / WindowsAppSDK runtime.
       (Self-containing the WindowsAppSDK runtime + bootstrap auto-init fixes the historical bare-exe
       REGDB_E_CLASSNOTREG crash.)
    2. iscc compiles installer\WindowsAI.iss against the publish output.

.PARAMETER Configuration
    Build configuration (default: Release).

.PARAMETER Rid
    Runtime identifier (default: win-x64; win-arm64 also supported).

.EXAMPLE
    pwsh installer\build.ps1
    pwsh installer\build.ps1 -Rid win-arm64
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Rid = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repoRoot "src\WindowsAI.App\WindowsAI.App.csproj"
$platform = if ($Rid -eq "win-arm64") { "ARM64" } else { "x64" }
# Publish to an explicit, predictable folder (the default publish path varies with Platform).
$publishDir = Join-Path $PSScriptRoot "publish\$Rid"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

Write-Host "==> Publishing $Rid ($Configuration), unpackaged + self-contained..." -ForegroundColor Cyan
dotnet publish $proj `
    -c $Configuration `
    -r $Rid `
    -p:Platform=$platform `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:SelfContained=true `
    -p:PublishReadyToRun=true `
    --output $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

if (-not (Test-Path (Join-Path $publishDir "WindowsAI.App.exe"))) {
    throw "Publish output not found at $publishDir"
}
Write-Host "    Published to $publishDir" -ForegroundColor Green

# Resolve the app version from the built assembly.
$exe = Join-Path $publishDir "WindowsAI.App.exe"
$version = (Get-Item $exe).VersionInfo.ProductVersion
if (-not $version) { $version = "1.0.0" }
$version = ($version -split '\+')[0]   # strip any build metadata

# Locate the Inno Setup compiler.
$iscc = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    foreach ($candidate in @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $candidate) { $iscc = $candidate; break }
    }
}
if (-not $iscc) {
    Write-Warning "Inno Setup (ISCC.exe) was not found. Install it from https://jrsoftware.org/isdl.php, then re-run."
    Write-Host   "Publish output is ready at: $publishDir" -ForegroundColor Yellow
    return
}

$iss = Join-Path $PSScriptRoot "WindowsAI.iss"
Write-Host "==> Compiling installer (v$version)..." -ForegroundColor Cyan
& $iscc "/DMyAppSrc=$publishDir" "/DMyAppVersion=$version" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)." }

Write-Host "==> Installer written to $(Join-Path $PSScriptRoot 'output')" -ForegroundColor Green
