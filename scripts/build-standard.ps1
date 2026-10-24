# ============================================================================
# build-standard.ps1
# Build the Standard edition (.NET Framework 4.8, system runtime, small binary).
# Compiles all .cs under src/ with the built-in csc.exe into
# dist/v1.0.0/MobileUI-Config-Standard-v1.0.0.exe.
# The Standard edition requires .NET Framework 4.8 installed on the target machine.
# Note: csc is the .NET Framework 4 compiler (C# 5 surface); do not use newer
# language features (null-conditional, expression members, nameof, inline out).
# ============================================================================

$ErrorActionPreference = 'Stop'

# scripts/ directory -> repository root
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $scriptDir
Set-Location $root

$version = 'v1.0.0'
$outDir = Join-Path $root (Join-Path 'dist' 'v1.0.0')
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$outExe = Join-Path $outDir ('MobileUI-Config-Standard-' + $version + '.exe')

# Locate .NET Framework 4.8 built-in csc.exe
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) {
    throw 'csc.exe not found. Install the .NET Framework 4.8 Developer Pack.'
}

# Include every .cs file in src/ (*.bak / *.old are not .cs and are excluded)
$sources = Get-ChildItem -Path (Join-Path $root 'src') -Filter '*.cs' |
    ForEach-Object { $_.FullName }

# System assemblies needed by the desktop UI, XML config and compression support
$refs = @(
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.Xml.dll',
    '/r:System.IO.Compression.dll',
    '/r:System.IO.Compression.FileSystem.dll'
)

$compilerArgs = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    ('/out:' + $outExe),
    ('/win32icon:' + (Join-Path $root 'app.ico')),
    ('/resource:' + (Join-Path $root 'app.ico') + ',app.ico'),
    ('/win32manifest:' + (Join-Path $root 'app.manifest'))
) + $refs + $sources

& $csc @compilerArgs
if ($LASTEXITCODE -ne 0) {
    throw ("Standard build failed, exit code " + $LASTEXITCODE)
}

# Remove Mark-of-the-Web so a locally built exe is not blocked by SmartScreen
try {
    Unblock-File -Path $outExe -ErrorAction SilentlyContinue
    Remove-Item -Path $outExe -Stream Zone.Identifier -ErrorAction SilentlyContinue
} catch { }

# Carry the third-party native library next to the exe
Copy-Item -LiteralPath (Join-Path $root 'libs\ViGEmClient.dll') `
    -Destination (Join-Path $outDir 'ViGEmClient.dll') -Force

Write-Host ("Standard build done: " + $outExe)
