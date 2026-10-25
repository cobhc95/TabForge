# Runs the headless data steps of one build inside a test VM (Run-DataAB.ps1 copies this file and the two inputs under per-run names and starts it).
# Writes every output under -Out and a manifest.txt with one "step exit <code>" line per step, so a changed exit code is a difference too.
param(
    [Parameter(Mandatory)] [string] $Exe,
    [Parameter(Mandatory)] [string] $Out,
    [Parameter(Mandatory)] [string] $Song1,
    [Parameter(Mandatory)] [string] $Song2,
    [int] $StepTimeoutMs = 240000
)
$ErrorActionPreference = 'Continue'
$exeFile = Join-Path $Exe 'TabForge.exe'
$songs = @(@{ Name = 'song1'; Path = $Song1 }, @{ Name = 'song2'; Path = $Song2 })
Remove-Item $Out -Recurse -Force -ErrorAction SilentlyContinue
foreach ($sub in 'out', 'render', 'settings') { New-Item (Join-Path $Out $sub) -ItemType Directory -Force | Out-Null }
$manifest = New-Object System.Collections.Generic.List[string]
# A build without the command opens its window instead and waits there: each step is time-boxed, and an overrun is killed and recorded.
# --profile makes this run's output folder a scratch root, so the save and settings commands may write under it (they refuse other folders).
function Step([string] $Name, [string[]] $Arguments) {
    $p = Start-Process -FilePath $exeFile -ArgumentList ($Arguments + @('--profile', $Out)) -PassThru
    if ($p.WaitForExit($StepTimeoutMs)) { $manifest.Add("$Name exit $($p.ExitCode)") }
    else { $p.Kill(); $manifest.Add("$Name exit timeout") }
}
foreach ($s in $songs) {
    $o = Join-Path $Out 'out'
    Step "$($s.Name) save-copy" @('--save-copy', $s.Path, (Join-Path $o "$($s.Name).tforge"))
    Step "$($s.Name) exportgp" @('--exportgp', $s.Path, (Join-Path $o "$($s.Name).gp"))
    Step "$($s.Name) musicxml" @('--musicxml-export', $s.Path, (Join-Path $o "$($s.Name).musicxml"))
}
Step 'render-probe' @('--render-probe', (Join-Path $Out 'render\probe.wav'))
Step 'settings-roundtrip' @('--settings-roundtrip', (Join-Path $Out 'settings'))
$manifest | Set-Content -Path (Join-Path $Out 'manifest.txt') -Encoding ascii
