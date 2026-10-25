<#
.SYNOPSIS
    The one dotnet publish command line for TabForge. CI and Source/tools/Package-Release.ps1 call it, so a
    flag change cannot make CI test a different binary from the one released.
    ContinuousIntegrationBuild/Deterministic are always on here (not only when CI=true), so a local publish and a
    CI publish of the same commit produce the same bytes (see Source/docs/REPRODUCIBLE_BUILDS.md).
.PARAMETER Output
    Folder to publish into (created by dotnet publish; not cleared here).
.PARAMETER ExtraArgs
    Additional dotnet arguments, e.g. '-p:DebugType=portable'.
.NOTES
    Assumes restore already ran (uses --no-restore).
#>
param(
    [Parameter(Mandatory)][string]$Output,
    [string[]]$ExtraArgs = @()
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $root 'Source\src\TabForge.Avalonia\TabForge.Avalonia.csproj'
dotnet publish $project -c Release -r win-x64 --no-restore --self-contained true -p:PublishSingleFile=true `
    -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:ContinuousIntegrationBuild=true -p:Deterministic=true @ExtraArgs -o $Output
if ($LASTEXITCODE) { throw 'publish failed' }
