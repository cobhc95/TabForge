<#
Builds the two GitHub release downloads into dist\:
  TabForge-<display>-win-x64-portable.zip   extract anywhere and run TabForge.exe
  TabForge-<display>-setup.exe              Inno Setup installer (optional file associations)
The version comes from Directory.Build.props <Version>, so the app, the engine, the zip and the installer agree.
File names and the installer use the display version (0.5.0 shows as "0.5", like the app); the numeric file version stays 0.5.0.0.

Release gate: this script publishes to build\TabForge-release, runs the headless basic self-test set ON THAT EXACT
FOLDER and refuses to package unless it exits 0 and its log says
"TabForge self-test: N passed, 0 failed". The full suite (tests\full-suite) never runs implicitly: pass -FullSuite to
also publish a separate full-suite build (build\TabForge-fullsuite, never packaged) and run it with --require ci,document-context first.
The tested folder is what gets zipped and installed; the
executable hash is re-checked after the test so nothing can be swapped in between.
It also verifies the shipped tfvst3.dll against the SHA-256 recorded in native\BUILD_PROVENANCE.md and refuses to
package on a mismatch or a missing record, unless -AllowUnprovenancedNative is passed (then it only warns loudly).
The PDBs are not shipped; they are archived in dist\symbols\<version>\ so crash logs can be symbolicated later.
The portable zip is written with forward-slash entry names and one fixed, neutral timestamp (2026-01-01 00:00) for
every entry, so it carries no build machine time or time zone.

Reproducible builds: unless it already runs on a clean checkout (CI), the script builds from a temporary git worktree
of the committed HEAD, never from the working folder, so line endings, stray files and uncommitted edits cannot leak
into the release, and a local build equals a CI build of the same commit. Build settings that affect bytes live in
global.json (exact SDK), Directory.Build.props and tools\Publish.ps1. The one native bridge is the committed
src\TabForge.AudioEngine\native\tfvst3.dll (its hash is verified against native\BUILD_PROVENANCE.md).
tools\Compare-Release.ps1 compares two results. See docs\REPRODUCIBLE_BUILDS.md.
#>
[CmdletBinding()]
param(
    [string]$InnoCompiler = '',
    [switch]$AllowUnprovenancedNative,
    # Also builds and runs the full self-test suite (tests\full-suite) before packaging; without it the gate is the basic set only.
    [switch]$FullSuite,
    # Internal: set by the worktree wrapper below (and implied on CI) - the tree this script lives in is already a
    # clean checkout of the commit being released.
    [switch]$InCleanCheckout
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

# ---- build from a clean worktree of the committed HEAD ---------------------------------------------
if (-not $InCleanCheckout -and $env:CI -ne 'true') {
    $dirty = @(& git -C $root status --porcelain)
    if ($dirty.Count -gt 0) {
        Write-Warning ("The working folder has {0} uncommitted change(s). The release is built from the committed HEAD only; they are NOT in it (commit first if they should be)." -f $dirty.Count)
    }
    $head = (& git -C $root rev-parse HEAD).Trim()
    if ($LASTEXITCODE -or $head -notmatch '^[0-9a-f]{40}$') { throw 'git rev-parse HEAD failed; the release is built from a git commit.' }
    $wt = Join-Path ([IO.Path]::GetTempPath()) ('tfrel-' + $head.Substring(0, 8) + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 6))
    function Remove-Worktree {
        if (Test-Path -LiteralPath $wt) {
            # git marks some files read-only (pack files of the SoundTouch clone); clear that so the folder can go.
            Get-ChildItem -LiteralPath $wt -Recurse -Force -File -ErrorAction SilentlyContinue | ForEach-Object { $_.Attributes = 'Normal' }
            [IO.Directory]::Delete($wt, $true)
        }
        & git -C $root worktree prune
    }
    Remove-Worktree
    & git -C $root worktree add --detach --quiet $wt $head
    if ($LASTEXITCODE) { throw "git worktree add failed for $wt" }
    Write-Output "Building commit $head from a clean worktree: $wt"
    try {
        $innerArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $wt 'tools\Package-Release.ps1'), '-InCleanCheckout')
        if ($InnoCompiler) { $innerArgs += @('-InnoCompiler', $InnoCompiler) }
        if ($AllowUnprovenancedNative) { $innerArgs += '-AllowUnprovenancedNative' }
        if ($FullSuite) { $innerArgs += '-FullSuite' }
        & (Get-Process -Id $PID).Path @innerArgs
        if ($LASTEXITCODE) { throw "The release build in the clean worktree failed (exit $LASTEXITCODE)." }
        # Bring the results (zip, installer, checksums, self-test log, symbols) home.
        $distHome = Join-Path $root 'dist'
        [IO.Directory]::CreateDirectory($distHome) | Out-Null
        foreach ($item in Get-ChildItem -LiteralPath (Join-Path $wt 'dist')) {
            Copy-Item -LiteralPath $item.FullName -Destination $distHome -Recurse -Force
        }
        Write-Output "Release files are in $distHome (built from commit $head)."
    } finally {
        Remove-Worktree
    }
    return
}
# From here on this is a clean checkout of one commit (a temporary worktree, or the CI checkout).
$porcelain = @(& git -C $root status --porcelain)
if ($porcelain.Count -gt 0) { throw ('The checkout is not clean, so the build would not match the committed commit:' + [Environment]::NewLine + ($porcelain -join [Environment]::NewLine)) }
# Same as CI: the sample songs in Tabs\ are not part of a clean checkout, so the gate must not depend on them.
$env:TABFORGE_NO_LOCAL_SONGS = '1'
$project = Join-Path $root 'src\TabForge\TabForge.csproj'
$buildProps = Join-Path $root 'Directory.Build.props'
$version = ([xml][IO.File]::ReadAllText($buildProps)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }
# Display version, same rule as AppInfo.DisplayVersion: a final release with patch 0 is shown as major.minor ("0.5").
$display = $version
if ($version -match '^(\d+)\.(\d+)\.0$') { $display = "$($Matches[1]).$($Matches[2])" }
$numericVersion = ($version -split '-')[0] + '.0'   # 0.5.0 -> 0.5.0.0 (file version resource)
$publish = Join-Path $root 'build\TabForge-release'
$dist = Join-Path $root 'dist'

