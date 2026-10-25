param([string]$run, [string]$mode = 'replay', [int]$shards = 1, [string]$exe = 'C:\Users\COBHC\Downloads\TabForge\Source\work\gp5diff\tfbuild\TabForge.exe', [string]$tfdir = 'tf')
# Runs the TabForge side of the harness over a run folder: replay (sequences -> tf/*.json) or import (out/*.gp5 -> gp5/*.json),
# in $shards parallel TabForge processes, each with a 3-hour timeout. Logs go to <run>\tflog-<mode>-<k>.log.
$procs = @()
for ($k = 0; $k -lt $shards; $k++) {
    $env:TABFORGE_GP5DIFF_DIR = $run; $env:TABFORGE_GP5DIFF_MODE = $mode; $env:TABFORGE_GP5DIFF_SHARD = "$k/$shards"; $env:TABFORGE_GP5DIFF_TF = $tfdir
    $procs += Start-Process -FilePath $exe -ArgumentList '--selftest', "$run\tflog-$mode-$tfdir-$k.log", '--only', 'TestGp5DiffReplay' -PassThru
}
foreach ($p in $procs) { if (-not $p.WaitForExit(3 * 3600 * 1000)) { $p.Kill() } }
for ($k = 0; $k -lt $shards; $k++) { Select-String -Path "$run\tflog-$mode-$tfdir-$k.log" -Pattern 'gp5diff (replay|import):|passed' | ForEach-Object { $_.Line } }
