# ============================================================================
# build-selfcontained.ps1
# Build the Self-Contained edition (.NET 8, bundled runtime, single file).
# Runs dotnet publish for win-x64 and copies the output to
# dist/v1.0.0/MobileUI-Config-SelfContained-v1.0.0.exe.
# The Self-Contained edition bundles the .NET 8 runtime and only supports
# Windows 10 1607+ / Windows 11 on x64.
# Prerequisite: .NET 8 SDK installed (`dotnet --version` works).
# ============================================================================

$ErrorActionPreference = 'Stop'

# scripts/ directory -> repository root
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $scriptDir
Set-Location $root

$version = 'v1.0.0'
$outDir = Join-Path $root (Join-Path 'dist' 'v1.0.0')
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# Intermediate publish directory (ignored by VCS; final artifacts go to dist/)
$publishDir = Join-Path $root 'publish_selfcontained'
if (Test-Path $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }

$csproj = Join-Path $root 'MobileUiConfig.SelfContained.csproj'

& dotnet publish $csproj `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw ("Self-contained publish failed, exit code " + $LASTEXITCODE)
}

# Pick the main exe from the publish folder and copy it with the versioned name
$sourceExe = Get-ChildItem -LiteralPath $publishDir -Filter '*.exe' |
    Where-Object { $_.Name -notlike '*.exe.config' } |
    Select-Object -First 1
if ($null -eq $sourceExe) {
    throw 'Main exe not found in the publish folder.'
}

$targetExe = Join-Path $outDir ('MobileUI-Config-SelfContained-' + $version + '.exe')
Copy-Item -LiteralPath $sourceExe.FullName -Destination $targetExe -Force

# Carry the third-party native library next to the exe
Copy-Item -LiteralPath (Join-Path $root 'libs\ViGEmClient.dll') `
    -Destination (Join-Path $outDir 'ViGEmClient.dll') -Force

Write-Host ("Self-contained build done: " + $targetExe)
