param([switch]$RegenerateIcon)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src  = Join-Path $root "src"
$bin  = Join-Path $root "bin"
$csc  = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) { throw "csc.exe not found at $csc" }
if (-not (Test-Path $bin)) { New-Item -ItemType Directory -Path $bin | Out-Null }

$out = Join-Path $bin "zones.exe"
$files = Get-ChildItem $src -Filter *.cs -Recurse | ForEach-Object { $_.FullName }

# Generate the icon if it is missing, so a clean checkout still builds branded.
# The artwork is src\Ui\Logo.cs, the same drawing as the settings window's title
# bar; IconGen renders it and refreshes the README logo in ..\docs too.
# -RegenerateIcon forces a rebuild after the mark changes.
$ico = Join-Path $root "assets\zones.ico"
if ($RegenerateIcon -or -not (Test-Path $ico)) {
  Write-Host "Generating icon..."
  $obj = Join-Path $root "obj"
  $assets = Join-Path $root "assets"
  $docs = Join-Path $root "..\docs"
  foreach ($dir in @($obj, $assets, $docs)) {
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
  }
  $gen = Join-Path $obj "IconGen.exe"

  $genArgs = @(
    "/nologo",
    "/target:exe",
    "/optimize+",
    "/out:$gen",
    "/reference:System.Drawing.dll",
    (Join-Path $root "tools\IconGen.cs"),
    (Join-Path $src "Ui\Logo.cs")
  )
  & $csc $genArgs
  if ($LASTEXITCODE -ne 0) { throw "ICON FAILED (csc exit $LASTEXITCODE)" }

  & $gen $ico (Join-Path $docs "logo.png")
  if ($LASTEXITCODE -ne 0) { throw "ICON FAILED (IconGen exit $LASTEXITCODE)" }
}

$args = @(
  "/nologo",
  "/target:winexe",
  "/platform:anycpu",
  "/optimize+",
  "/win32icon:$ico",
  "/out:$out",
  "/reference:System.dll",
  "/reference:System.Core.dll",
  "/reference:System.Drawing.dll",
  "/reference:System.Windows.Forms.dll"
) + $files

Write-Host "Compiling $($files.Count) files..."
& $csc $args
if ($LASTEXITCODE -ne 0) { throw "BUILD FAILED (csc exit $LASTEXITCODE)" }

if (-not (Test-Path $out)) { throw "BUILD FAILED: $out was not produced" }
$size = (Get-Item $out).Length
Write-Host "BUILD OK -> $out ($size bytes)"
