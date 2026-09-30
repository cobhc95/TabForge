# Fetches the Steinberg VST3 SDK (MIT licence) into third_party\vst3sdk with only the submodules tfvst3 needs
# (base, pluginterfaces, public.sdk). vstgui, doc, tutorials and vst3_doc are skipped.
#
# The SDK is PINNED: a release tag plus the exact commit it must resolve to, and the exact commits of the
# three submodules that tag records. A moved/retagged upstream, or a different checkout, fails loudly.
# To update the SDK: change the values below, rebuild tfvst3 (native\build-tfvst3.ps1), and commit the new
# native\BUILD_PROVENANCE.md and docs\SBOM.md together with them.
[CmdletBinding()]
param([switch]$Force)
$ErrorActionPreference = 'Stop'

$SdkTag    = 'v3.8.1_build_84'
$SdkCommit = '3cdf9ca5d1f5b1b21e0a86832aa4abe55607bd96'
$SubmoduleCommits = [ordered]@{
    'base'          = 'fcf9da0bd27a16f7f03773a3a39822f28f5c8477'
    'pluginterfaces' = '4f547e8e102b47de4a8b8aaf343c73b700786372'
    'public.sdk'    = '586dc5e6c8012c3e4b01c79389375cbe96bdb1da'
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$target = Join-Path $repoRoot 'third_party\vst3sdk'
$git = 'C:\Program Files\Git\cmd\git.exe'
if (-not (Test-Path $git)) { $git = 'git' }

function Assert-Pinned {
    $head = (& $git -C $target rev-parse HEAD).Trim()
    if ($head -ne $SdkCommit) { throw "VST3 SDK at $target is at $head, expected pinned $SdkCommit ($SdkTag). Re-run with -Force to re-fetch." }
    foreach ($name in $SubmoduleCommits.Keys) {
        $sub = (& $git -C (Join-Path $target $name) rev-parse HEAD).Trim()
        if ($sub -ne $SubmoduleCommits[$name]) { throw "VST3 SDK submodule '$name' is at $sub, expected $($SubmoduleCommits[$name])." }
    }
}

if ((Test-Path (Join-Path $target 'public.sdk\source\vst\hosting\module.cpp')) -and -not $Force) {
    Assert-Pinned
    Write-Host "VST3 SDK $SdkTag ($SdkCommit) already present and verified at $target"
    return
}
if (Test-Path $target) { Remove-Item -Recurse -Force $target }
New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null

# git writes progress to stderr; in Windows PowerShell 5.1 that must not be treated as a terminating error.
$ErrorActionPreference = 'Continue'
& $git clone --depth 1 --branch $SdkTag https://github.com/steinbergmedia/vst3sdk $target 2>&1 | ForEach-Object { "$_" }
if ($LASTEXITCODE -ne 0) { throw "git clone of $SdkTag failed ($LASTEXITCODE)" }
& $git -C $target submodule update --init --depth 1 base pluginterfaces public.sdk 2>&1 | ForEach-Object { "$_" }
if ($LASTEXITCODE -ne 0) { throw "git submodule update failed ($LASTEXITCODE)" }
$ErrorActionPreference = 'Stop'
Assert-Pinned
Write-Host "VST3 SDK $SdkTag ($SdkCommit) fetched and verified in $target"
