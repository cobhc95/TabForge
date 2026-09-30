<#
Builds the two GitHub release downloads into dist\:
  TabForge-<version>-win-x64-portable.zip   extract anywhere and run TabForge.exe
  TabForge-<version>-setup.exe              Inno Setup installer (optional file associations)
The version comes from Directory.Build.props <Version>, so the app, the engine, the zip and the installer agree.

Release gate: this script publishes to build\TabForge-release, runs the headless self-test ON THAT EXACT
FOLDER (with --require gp-fixtures,synthetic-fixtures,source-hygiene,installer-parity) and refuses to package unless it exits 0 and its log says
"TabForge self-test: N passed, 0 failed". The tested folder is what gets zipped and installed; the
executable hash is re-checked after the test so nothing can be swapped in between.
It also verifies the shipped tfvst3.dll against the SHA-256 recorded in native\BUILD_PROVENANCE.md and refuses to
package on a mismatch or a missing record, unless -AllowUnprovenancedNative is passed (then it only warns loudly).
The PDBs are not shipped; they are archived in dist\symbols\<version>\ so crash logs can be symbolicated later.
The portable zip is written with forward-slash entry names and one fixed, neutral timestamp (2026-01-01 00:00) for
every entry, so it carries no build machine time or time zone.
#>
[CmdletBinding()]
param(
    [string]$InnoCompiler = '',
    [switch]$AllowUnprovenancedNative
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'src\TabForge\TabForge.csproj'
$buildProps = Join-Path $root 'Directory.Build.props'
$version = ([xml][IO.File]::ReadAllText($buildProps)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'No <Version> in Directory.Build.props.' }
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

Write-Output "TabForge $version"

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

# LGPL-2.1 (SoundTouch.Net): the DLL must be a loose, replaceable file beside the exe, and the licence texts must ship.
if (-not (Test-Path -LiteralPath (Join-Path $publish 'SoundTouch.Net.dll'))) {
    throw 'SoundTouch.Net.dll is not beside TabForge.exe in the publish folder (LGPL: it must stay a loose, replaceable file); refusing to package.'
}
foreach ($required in @('LGPL-2.1_SoundTouch.Net.txt', 'OFL-1.1_Bravura.txt', 'MIT_NAudio.txt', 'MIT_MeltySynth_and_notices.txt', 'THIRD-PARTY-NOTICES_DotNet_runtime.txt')) {
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

# ---- release gate: self-test the exact folder that will be packaged --------------------------------
$exe = Join-Path $publish 'TabForge.exe'
$exeHashBefore = Get-Sha256 $exe
$selfTestLog = Join-Path ([IO.Path]::GetTempPath()) "tabforge-release-selftest-$version.log"
if (Test-Path -LiteralPath $selfTestLog) { Remove-Item -LiteralPath $selfTestLog -Force }
Write-Output "Self-testing $exe ..."
$proc = Start-Process -FilePath $exe -ArgumentList @('--selftest', "`"$selfTestLog`"", '--require', 'gp-fixtures,synthetic-fixtures,source-hygiene,installer-parity') -WorkingDirectory $root -Wait -PassThru
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
Copy-Item -LiteralPath $selfTestLog -Destination (Join-Path $dist "TabForge-$version-selftest.log") -Force
Write-Output "Self-test passed for the exact build being packaged (TabForge.exe SHA-256 $exeHashBefore)."

# ---- package the tested folder ---------------------------------------------------------------------
# Portable zip: one top-level folder so extracting never scatters files.
$zip = Join-Path $dist "TabForge-$version-win-x64-portable.zip"
if (Test-Path -LiteralPath $zip) { [IO.File]::Delete($zip) }
$staging = Join-Path ([IO.Path]::GetTempPath()) "TabForge-$version-portable"
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
& $InnoCompiler "/DAppVersion=$version" "/DSourceDir=$publish" /Q (Join-Path $root 'installer\TabForge.iss')
if ($LASTEXITCODE) { throw 'Inno Setup failed' }
$setup = Join-Path $dist "TabForge-$version-setup.exe"
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
$sums = Join-Path $dist "TabForge-$version-SHA256.txt"
Get-FileHash -Algorithm SHA256 $zip, $setup |
    ForEach-Object { "{0}  {1}" -f $_.Hash.ToLowerInvariant(), (Split-Path $_.Path -Leaf) } |
    Set-Content -LiteralPath $sums -Encoding ascii
Write-Output "Checksums: $sums"
