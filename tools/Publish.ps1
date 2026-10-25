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
# Private workspace: <root>\Source\tools\. Public repository: <root>\tools\ (sources at the root).
$top = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (Test-Path -LiteralPath (Join-Path $top 'TabForge.sln')) { $root = $top; $srcRoot = $top }
else { $root = [IO.Path]::GetFullPath((Join-Path $top '..')); $srcRoot = Join-Path $root 'Source' }
$project = Join-Path $srcRoot 'src\TabForge.Avalonia\TabForge.Avalonia.csproj'
dotnet publish $project -c Release -r win-x64 --no-restore --self-contained true -p:PublishSingleFile=true `
    -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:ContinuousIntegrationBuild=true -p:Deterministic=true @ExtraArgs -o $Output
if ($LASTEXITCODE) { throw 'publish failed' }
