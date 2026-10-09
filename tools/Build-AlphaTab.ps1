<#
.SYNOPSIS
    Builds TabForge.AlphaTab (alphaTab 1.8.4 plus four patches) from the upstream source, or verifies the committed package.
.DESCRIPTION
    alphaTab's .NET library is generated from its TypeScript by alphaTab's own transpiler. This script reproduces what
    upstream CI does (npm ci; generate-typescript; transpile; dotnet build) on a clean checkout of the pinned upstream
    commit with vendor/alphatab/*.patch applied, and packs the result as the NuGet package TabForge.AlphaTab
    (assembly TabForge.AlphaTab.dll, version 1.8.4.4, package version 1.8.4-tabforge.4), so it can never be mistaken for the
    upstream AlphaTab package.

      .\tools\Build-AlphaTab.ps1            build, then check the DLL equals the one inside the committed package
      .\tools\Build-AlphaTab.ps1 -Update    build, then replace vendor/alphatab/*.nupkg and vendor/alphatab/SHA256SUMS

    Official sources only: github.com/CoderLine/alphaTab, npmjs.org, nuget.org. Needs git, node (LTS), npm and the .NET 8 SDK.
    Exit codes: 0 ok, 1 build or verification failed.
#>
[CmdletBinding()]
param(
    [switch]$Update,
    # Scratch folder for the clone and the build output (created fresh on every run).
    [string]$Work = (Join-Path ([IO.Path]::GetTempPath()) 'tabforge-alphatab-build')
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$vendor = Join-Path $repo 'vendor\alphatab'
$upstream = 'https://github.com/CoderLine/alphaTab.git'
$tag = 'v1.8.4'
$commit = '022a45c8e42370f9e12e68949d11eada370da83d'
$packageFile = 'TabForge.AlphaTab.1.8.4-tabforge.4.nupkg'

function Invoke-Native([string]$What, [scriptblock]$Command) {
    # Windows PowerShell 5.1 turns native stderr text (npm notices, git advice) into terminating errors under 'Stop'.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Command } finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit code $LASTEXITCODE)" }
}

function Get-PackedDllHash([string]$Nupkg) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($Nupkg)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq 'lib/netstandard2.0/TabForge.AlphaTab.dll' } | Select-Object -First 1
        if (-not $entry) { throw "$Nupkg does not contain lib/netstandard2.0/TabForge.AlphaTab.dll" }
        $stream = $entry.Open()
        try { return ([BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($stream)) -replace '-', '').ToLowerInvariant() }
        finally { $stream.Dispose() }
    }
    finally { $zip.Dispose() }
}

$patches = @(Get-ChildItem -LiteralPath $vendor -Filter '*.patch' | Sort-Object Name)
if ($patches.Count -eq 0) { throw "No patch found in $vendor" }

if (Test-Path -LiteralPath $Work) { Remove-Item -LiteralPath $Work -Recurse -Force }
New-Item -ItemType Directory -Path $Work | Out-Null
# The same .NET SDK (compiler) as TabForge itself: global.json applies to everything below the work folder.
Copy-Item -LiteralPath (Join-Path $repo 'global.json') -Destination (Join-Path $Work 'global.json')
$src = Join-Path $Work 'alphaTab'

# 1. The pinned upstream source, with line endings untouched so every machine generates the same files.
Invoke-Native 'git clone' { git clone --quiet -c core.autocrlf=false -c advice.detachedHead=false --depth 1 --branch $tag $upstream $src 2>&1 | Out-Null }
$head = (git -C $src rev-parse HEAD).Trim()
if ($head -ne $commit) { throw "Upstream tag $tag is commit $head, expected $commit. Refusing to build from different source." }
# The build date alphaTab stamps into the library is pinned to the upstream commit's date (patch: SOURCE_DATE_EPOCH), not "now".
$env:SOURCE_DATE_EPOCH = (git -C $src log -1 --format=%ct).Trim()
foreach ($patch in $patches) {
    Invoke-Native "git apply --check $($patch.Name)" { git -C $src apply --check --whitespace=nowarn $patch.FullName }
    Invoke-Native "git apply $($patch.Name)" { git -C $src apply --whitespace=nowarn $patch.FullName }
}

# 2. Upstream's own build (workflow build_csharp): dependencies from the lock file, TypeScript -> C#, then the library project.
Push-Location $src
try {
    Invoke-Native 'npm ci' { npm ci --ignore-scripts --no-audit --no-fund 2>&1 | Out-Null }
    Invoke-Native 'generate-typescript' { npm run generate-typescript 2>&1 | Out-Null }
    Invoke-Native 'transpile (C#)' { npm run transpile --workspace=packages/csharp 2>&1 | Out-Null }
    $project = Join-Path $src 'packages\csharp\src\AlphaTab\AlphaTab.csproj'
    # PathMap + ContinuousIntegrationBuild: no machine path ends up in the DLL or PDB, so the bytes do not depend on the work folder.
    Invoke-Native 'dotnet build' { dotnet build $project -c Release --nologo -p:ContinuousIntegrationBuild=true "-p:PathMap=$src=/_/" 2>&1 | Select-Object -Last 4 }
}
finally { Pop-Location }

$built = Join-Path $src "packages\csharp\src\AlphaTab\bin\Release\$packageFile"
if (-not (Test-Path -LiteralPath $built)) { throw "The build did not produce $packageFile" }
$builtHash = Get-PackedDllHash $built
Write-Output "Built $packageFile; TabForge.AlphaTab.dll SHA-256 $builtHash"

$committed = Join-Path $vendor $packageFile
if ($Update) {
    # A rebuild of the same source gives the same DLL but not the same .nupkg bytes (zip metadata), and the package's SHA-512 is
    # recorded in packages.lock.json: an unchanged DLL keeps the committed package, so the lock files do not change for nothing.
    if ((Test-Path -LiteralPath $committed) -and (Get-PackedDllHash $committed) -eq $builtHash) {
        Write-Output "Unchanged: the committed package already holds this DLL; it was left as it is."
        exit 0
    }
    Copy-Item -LiteralPath $built -Destination $committed -Force
    Write-Output 'The package changed: regenerate the lock files (dotnet restore TabForge.sln --force-evaluate), docs/SBOM.md (tools/Write-Sbom.ps1) and the hashes in vendor/alphatab/README.md.'
    $nupkgHash = (Get-FileHash -LiteralPath $committed -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines = @(
        "$nupkgHash  $packageFile",
        "$builtHash  TabForge.AlphaTab.dll (lib/netstandard2.0, inside the package)",
        "upstream commit  $commit ($tag)"
    )
    [IO.File]::WriteAllLines((Join-Path $vendor 'SHA256SUMS'), $lines)
    Write-Output "Updated $committed"
    exit 0
}

if (-not (Test-Path -LiteralPath $committed)) { throw "$committed is missing; run with -Update to create it." }
$committedHash = Get-PackedDllHash $committed
if ($committedHash -ne $builtHash) {
    Write-Output "MISMATCH: the committed package holds TabForge.AlphaTab.dll $committedHash, the rebuild gives $builtHash."
    exit 1
}
Write-Output "OK: the committed package's TabForge.AlphaTab.dll is exactly what the pinned source plus the patches builds."
exit 0