if (-not $InnoCompiler) {
    # First Inno Setup compiler found in the usual per-user and machine-wide locations.
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'))
    $InnoCompiler = @($candidates | Where-Object { Test-Path -LiteralPath $_ })[0]
    if (-not $InnoCompiler) { $InnoCompiler = $candidates[0] }
}

function Clear-Folder([string]$Path) {
    if (Test-Path -LiteralPath $Path) { [IO.Directory]::Delete($Path, $true) }
}

function Get-Sha256([string]$Path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }

function Write-LoudWarning([string]$Message) {
    Write-Warning ('*' * 70)
    Write-Warning $Message
    Write-Warning ('*' * 70)
}

Write-Output "TabForge $display (version $version)"

# ---- native bridge provenance ---------------------------------------------------------------------
$shippedDll = Join-Path $root 'src\TabForge.AudioEngine\native\tfvst3.dll'
$provenanceFile = Join-Path $root 'native\BUILD_PROVENANCE.md'
if (-not (Test-Path -LiteralPath $shippedDll)) {
    throw "tfvst3.dll is missing at $shippedDll (run native\build-tfvst3.ps1)."
}
$shippedHash = Get-Sha256 $shippedDll
# A native DLL that is not tied to a recorded build is fatal unless the caller explicitly accepts it.
function Stop-Unprovenanced([string]$Message) {
    if ($AllowUnprovenancedNative) { Write-LoudWarning "$Message (continuing: -AllowUnprovenancedNative)"; return }
    throw "$Message Refusing to package; pass -AllowUnprovenancedNative to override."
}
if (-not (Test-Path -LiteralPath $provenanceFile)) {
    Stop-Unprovenanced "No native\BUILD_PROVENANCE.md: the shipped tfvst3.dll ($shippedHash) is NOT tied to a recorded SDK commit/compiler. Run native\build-tfvst3.ps1."
} else {
    $recorded = $null
    foreach ($line in [IO.File]::ReadAllLines($provenanceFile)) {
        if ($line -match '^tfvst3\.dll SHA-256:\s*`?([0-9a-fA-F]{64})`?\s*$') { $recorded = $Matches[1].ToLowerInvariant() }
    }
    if (-not $recorded) {
        Stop-Unprovenanced 'native\BUILD_PROVENANCE.md has no tfvst3.dll SHA-256 line. Re-run native\build-tfvst3.ps1.'
    } elseif ($recorded -ne $shippedHash) {
        Stop-Unprovenanced "tfvst3.dll MISMATCH: shipped $shippedHash, recorded $recorded. The DLL was not produced by the recorded build. Re-run native\build-tfvst3.ps1 or investigate before releasing."
    } else {
        Write-Output "tfvst3.dll matches native\BUILD_PROVENANCE.md ($shippedHash)"
    }
}

