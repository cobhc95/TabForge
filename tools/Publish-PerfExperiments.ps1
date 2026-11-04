<#
.SYNOPSIS
  Isolated audit publications: audited composite baseline, noncomposite R2R, and no R2R.
  Never changes project properties or writes build/TabForge. Trimming and compression stay disabled.
  Size results are NOT a recommendation: native startup/playback/bridge validation is still required.
#>
param(
    [string]$Dotnet = (Join-Path $env:USERPROFILE '.dotnet-sdk-10.0.403\dotnet.exe'),
    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\..\build\perf-audit-sol')
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$output = [IO.Path]::GetFullPath($OutputRoot)
$experiments = [IO.Path]::GetFullPath((Join-Path $root 'build\perf-audit-sol'))
if ($output -ne $experiments -and -not $output.StartsWith($experiments + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use the dedicated build/perf-audit-sol folder (or a new subfolder); standard publication is not an experiment target.'
}
if (-not (Test-Path -LiteralPath $Dotnet -PathType Leaf)) { throw 'Pinned SDK executable not found.' }
$project = Join-Path $root 'Source\src\TabForge.Avalonia\TabForge.Avalonia.csproj'
$variants = @(
    @{ Name = 'audit-baseline-composite'; Ready = 'true'; Composite = 'true' },
    @{ Name = 'noncomposite-r2r'; Ready = 'true'; Composite = 'false' },
    @{ Name = 'no-r2r'; Ready = 'false'; Composite = 'false' }
)
$results = @()
foreach ($variant in $variants) {
    $folder = Join-Path $output $variant.Name
    if (Test-Path -LiteralPath $folder) { throw "Experiment folder already exists: $folder (choose/archive it manually, never overwrite)." }
    $artifacts = Join-Path $folder 'artifacts'
    $publish = Join-Path $folder 'publish'
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $properties = @(
        "-p:ArtifactsPath=$artifacts",
        "-p:PublishReadyToRun=$($variant.Ready)",
        "-p:PublishReadyToRunComposite=$($variant.Composite)",
        '-p:PublishTrimmed=false', '-p:EnableCompressionInSingleFile=false',
        '-p:TreatWarningsAsErrors=true', '-p:ContinuousIntegrationBuild=true', '-p:Deterministic=true'
    )
    $watch = [Diagnostics.Stopwatch]::StartNew()
    & $Dotnet restore $project -r win-x64 --locked-mode @properties *> (Join-Path $folder 'restore.log')
    if ($LASTEXITCODE) { throw "Restore failed: $folder\restore.log" }
    & $Dotnet publish $project -c Release -r win-x64 --no-restore --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true @properties -o $publish *> (Join-Path $folder 'publish.log')
    if ($LASTEXITCODE) { throw "Publish failed: $folder\publish.log" }
    $watch.Stop()
    if (Get-ChildItem -LiteralPath $publish -File -Recurse -Filter 'manifest.private*.json') { throw 'Private plugin manifest in experiment.' }
    $exe = Get-Item -LiteralPath (Join-Path $publish 'TabForge.exe')
    $files = @(Get-ChildItem -LiteralPath $publish -File -Recurse)
    $results += [pscustomobject]@{
        Variant = $variant.Name; ExeBytes = $exe.Length; FolderBytes = ($files | Measure-Object Length -Sum).Sum
        FileCount = $files.Count; PublishSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 2)
        Sha256 = (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash
    }
    $results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding utf8
}
$results | Format-Table Variant, ExeBytes, FolderBytes, FileCount, PublishSeconds -AutoSize
