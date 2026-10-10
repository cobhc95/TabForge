# Runs one self-test, or a comma-separated list, from a full-suite build.
# Usage: tools\run-test.ps1 -Test TestVideoExport [-Log <path>] [-TimeoutSec 600]
#
# Builds tests/full-suite into the fixed folder %TEMP%\tf-fs (incremental; the folder is never cleaned),
# then runs TabForge.exe --only with a scratch profile. The build and the run both hold the lock
# directory %TEMP%\tf-build.lock, so two jobs never run dotnet or TabForge.exe at the same time.
# Each step gets -TimeoutSec; a cold full-suite build may need 900.
# Exit codes: 0 all passed, 1 a test failed or there is no result line, 2 build failed or the lock
# was not free within 30 minutes, 124 a step timed out and was killed.
param(
    [Parameter(Mandatory = $true)] [string] $Test,
    [string] $Log,
    [int] $TimeoutSec = 600
)

$ErrorActionPreference = 'Stop'
if ($Test -notmatch '^[A-Za-z0-9_,]+$') { Write-Host "Test names may contain only letters, digits, underscore and comma: $Test"; exit 1 }

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'src\TabForge\TabForge.csproj'
$temp = $env:TEMP
$buildDir = Join-Path $temp 'tf-fs'
$profileDir = Join-Path $temp 'tf-fs-profile'
$lock = Join-Path $temp 'tf-build.lock'
$exe = Join-Path $buildDir 'TabForge.exe'
if (-not $Log) { $Log = Join-Path $temp ('tf-fs-' + ($Test -replace ',', '_') + '.log') }
$Log = [IO.Path]::GetFullPath($Log)

function Wait-ForLock {
    $deadline = (Get-Date).AddMinutes(30)
    while ($true) {
        try {
            New-Item -ItemType Directory -Path $lock -ErrorAction Stop | Out-Null
            return
        } catch {
            if ((Get-Date) -gt $deadline) { throw 'Gave up after 30 minutes waiting for the lock %TEMP%\tf-build.lock.' }
            Start-Sleep -Seconds 20
        }
    }
}

# Starts a process, waits at most $Seconds, kills its whole tree on timeout. Returns the exit code, or 124.
function Invoke-Timed {
    param([string] $File, [string] $Arguments, [int] $Seconds, [string] $StdOut, [string] $StdErr)
    $p = if ($StdOut) {
        Start-Process -FilePath $File -ArgumentList $Arguments -PassThru -NoNewWindow -RedirectStandardOutput $StdOut -RedirectStandardError $StdErr
    } else {
        Start-Process -FilePath $File -ArgumentList $Arguments -PassThru -NoNewWindow
    }
    $null = $p.Handle   # PowerShell 5.1: without this ExitCode is null after WaitForExit(ms)
    if (-not $p.WaitForExit($Seconds * 1000)) {
        & taskkill.exe /PID $p.Id /T /F | Out-Null
        return 124
    }
    return $p.ExitCode
}

Wait-ForLock
try {
    New-Item -ItemType Directory -Force -Path $profileDir | Out-Null

    $buildOut = Join-Path $temp 'tf-fs-build.out.log'
    $buildErr = Join-Path $temp 'tf-fs-build.err.log'
    $buildArgs = 'build "' + $project + '" -c Release -v q -p:TabForgeFullSuite=true -o "' + $buildDir + '"'
    $code = Invoke-Timed -File 'dotnet' -Arguments $buildArgs -Seconds $TimeoutSec -StdOut $buildOut -StdErr $buildErr
    if ($code -ne 0) {
        Write-Host "Build failed (exit $code). Output: $buildOut ; errors: $buildErr"
        exit $(if ($code -eq 124) { 124 } else { 2 })
    }

    if (Test-Path -LiteralPath $Log) { Remove-Item -LiteralPath $Log -Force }
    $runArgs = '--profile "' + $profileDir + '" --selftest "' + $Log + '" --only ' + $Test
    $code = Invoke-Timed -File $exe -Arguments $runArgs -Seconds $TimeoutSec
    if ($code -eq 124) { Write-Host "Timed out after $TimeoutSec s and killed: $Test"; exit 124 }

    $result = $null
    if (Test-Path -LiteralPath $Log) {
        $result = Select-String -LiteralPath $Log -Pattern 'TabForge self-test: \d+ passed, \d+ failed' | Select-Object -Last 1
    }
    if (-not $result) { Write-Host "No result line in $Log (exit $code). Read the log."; exit 1 }

    $line = $result.Line.Trim()
    Write-Host $line
    Select-String -LiteralPath $Log -Pattern '\bFAIL\b' -CaseSensitive | Select-Object -First 20 | ForEach-Object { Write-Host $_.Line.Trim() }
    if ($line -match '(\d+) failed') { if ([int]$Matches[1] -gt 0) { exit 1 } }
    exit 0
} finally {
    Remove-Item -LiteralPath $lock -Recurse -Force -ErrorAction SilentlyContinue
}
