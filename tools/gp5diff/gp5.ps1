param([string]$run, [int]$n = 8, [int]$restartEvery = 150, [int]$timeoutMin = 600, [string]$exe = 'C:\Users\COBHC\Downloads\TabForge\work\gp5diff\bin\Gp5Driver.exe', [string]$prefix = 'gp5d_', [int]$copyOffset = 0)
# Runs the GP5 side of the harness over a run folder with $n GP5 instances, each on its own hidden desktop (gp5d_<k>), each
# taking every n-th sequence. Each driver gets a timeout; a driver that runs over is killed together with its GP5.
# A file named STOP in the run folder makes every driver finish its sequence and close its GP5.
$procs = @()
for ($k = 0; $k -lt $n; $k++) {
    $procs += Start-Process -FilePath $exe -ArgumentList 'run', "$prefix$k", $run, "$k/$n", $restartEvery, "C:\Users\COBHC\Downloads\TabForge\work\gp5diff\gp5copies\$($k + $copyOffset)\GP5.exe" -PassThru -NoNewWindow -RedirectStandardOutput "$run\gp5drv-$k.out" -RedirectStandardError "$run\gp5drv-$k.err"
}
$deadline = (Get-Date).AddMinutes($timeoutMin)
foreach ($p in $procs) {
    $left = [Math]::Max(1000, ($deadline - (Get-Date)).TotalMilliseconds)
    if (-not $p.WaitForExit([int]$left)) {
        # A driver over its time: kill it and the GP5 it started (never another GP5).
        Get-CimInstance Win32_Process -Filter "Name='GP5.exe' and ParentProcessId=$($p.Id)" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        $p.Kill()
        Add-Content "$run\..\hangs.log" "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') driver $($p.Id) over the run timeout: killed with its GP5"
    }
}
Get-Content "$run\gp5drv-*.out"
