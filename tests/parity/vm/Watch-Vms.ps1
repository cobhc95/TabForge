# Keeps the test VMs usable while agents work: every two minutes, for each TabForge-VM*:
#  - a VM that is off or saved is started (a saved state that will not restore is discarded first);
#  - a disconnected desktop is reattached to the console (needed for rendering and real input);
#  - a run lock whose heartbeat is stale is removed (its run died; the rule is in VmCommon.ps1);
#  - a test process running for more than 25 minutes on a VM with no live run lock is killed (a hung run; the run's own time limit then reports it);
#  - after 3 credential rejections in a row a VM is not polled for 30 minutes (shared back-off, see VmCommon.ps1);
#  - run files and scheduled tasks left by a run whose host died are removed once they are 3 hours old.
# Writes one line per action to %TEMP%\tf-vm-watch.log. Usage: .\Watch-Vms.ps1 (runs until stopped)
. "$PSScriptRoot\VmCommon.ps1"
$log = "$env:TEMP\tf-vm-watch.log"
function Say($m) { "$(Get-Date -Format 'HH:mm:ss') $m" | Add-Content $log }
$cred = Import-Clixml "$env:TEMP\tabforge-vm-cred.xml"
while ($true) {
    foreach ($vm in Get-VM -Name 'TabForge-VM*') {
        $n = $vm.Name
        if (Test-VmBackoff $n) { continue }
        try {
            if ($vm.State -eq 'Saved') { try { Start-VM $n -ErrorAction Stop } catch { Remove-VMSavedState $n; Start-VM $n }; Say "$n started from saved"; continue }
            if ($vm.State -eq 'Off') { Start-VM $n; Say "$n started"; continue }
            if ($vm.State -ne 'Running') { continue }
            $held = (Test-Path (Get-VmLockPath $n)) -and -not (Test-VmLockStale $n)
            $busy = Invoke-Command -VMName $n -Credential $cred -ErrorAction Stop -ArgumentList $held -ScriptBlock {
                param($held)
                if ((query user 2>$null | Select-String 'Disc')) { tscon 1 /dest:console 2>$null | Out-Null; 'reattached' }
                $procs = Get-Process TabForge, ParityRecorder -ErrorAction SilentlyContinue
                # A run holding a live lock owns its processes and its own time limit; only unowned ones are judged hung.
                if (-not $held) { foreach ($p in $procs) { if ($p.StartTime -lt (Get-Date).AddMinutes(-25)) { Stop-Process $p -Force; "killed hung $($p.ProcessName)" } } }
                # Leftovers of a run whose host died before it could clean up (batch, task and files older than 3 hours).
                foreach ($f in @(Get-ChildItem C:\TF -Filter 'run-*.cmd' -ErrorAction SilentlyContinue | Where-Object { $_.CreationTime -lt (Get-Date).AddHours(-3) })) {
                    $id = $f.BaseName.Substring(4)
                    Unregister-ScheduledTask -TaskName "TabForgeTest-$id" -Confirm:$false -ErrorAction SilentlyContinue
                    Remove-Item "C:\TF\run-$id.*" -Force -ErrorAction SilentlyContinue
                    "swept leftover run $id"
                }
                if ($procs) { 'busy' } else { 'idle' }
            }
            Register-VmOk $n
            foreach ($b in $busy) { if ($b -ne 'busy' -and $b -ne 'idle') { Say "$n $b" } }
            if (Remove-VmLockIfStale $n) { Say "$n stale lock removed" }
        } catch {
            if (Register-VmAuthOutcome $n $_) { Say "$n rejected the saved credential 3 times: not polling it for 30 min (refresh tabforge-vm-cred.xml)" }
            Say "$n check failed: $($_.Exception.Message)"
        }
    }
    Start-Sleep 120
}
