# Builds Zones, installs it to %LOCALAPPDATA%\Zones, adds a Start Menu shortcut
# and starts it.
#
# Install here rather than running bin\zones.exe directly: "Start with Windows"
# works against the running exe's own path, so pointing it at a build output
# means deleting or moving the project breaks it.

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dest = Join-Path $env:LOCALAPPDATA "Zones"
$exe  = Join-Path $dest "zones.exe"

# A running instance holds a lock on the exe, which fails the build's own write
# when it was started from bin, and the copy below in every case.
Get-Process zones -ErrorAction Ignore | ForEach-Object {
    Write-Host "Stopping running instance (PID $($_.Id))"
    Stop-Process -Id $_.Id -Force
}
Start-Sleep -Milliseconds 400

Write-Host "Building..."
& (Join-Path $root "build.ps1")
if ($LASTEXITCODE -ne 0) { throw "build failed" }

if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest | Out-Null }
Copy-Item (Join-Path $root "bin\zones.exe") $exe -Force
Write-Host "Installed -> $exe"

$lnk = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Zones.lnk"
$ws = New-Object -ComObject WScript.Shell
$sc = $ws.CreateShortcut($lnk)
$sc.TargetPath = $exe
$sc.WorkingDirectory = $dest
$sc.IconLocation = "$exe,0"
$sc.Description = "Snap windows into your own zones"
$sc.Save()
Write-Host "Shortcut  -> $lnk"

# An older install may have registered a path that no longer exists.
$run = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
if ((Get-ItemProperty $run -ErrorAction SilentlyContinue).Zones) {
    Set-ItemProperty $run -Name Zones -Value "`"$exe`""
    Write-Host "Startup   -> $exe"
}

Start-Process $exe
Write-Host "Started."
