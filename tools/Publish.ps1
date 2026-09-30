<#
.SYNOPSIS
    The one dotnet publish command line for TabForge. CI and tools/Package-Release.ps1 call it, so a
    flag change cannot make CI test a different binary from the one released.
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
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\TabForge\TabForge.csproj'
dotnet publish $project -c Release -r win-x64 --no-restore --self-contained true -p:PublishSingleFile=true `
    -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true @ExtraArgs -o $Output
if ($LASTEXITCODE) { throw 'publish failed' }
