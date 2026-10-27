param([string]$clusters, [string]$out, [int]$top = 25, [int]$n = 4, [int]$rounds = 6, [string]$deskPrefix = 'gp5m')
# Repro minimisation: rounds of candidate sequences through both apps (minimize.py says which), each round with a timeout.
$py = "$env:USERPROFILE\Downloads\TabForge\Source\work\tools\venv\Scripts\python.exe"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = "$env:USERPROFILE\Downloads\TabForge\Source\work\gp5diff\bin\Gp5Driver.exe"
if (-not (Test-Path "$out\state.json")) { & $py "$here\minimize.py" init $clusters $out $top }
for ($k = 0; $k -lt $rounds; $k++) {
    if (Test-Path "$out\r$k\done.txt") { continue }
    $count = [int](& $py "$here\minimize.py" round $out $k)
    if ($count -eq 0) { break }
    $rd = "$out\r$k"
    $procs = @()
    for ($i = 0; $i -lt $n; $i++) {
        $procs += Start-Process -FilePath $exe -ArgumentList 'run', "$deskPrefix$i", $rd, "$i/$n", 300 -PassThru -NoNewWindow -RedirectStandardOutput "$rd\drv-$i.out" -RedirectStandardError "$rd\drv-$i.err"
    }
    & "$here\tf.ps1" $rd replay 2 | Out-Null
    foreach ($p in $procs) {
        if (-not $p.WaitForExit(90 * 60 * 1000)) {
            Get-CimInstance Win32_Process -Filter "Name='GP5.exe' and ParentProcessId=$($p.Id)" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
            $p.Kill()
        }
    }
    & "$here\tf.ps1" $rd import 2 | Out-Null
    & $py "$here\compare.py" $rd | Out-Null
    $changed = & $py "$here\minimize.py" update $out $k
    Set-Content "$rd\done.txt" "changed $changed"
    Add-Content "$out\min.log" "$(Get-Date -Format 'HH:mm') round $k candidates $count improved $changed"
}
