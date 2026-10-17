# Builds Zones for Windows and macOS and publishes both as one release on
# rui-branco/zones, the releases the apps update from.
#
#   .\release.ps1            build, check and publish VERSION as v<VERSION>
#   .\release.ps1 -DryRun    build and check only: everything ends up in dist\
#
# The Windows build runs here. The Mac build runs on -MacHost over ssh: the
# repo's VERSION and macos\ are copied to a fresh ~/zones-build/release there,
# packaged by macos/scripts/package-release.sh, and the zip copied back.
#
# A release is only ever made from a clean tree at origin/main, with release
# notes written (docs\release-notes\<VERSION>.md), for a version not yet
# released. Every asset is checked against VERSION before anything is
# uploaded, and the upload is read back afterwards.

param(
  [string]$NotesFile,
  [switch]$DryRun,
  [string]$MacHost = "mac",
  # Dry runs only: build from a tree with uncommitted changes, or not at
  # origin/main. Never for a real release.
  [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"

$Repo = "rui-branco/zones"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dist = Join-Path $root "dist"
$bash = "C:\Program Files\Git\bin\bash.exe"

# On the Mac, relative to the home directory ssh starts in.
$remoteDir = "zones-build/release"

function Step([string]$text) {
  Write-Host ""
  Write-Host "==> $text" -ForegroundColor Cyan
}

function Info([string]$text) {
  Write-Host "    $text"
}

function Check([string]$what) {
  if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" }
}

# A native command whose failure is an answer rather than a fault, with its
# stderr kept as part of the output. Under "Stop", PowerShell 5.1 turns a
# native program's redirected stderr into a terminating error.
function Capture([scriptblock]$command) {
  $prev = $ErrorActionPreference
  $ErrorActionPreference = "Continue"
  try {
    $out = & $command 2>&1 | ForEach-Object { "$_" }
  } finally {
    $ErrorActionPreference = $prev
  }
  return ($out -join "`n")
}

# Runs native commands judged by their exit codes (see Check), not by whether
# they wrote to stderr. rcodesign and gh print progress there; under "Stop",
# PowerShell 5.1 turns that into a terminating error whenever the script's
# output is redirected. Its output is the block's, so assign the call, not
# inside the block: an assignment in there is lost with the function's scope.
function Native([scriptblock]$block) {
  $prev = $ErrorActionPreference
  $ErrorActionPreference = "Continue"
  try {
    . $block
  } finally {
    $ErrorActionPreference = $prev
  }
}

if ($AllowDirty -and -not $DryRun) { throw "-AllowDirty is only for -DryRun: a release is made from a clean tree at origin/main" }

Push-Location $root
try {
  Write-Host "Zones release" -ForegroundColor White
  if ($DryRun) { Info "dry run: build and check only, nothing is published" }

  # ---------- the source ----------

  if ($AllowDirty) {
    Step "Source"
    Write-Host "    WARNING: -AllowDirty: not checking for a clean tree at origin/main. Dry runs only." -ForegroundColor Yellow
  } else {
    Step "Checking the working tree is clean"
    $status = git status --porcelain
    Check "git status"
    if ($status) {
      $status | ForEach-Object { Info $_ }
      throw "The working tree has uncommitted changes. Commit or stash them first."
    }
    Info "clean"

    Step "Checking HEAD is origin/main"
    git fetch origin
    Check "git fetch origin"
    $head = (git rev-parse HEAD).Trim()
    Check "git rev-parse HEAD"
    $main = (git rev-parse origin/main).Trim()
    Check "git rev-parse origin/main"
    if ($head -ne $main) {
      throw "HEAD ($($head.Substring(0, 7))) is not origin/main ($($main.Substring(0, 7))). Release from main, pushed."
    }
    Info "HEAD = origin/main = $($head.Substring(0, 7))"
  }

  # ---------- version and notes ----------

  Step "Reading VERSION"
  $versionFile = Join-Path $root "VERSION"
  if (-not (Test-Path $versionFile -PathType Leaf)) { throw "VERSION not found at $versionFile" }
  $version = (Get-Content $versionFile -Raw).Trim()
  if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "VERSION is '$version': expected major.minor.patch, like 1.0.0" }
  $tag = "v$version"
  Info "$version (tag $tag on $Repo)"

  Step "Checking the release notes"
  if (-not $NotesFile) { $NotesFile = Join-Path $root "docs\release-notes\$version.md" }
  if (-not (Test-Path $NotesFile -PathType Leaf)) {
    throw "No release notes at $NotesFile. Write them first, or pass -NotesFile."
  }
  $NotesFile = (Resolve-Path $NotesFile).Path
  Info $NotesFile

  Step "Checking $tag is not released yet"
  $view = Capture { gh release view $tag -R $Repo --json tagName }
  if ($LASTEXITCODE -eq 0) { throw "$tag is already released on $Repo. Bump VERSION." }
  if ($view -notmatch "release not found") { throw "Could not ask GitHub about $tag on ${Repo}:`n$view" }
  Info "not released"

  # ---------- builds ----------

  Step "Preparing dist\"
  if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
  New-Item -ItemType Directory -Path $dist | Out-Null
  $winOut = Join-Path $dist "win"
  New-Item -ItemType Directory -Path $winOut | Out-Null
  Info $dist

  Step "Building for Windows"
  & (Join-Path $root "windows\build.ps1") -OutDir $winOut
  $built = Join-Path $winOut "zones.exe"
  if (-not (Test-Path $built -PathType Leaf)) { throw "The Windows build did not produce $built" }
  $exe = Join-Path $dist "zones.exe"
  Copy-Item $built $exe -Force
  $fileVersion = (Get-Item $exe).VersionInfo.FileVersion
  if (-not ($fileVersion -eq $version -or "$fileVersion".StartsWith("$version."))) {
    throw "zones.exe says it is version '$fileVersion', not $version"
  }
  Info "zones.exe $fileVersion ($((Get-Item $exe).Length) bytes)"

  Step "Building for macOS on $MacHost"
  Info "copying VERSION and macos\ to ~/$remoteDir"
  Native { ssh $MacHost "rm -rf $remoteDir && mkdir -p $remoteDir" }
  Check "ssh $MacHost (preparing ~/$remoteDir)"
  # Through Git Bash: PowerShell does not pipe binary output between programs intact.
  Native { & $bash -c "tar czf - --exclude=.build VERSION macos | ssh $MacHost 'cd $remoteDir && tar xzf -'" }
  Check "Copying the source to $MacHost"

  Info "packaging"
  Native { ssh $MacHost "cd $remoteDir && bash macos/scripts/package-release.sh" }
  Check "package-release.sh on $MacHost"

  # Read from the zip itself, so what is checked is what gets uploaded.
  $macVersion = Native { ssh $MacHost "cd $remoteDir && rm -rf check && unzip -q macos/dist/Zones-mac.zip Zones.app/Contents/Info.plist -d check && /usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' check/Zones.app/Contents/Info.plist" }
  Check "Reading the version inside Zones-mac.zip"
  $macVersion = "$macVersion".Trim()
  if ($macVersion -ne $version) { throw "Zones.app in Zones-mac.zip says it is version '$macVersion', not $version" }

  # A relative destination: scp would read C: in an absolute one as a host.
  Native { scp "${MacHost}:$remoteDir/macos/dist/Zones-mac.zip" "dist/Zones-mac.zip" }
  Check "scp Zones-mac.zip from $MacHost"
  $zip = Join-Path $dist "Zones-mac.zip"
  if (-not (Test-Path $zip -PathType Leaf)) { throw "Zones-mac.zip was not copied to $zip" }
  Info "Zones-mac.zip $macVersion ($((Get-Item $zip).Length) bytes)"

  # ---------- checksums ----------

  Step "Writing SHA256SUMS.txt"
  $sums = Join-Path $dist "SHA256SUMS.txt"
  $lines = @()
  foreach ($name in @("zones.exe", "Zones-mac.zip")) {
    $hash = (Get-FileHash (Join-Path $dist $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines += "$hash  $name"
  }
  # LF line endings and no BOM: the updaters on both systems read it.
  [System.IO.File]::WriteAllText($sums, ($lines -join "`n") + "`n", (New-Object System.Text.UTF8Encoding $false))
  $lines | ForEach-Object { Info $_ }

  if ($DryRun) {
    Step "Dry run done"
    Info "$exe"
    Info "$zip"
    Info "$sums"
    Info "Nothing was published."
    return
  }

  # ---------- publish ----------

  # The tag on the commit that was built, not on whatever main is by now.
  Step "Publishing $tag on $Repo"
  Native { gh release create $tag -R $Repo --target $head --title "Zones $version" --notes-file $NotesFile $exe $zip $sums }
  Check "gh release create"

  Step "Checking the release"
  $json = Native { gh release view $tag -R $Repo --json assets }
  Check "gh release view"
  $assets = (($json -join "`n") | ConvertFrom-Json).assets
  if (@($assets).Count -ne 3) { throw "$tag has $(@($assets).Count) assets, not 3" }
  foreach ($file in @($exe, $zip, $sums)) {
    $name = Split-Path $file -Leaf
    $asset = $assets | Where-Object { $_.name -eq $name }
    if (-not $asset) { throw "$tag is missing $name" }
    $size = (Get-Item $file).Length
    if ($asset.size -ne $size) { throw "$name is $($asset.size) bytes on GitHub, $size here" }
    Info "$name ($size bytes)"
  }

  $back = Join-Path $dist "verify"
  New-Item -ItemType Directory -Path $back | Out-Null
  Native { gh release download $tag -R $Repo -p SHA256SUMS.txt -D $back }
  Check "gh release download SHA256SUMS.txt"
  $downloaded = Join-Path $back "SHA256SUMS.txt"
  if ((Get-FileHash $downloaded).Hash -ne (Get-FileHash $sums).Hash) {
    throw "SHA256SUMS.txt downloaded from $tag does not match dist\SHA256SUMS.txt"
  }
  Info "SHA256SUMS.txt downloaded back matches"

  Step "Released Zones $version"
  Info "https://github.com/$Repo/releases/tag/$tag"
} finally {
  Pop-Location
}
