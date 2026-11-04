<#
.SYNOPSIS
  Runs the published candidates' existing BASIC self-tests in isolated profiles/extraction folders.
  Reports self-test wall time, NOT application startup latency. Does not launch the user's normal profile.
#>
param([string]$Experiments = (Join-Path $PSScriptRoot '..\..\build\perf-audit-sol\verified'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\build\perf-audit-sol'))
$folder = [IO.Path]::GetFullPath($Experiments)
if ($folder -ne $root -and -not $folder.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Only dedicated performance experiment publications may be tested here.'
}
$results = Get-Content -LiteralPath (Join-Path $folder 'results.json') -Raw | ConvertFrom-Json
$runs = @()
$oldExtraction = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
try {
    foreach ($candidate in $results) {
        $candidateRoot = Join-Path $folder $candidate.Variant
        $run = Join-Path $candidateRoot ('checks-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
        New-Item -ItemType Directory -Path $run | Out-Null
        $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $run 'extracted'
        $log = Join-Path $run 'basic.log'
        $profile = Join-Path $run 'profile'
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $process = Start-Process -FilePath (Join-Path $candidateRoot 'publish\TabForge.exe') -PassThru `
            -ArgumentList @('--selftest', "`"$log`"", '--areas', 'basic', '--profile', "`"$profile`"")
        if (-not $process.WaitForExit(120000)) {
            $process.Kill()
            throw "Candidate BASIC self-test timed out: $($candidate.Variant)"
        }
        $process.Refresh()
        $watch.Stop()
        $runs += [pscustomobject]@{
            Variant = $candidate.Variant; ExitCode = $process.ExitCode; SelfTestWallMs = $watch.ElapsedMilliseconds
            Summary = (Get-Content -LiteralPath $log | Select-String 'TabForge self-test:' | Select-Object -Last 1).Line
            Log = $log
        }
    }
} finally { $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $oldExtraction }
$runs | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $folder 'verification.json') -Encoding utf8
$runs | Format-Table Variant, ExitCode, SelfTestWallMs, Summary -AutoSize
if (@($runs | Where-Object { $_.ExitCode -ne 0 }).Count -gt 0) { throw 'A candidate failed BASIC verification; inspect verification.json and its log.' }
