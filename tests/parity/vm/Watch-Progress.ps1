# Progress watchdog (no AI, no tokens): every two minutes it writes %TEMP%\tf-progress.txt, a short status with one line per
# item and a FLAG line for anything that looks stalled, so a cheap scheduled check only has to read this file:
#  - agent worktrees (.claude\worktrees\agent-*) changed in the last 3 hours: minutes since the last file change; FLAG at 20 minutes idle
#    while uncommitted changes exist (an agent that stopped mid-edit);
#  - dotnet builds and TabForge/ParityRecorder processes on this PC: FLAG past 15 minutes (a hung build or test);
#  - test VMs: run locks whose heartbeat is stale (the stale-lock rule in VmCommon.ps1), test processes in the VM older than
#    20 minutes, VM not running; a VM is not polled while the shared credential back-off is active (VmCommon.ps1);
#  - slot locks (%TEMP%\tf-slot*.lock) older than 30 minutes (a dead build left them).
# Usage: .\Watch-Progress.ps1 (runs until stopped)
. "$PSScriptRoot\VmCommon.ps1"
$repo = "$env:USERPROFILE\Downloads\TabForge"
$out = "$env:TEMP\tf-progress.txt"
$cred = Import-Clixml "$env:TEMP\tabforge-vm-cred.xml"
while ($true) {
    $lines = @("updated $(Get-Date -Format 'HH:mm:ss')")
    $flags = @()
    foreach ($wt in Get-ChildItem "$repo\.claude\worktrees" -Directory -Filter 'agent-*' -ErrorAction SilentlyContinue) {
        $src = Join-Path $wt.FullName 'src'
        if (-not (Test-Path $src)) { continue }
        $last = Get-ChildItem $src -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $last -or $last.LastWriteTime -lt (Get-Date).AddHours(-3)) { continue }
        $idle = [int]((Get-Date) - $last.LastWriteTime).TotalMinutes
        $dirty = (git -C $wt.FullName status --porcelain 2>$null | Measure-Object).Count
        $lines += "agent $($wt.Name.Substring(6, 8)) idle ${idle}m dirty $dirty"
        if ($idle -ge 20 -and $dirty -gt 0) { $flags += "FLAG agent $($wt.Name) idle ${idle}m with $dirty uncommitted files" }
    }
    foreach ($p in Get-Process dotnet, TabForge, ParityRecorder, ParityCheck -ErrorAction SilentlyContinue) {
        if ($p.Path -like "$repo\build\*") { continue }   # the owner's own TabForge
        $age = [int]((Get-Date) - $p.StartTime).TotalMinutes
        if ($p.ProcessName -eq 'dotnet' -and $age -ge 15) {
            $cmd = (Get-CimInstance Win32_Process -Filter "ProcessId=$($p.Id)").CommandLine
            if ($cmd -match 'build|publish|run' -and $cmd -notmatch '/nodemode:') { $flags += "FLAG host $($p.ProcessName) pid $($p.Id) running ${age}m: $($cmd.Substring(0, [Math]::Min(120, $cmd.Length)))" }
        } elseif ($p.ProcessName -ne 'dotnet' -and $age -ge 15) { $flags += "FLAG host $($p.ProcessName) pid $($p.Id) running ${age}m ($($p.Path))" }
    }
    foreach ($l in Get-ChildItem $env:TEMP -Directory -Filter 'tf-slot*.lock' -ErrorAction SilentlyContinue) {
        if ($l.CreationTime -lt (Get-Date).AddMinutes(-30)) { $flags += "FLAG slot lock $($l.Name) is $([int]((Get-Date) - $l.CreationTime).TotalMinutes)m old" }
    }
    foreach ($vm in Get-VM -Name 'TabForge-VM*') {
        $n = $vm.Name
        if ($vm.State -ne 'Running') { $flags += "FLAG $n is $($vm.State)"; continue }
        $lockAge = Get-VmLockAgeMinutes $n
        if (Test-VmBackoff $n) {
            $procs = @('backoff'); $lines += "$n lock ${lockAge}m procs backoff"
        } else {
            try {
                $procs = Invoke-Command -VMName $n -Credential $cred -ErrorAction Stop -ScriptBlock {
                    Get-Process TabForge, ParityRecorder, dotnet -ErrorAction SilentlyContinue | ForEach-Object { "$($_.ProcessName):$([int]((Get-Date) - $_.StartTime).TotalMinutes)" }
                }
                Register-VmOk $n
            } catch {
                $procs = @('unreachable')
                Register-VmAuthOutcome $n $_ | Out-Null
            }
            $lines += "$n lock ${lockAge}m procs $($procs -join ',')"
        }
        if (Test-VmLockStale $n) { $flags += "FLAG $n run lock heartbeat ${lockAge}m old" }
        foreach ($p in $procs) { if ($p -match ':(\d+)$' -and [int]$Matches[1] -ge 20) { $flags += "FLAG $n process $p minutes" } }
        if ($procs -contains 'unreachable') { $flags += "FLAG $n unreachable" }
    }
    # Agents that changed a file in the last 15 minutes count as working; fewer than 3 means the queue may be running dry.
    $active = @($lines | Where-Object { $_ -match '^agent \S+ idle (\d+)m' -and [int]$Matches[1] -lt 15 }).Count
    $lines += "ACTIVE agents $active"
    if ($active -lt 3) { $flags += "FLAG QUEUE-LOW only $active agents active: start the next jobs from work/port/QUEUE_PORT.md" }
    if (-not $flags) { $lines += 'OK nothing stalled' }
    ($lines + $flags) | Set-Content $out -Encoding utf8
    Start-Sleep 120
}
