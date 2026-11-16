# Builds a Release copy into %LOCALAPPDATA%\Programs\UnityTimeTracker and starts it.
param([string]$Target = (Join-Path $env:LOCALAPPDATA 'Programs\UnityTimeTracker'))
$ErrorActionPreference = 'Stop'

$exe = Join-Path $Target 'UnityTimeTracker.exe'
if (Get-Process UnityTimeTracker -ErrorAction SilentlyContinue) {
    $any = if (Test-Path $exe) { $exe } else { (Get-Process UnityTimeTracker | Select-Object -First 1).Path }
    Start-Process $any -ArgumentList '--exit' -Wait
    Get-Process UnityTimeTracker -ErrorAction SilentlyContinue | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
}

dotnet publish "$PSScriptRoot\src\UnityTimeTracker" -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $Target
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Start-Process $exe
"Installed to $Target"
