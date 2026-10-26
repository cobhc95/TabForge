# Owns: the one-line A/B parity run: clears the previous Avalonia renders and pairs under %TEMP%\tf-ab, renders the Avalonia twins with
# ParityShotsTests, checks that every mapped shot rendered, then compares them with the WPF reference shots through compare.py.
# Does not own: the twin choices (ParityShotMap), the comparison rules (compare.py).
# Tests: run by hand; see Source/tools/ab/README.md. Usage: Source\tools\ab\run.ps1 -Wpf <folder of the WPF PNGs>  (or set TF_AB_WPF).
# Works under Windows PowerShell 5.1: native tools write progress to stderr, which is logged, not treated as an error.
param(
    [string]$Wpf = $env:TF_AB_WPF,
    [double]$Threshold = 2.0
)
$ErrorActionPreference = 'Stop'
if (-not $Wpf) { throw 'Pass -Wpf <folder of the WPF reference PNGs> or set TF_AB_WPF.' }
$repo = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
# One folder per run: ParityShotsTests and compare.py both read TF_AB_OUT, so a second run never clobbers the first.
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('tf-ab-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$env:TF_AB_OUT = $work

# Runs a native command, writes its combined output to <work>\<Name>.log and fails on a non-zero exit code.
# The error preference is relaxed only for the call: PowerShell 5.1 turns each stderr line into an error under 'Stop'.
function Invoke-Native([string]$Name, [scriptblock]$Command) {
    $log = Join-Path $work ($Name + '.log')
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Command 2>&1 | ForEach-Object { "$_" } | Tee-Object -FilePath $log | Out-Host
    } finally {
        $ErrorActionPreference = $previous
    }
    if ($LASTEXITCODE -ne 0) { throw "$Name failed with exit code $LASTEXITCODE; see $log" }
}

Push-Location $repo
try {
    Invoke-Native 'render' { dotnet test Source/tests/TabForge.Avalonia.Tests/TabForge.Avalonia.Tests.csproj --filter FullyQualifiedName~ParityShotsTests }

    # Every manifest shot is either rendered or listed in missing.txt. Anything else means a render was lost, so the run stops here.
    $manifest = Get-Content -Raw -LiteralPath (Join-Path $repo 'Source\tests\parity\parity.capture.json') | ConvertFrom-Json
    $manifestShots = @($manifest | Where-Object { $_.PSObject.Properties.Name -contains 'shot' } | ForEach-Object { [string]$_.shot })
    $missingNames = @{}
    foreach ($line in @(Get-Content -LiteralPath (Join-Path $work 'missing.txt'))) {
        if ($line.Length -gt 0) { $missingNames[($line -split "`t")[0]] = $true }
    }
    $expectedShots = @($manifestShots | Where-Object { -not $missingNames.ContainsKey($_) })
    $avaloniaDirectory = Join-Path $work 'avalonia'
    $absentShots = @($expectedShots | Where-Object { -not (Test-Path -LiteralPath (Join-Path $avaloniaDirectory ($_ + '.png'))) })
    if ($absentShots.Count -gt 0) {
        throw "Only $($expectedShots.Count - $absentShots.Count) of $($expectedShots.Count) mapped shots rendered. Not rendered: $($absentShots -join ', '). See $work\render.log"
    }
    Write-Host "Rendered $($expectedShots.Count) of $($manifestShots.Count) manifest shots; $($missingNames.Count) have no twin."

    Invoke-Native 'compare' { python -I (Join-Path $repo 'Source\tools\ab\compare.py') --wpf $Wpf --threshold $Threshold }
} finally {
    Pop-Location
}