# ---- publish ---------------------------------------------------------------------------------------
Clear-Folder $publish
[IO.Directory]::CreateDirectory($dist) | Out-Null

dotnet restore $project -r win-x64 --locked-mode
if ($LASTEXITCODE) { throw 'restore failed' }
& (Join-Path $PSScriptRoot 'Publish.ps1') -Output $publish -ExtraArgs '-p:DebugType=portable'
# Symbols: archived per release (never shipped) so crash logs can be symbolicated; moved out before the self-test
# so the tested folder is exactly the packaged one.
$symbols = Join-Path $dist "symbols\$version"
Clear-Folder $symbols
[IO.Directory]::CreateDirectory($symbols) | Out-Null
$pdbs = @(Get-ChildItem -LiteralPath $publish -Filter '*.pdb' -File -Recurse)
foreach ($pdb in $pdbs) { Move-Item -LiteralPath $pdb.FullName -Destination (Join-Path $symbols $pdb.Name) -Force }
if ($pdbs.Count -eq 0) { Write-LoudWarning "No PDBs were produced; dist\symbols\$version is empty." }
else { Write-Output ("Symbols: {0} PDB(s) archived in {1}" -f $pdbs.Count, $symbols) }
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $publish 'LICENSE.txt')
Copy-Item (Join-Path $root 'THIRD_PARTY.md') $publish
# MPL-2.0 s3.2 (TabForge.AlphaTab is a modified alphaTab): the patch, i.e. the source of the modification, ships beside the licence text.
$alphaTabPatches = @(Get-ChildItem -LiteralPath (Join-Path $root 'vendor\alphatab') -Filter '*.patch' -File)
if ($alphaTabPatches.Count -eq 0) { throw 'vendor\alphatab\*.patch is missing (MPL-2.0 source of TabForge.AlphaTab); refusing to package.' }
foreach ($patch in $alphaTabPatches) { Copy-Item -LiteralPath $patch.FullName -Destination (Join-Path $publish "licenses\alphaTab-$($patch.Name)") }

# LGPL-2.1 (SoundTouch.Net): the DLL must be a loose, replaceable file beside the exe, and the licence texts must ship.
if (-not (Test-Path -LiteralPath (Join-Path $publish 'SoundTouch.Net.dll'))) {
    throw 'SoundTouch.Net.dll is not beside TabForge.exe in the publish folder (LGPL: it must stay a loose, replaceable file); refusing to package.'
}
foreach ($required in @('LGPL-2.1_SoundTouch.Net.txt', 'MPL-2.0_alphaTab.txt', 'OFL-1.1_Bravura.txt', 'MIT_NAudio.txt', 'MIT_MeltySynth_and_notices.txt', 'THIRD-PARTY-NOTICES_DotNet_runtime.txt', 'MIT_PDFsharp_MigraDoc.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish "licenses\$required"))) { throw "licenses\$required is missing from the publish folder; refusing to package." }
}
# Machine-neutral binaries: no local user or build paths may be embedded in anything that ships.
foreach ($bin in Get-ChildItem -LiteralPath $publish -Recurse -File -Include *.exe, *.dll) {
    $text = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($bin.FullName))
    if ($text -match ':\\Users\\' -and $text -match [regex]::Escape($env:USERNAME)) { throw "$($bin.Name) embeds a local user path; refusing to package." }
}

# The demo song ships as a loose file (Samples\), not inside the single-file bundle.
if (-not (Get-ChildItem -LiteralPath (Join-Path $publish 'Samples') -Filter '*.gp' -ErrorAction SilentlyContinue)) {
    throw 'Samples\*.gp (the demo song) is missing from the publish folder; refusing to package.'
}

# Refresh the SBOM and ship it next to the licence texts.
& (Join-Path $PSScriptRoot 'Write-Sbom.ps1')
$sbom = Join-Path $root 'docs\SBOM.md'
if (Test-Path -LiteralPath $sbom) { Copy-Item $sbom (Join-Path $publish 'SBOM.md') }

# ---- optional: the full suite, on its own build that is never packaged -----------------------------
if ($FullSuite) {
    $fullFolder = Join-Path $root 'build\TabForge-fullsuite'
    Clear-Folder $fullFolder
    & (Join-Path $PSScriptRoot 'Publish.ps1') -Output $fullFolder -ExtraArgs '-p:TabForgeFullSuite=true'
    $fullLog = Join-Path ([IO.Path]::GetTempPath()) "tabforge-release-fullsuite-$version.log"
    if (Test-Path -LiteralPath $fullLog) { Remove-Item -LiteralPath $fullLog -Force }
    Write-Output "Full-suite self-test of $fullFolder ..."
    $fullProc = Start-Process -FilePath (Join-Path $fullFolder 'TabForge.exe') -ArgumentList @('--selftest', "`"$fullLog`"", '--require', 'ci,document-context') -WorkingDirectory $root -Wait -PassThru
    if (-not (Test-Path -LiteralPath $fullLog)) { throw "Full-suite self-test wrote no log ($fullLog); refusing to package." }
    if ($fullProc.ExitCode -ne 0 -or -not (Select-String -LiteralPath $fullLog -Pattern '^TabForge self-test: \d+ passed, 0 failed' -Quiet)) {
        Select-String -LiteralPath $fullLog -Pattern '^\s+FAIL' | ForEach-Object { Write-Output $_.Line }
        throw "Full-suite self-test failed (log: $fullLog); refusing to package."
    }
    Copy-Item -LiteralPath $fullLog -Destination (Join-Path $dist "TabForge-$display-fullsuite.log") -Force
    Remove-Item -LiteralPath $fullFolder -Recurse -Force
}

# ---- release gate: the curated release area, on its own test build that is never packaged ----------
# Basic set plus saving/atomic writes/recovery, import and malformed-input containment, playback/edit interaction, document context and
# security tests (SelfTestRelease.cs). It cannot be skipped; any failure stops the release.
$gateFolder = Join-Path $root 'build\TabForge-releasegate'
Clear-Folder $gateFolder
& (Join-Path $PSScriptRoot 'Publish.ps1') -Output $gateFolder -ExtraArgs '-p:TabForgeFullSuite=true'
$gateLog = Join-Path ([IO.Path]::GetTempPath()) "tabforge-release-gate-$version.log"
if (Test-Path -LiteralPath $gateLog) { Remove-Item -LiteralPath $gateLog -Force }
Write-Output "Release-gate self-test (--areas release) of $gateFolder ..."
$gateProc = Start-Process -FilePath (Join-Path $gateFolder 'TabForge.exe') -ArgumentList @('--selftest', "`"$gateLog`"", '--areas', 'release') -WorkingDirectory $root -Wait -PassThru
if (-not (Test-Path -LiteralPath $gateLog)) { throw "Release-gate self-test wrote no log ($gateLog); refusing to package." }
if ($gateProc.ExitCode -ne 0 -or -not (Select-String -LiteralPath $gateLog -Pattern '^TabForge self-test: \d+ passed, 0 failed' -Quiet)) {
    Select-String -LiteralPath $gateLog -Pattern '^\s+FAIL' | ForEach-Object { Write-Output $_.Line }
    throw "Release-gate self-test failed (log: $gateLog); refusing to package."
}
$gateSummary = Select-String -LiteralPath $gateLog -Pattern '^TabForge self-test: (\d+) passed' | Select-Object -Last 1
Write-Output $gateSummary.Line
if (-not $gateSummary -or [int]$gateSummary.Matches[0].Groups[1].Value -lt 1500) { throw "Release gate ran too few checks ($($gateSummary.Line)); the curated release tests did not run. Refusing to package." }
if (-not (Select-String -LiteralPath $gateLog -Pattern 'release gate: all \d+ curated release tests ran' -Quiet)) { throw "Release gate log lacks the curated-test confirmation ($gateLog); refusing to package." }
Copy-Item -LiteralPath $gateLog -Destination (Join-Path $dist "TabForge-$display-releasegate.log") -Force
Remove-Item -LiteralPath $gateFolder -Recurse -Force

# ---- release gate: self-test the exact folder that will be packaged (the basic set) ----------------
$exe = Join-Path $publish 'TabForge.exe'
$exeHashBefore = Get-Sha256 $exe
$selfTestLog = Join-Path ([IO.Path]::GetTempPath()) "tabforge-release-selftest-$version.log"
if (Test-Path -LiteralPath $selfTestLog) { Remove-Item -LiteralPath $selfTestLog -Force }
Write-Output "Self-testing $exe ..."
$proc = Start-Process -FilePath $exe -ArgumentList @('--selftest', "`"$selfTestLog`"") -WorkingDirectory $root -Wait -PassThru
if (-not (Test-Path -LiteralPath $selfTestLog)) { throw "Self-test wrote no log ($selfTestLog); refusing to package." }
$summary = Select-String -LiteralPath $selfTestLog -Pattern '^TabForge self-test: \d+ passed, \d+ failed' | Select-Object -Last 1
if ($summary) { Write-Output $summary.Line }
if ($proc.ExitCode -ne 0) {
    Select-String -LiteralPath $selfTestLog -Pattern '^\s+FAIL' | ForEach-Object { Write-Output $_.Line }
    throw "Self-test exited with $($proc.ExitCode) (log: $selfTestLog); refusing to package."
}
if (-not (Select-String -LiteralPath $selfTestLog -Pattern '^TabForge self-test: \d+ passed, 0 failed' -Quiet)) {
    throw "Self-test log does not report '0 failed' (log: $selfTestLog); refusing to package."
}
if ((Get-Sha256 $exe) -ne $exeHashBefore) { throw 'TabForge.exe changed during the self-test; refusing to package.' }
Copy-Item -LiteralPath $selfTestLog -Destination (Join-Path $dist "TabForge-$display-selftest.log") -Force
Write-Output "Self-test passed for the exact build being packaged (TabForge.exe SHA-256 $exeHashBefore)."

# ---- package the tested folder ---------------------------------------------------------------------
# Portable zip: one top-level folder so extracting never scatters files.
$zip = Join-Path $dist "TabForge-$display-win-x64-portable.zip"
if (Test-Path -LiteralPath $zip) { [IO.File]::Delete($zip) }
$staging = Join-Path ([IO.Path]::GetTempPath()) "TabForge-$display-portable"
Clear-Folder $staging
Copy-Item -LiteralPath $publish -Destination (Join-Path $staging 'TabForge') -Recurse
if ((Get-Sha256 (Join-Path $staging 'TabForge\TabForge.exe')) -ne $exeHashBefore) { throw 'Staged TabForge.exe differs from the tested one.' }
# Written with System.IO.Compression so entry names always use forward slashes and every entry carries the same
# neutral timestamp (Compress-Archive uses backslashes on Windows PowerShell 5.1 and the local file times).
Add-Type -AssemblyName System.IO.Compression
$zipTime = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
$stagedRoot = (Join-Path $staging 'TabForge').TrimEnd('\') + '\'
$zipEntries = New-Object 'System.Collections.Generic.SortedDictionary[string,string]' ([StringComparer]::Ordinal)
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $staging 'TabForge') -Recurse -File) {
    $zipEntries['TabForge/' + $file.FullName.Substring($stagedRoot.Length).Replace('\', '/')] = $file.FullName
}
$zipStream = [IO.File]::Open($zip, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite)
try {
    $archive = [IO.Compression.ZipArchive]::new($zipStream, [IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        foreach ($name in $zipEntries.Keys) {
            $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $zipTime
            $source = [IO.File]::OpenRead($zipEntries[$name])
            try { $target = $entry.Open(); try { $source.CopyTo($target) } finally { $target.Dispose() } } finally { $source.Dispose() }
        }
    } finally { $archive.Dispose() }
} finally { $zipStream.Dispose() }
Clear-Folder $staging
Write-Output ("Portable: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))

# Installer (built from the same tested folder)
if (-not (Test-Path -LiteralPath $InnoCompiler)) { throw "Inno Setup compiler not found: $InnoCompiler" }
& $InnoCompiler "/DAppVersion=$display" "/DFileVersion=$numericVersion" "/DSourceDir=$publish" /Q (Join-Path $root 'installer\TabForge.iss')
if ($LASTEXITCODE) { throw 'Inno Setup failed' }
$setup = Join-Path $dist "TabForge-$display-setup.exe"
Write-Output ("Installer: {0} ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB))

# SoundTouch.Net source (LGPL-2.1 s6): a pinned clone of the official repository, attached to the release.
# Network or git problems only warn: THIRD_PARTY.md carries a written offer of the source that covers the gap.
$soundTouchTag = '2.3.2'
$soundTouchCommit = '98e5b8fd2f8efed0ddf7c8f66b435bfb231659dc'
$soundTouchZip = Join-Path $dist "SoundTouch.Net-$soundTouchTag-source.zip"
$soundTouchClone = Join-Path $root 'build\soundtouch.net-src'
$savedEap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    if (Test-Path -LiteralPath $soundTouchClone) {
        # git marks its pack files read-only; clear that so the old clone can be removed.
        Get-ChildItem -LiteralPath $soundTouchClone -Recurse -Force -File | ForEach-Object { $_.Attributes = 'Normal' }
        [IO.Directory]::Delete($soundTouchClone, $true)
    }
    [IO.Directory]::CreateDirectory((Split-Path $soundTouchClone)) | Out-Null
    & git clone --quiet https://github.com/owoudenberg/soundtouch.net.git $soundTouchClone 2>&1 | Out-Null
    if ($LASTEXITCODE) { throw 'git clone failed' }
    & git -C $soundTouchClone -c advice.detachedHead=false checkout --quiet $soundTouchCommit 2>&1 | Out-Null
    if ($LASTEXITCODE) { throw "commit $soundTouchCommit not found in the clone" }
    $head = (& git -C $soundTouchClone rev-parse HEAD).Trim()
    if ($head -ne $soundTouchCommit) { throw "HEAD is $head, expected $soundTouchCommit" }
    if (Test-Path -LiteralPath $soundTouchZip) { [IO.File]::Delete($soundTouchZip) }
    & git -C $soundTouchClone archive --format=zip --prefix="SoundTouch.Net-$soundTouchTag/" -o $soundTouchZip $soundTouchCommit 2>&1 | Out-Null
    if ($LASTEXITCODE -or -not (Test-Path -LiteralPath $soundTouchZip)) { throw 'git archive failed' }
    Write-Output ("SoundTouch.Net source: {0} ({1:N1} MB, commit {2})" -f $soundTouchZip, ((Get-Item $soundTouchZip).Length / 1MB), $soundTouchCommit)
} catch {
    Write-LoudWarning "SoundTouch.Net $soundTouchTag source zip NOT produced: $($_.Exception.Message). Attach it to the release by hand (tag $soundTouchTag, commit $soundTouchCommit); THIRD_PARTY.md carries the written offer."
} finally {
    $ErrorActionPreference = $savedEap
}

# Checksums for the release notes.
$sums = Join-Path $dist "TabForge-$display-SHA256.txt"
Get-FileHash -Algorithm SHA256 $zip, $setup |
    ForEach-Object { "{0}  {1}" -f $_.Hash.ToLowerInvariant(), (Split-Path $_.Path -Leaf) } |
    Set-Content -LiteralPath $sums -Encoding ascii
Write-Output "Checksums: $sums"
