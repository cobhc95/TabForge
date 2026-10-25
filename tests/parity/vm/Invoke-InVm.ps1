# Runs a command inside a test VM's signed-in desktop session (so real mouse and keyboard tests and WPF rendering work there),
# waits for it with a time limit, and copies an output folder back to this PC.
# Several test VMs may exist (TabForge-VM, TabForge-VM2, ...): each run takes whichever VM is free (one run per VM at a time,
# because a second run would steal focus from the first). Pass -VmName to pin one.
# Needs: VMs with automatic sign-in, and a credential saved by the owner with
#   Get-Credential | Export-Clixml $env:TEMP\tabforge-vm-cred.xml
# While it holds a VM it writes a heartbeat every 30 seconds (see VmCommon.ps1); a lock without a fresh heartbeat is stale.
# -CopyIn files are copied to C:\TF and deleted there after the run; -CleanupInVm lists C:\TF folders or files
# (directly under C:\TF) to delete after the output is copied back.
# Example: .\Invoke-InVm.ps1 -Command 'C:\TF\main\TabForge.exe --profile C:\TF\p --capture C:\TF\s.json C:\TF\out' -OutDir C:\TF\out -LocalOut $env:TEMP\vm-out
param(
    [Parameter(Mandatory)] [string] $Command,
    [string] $OutDir,
    [string] $LocalOut,
    [string[]] $CopyIn = @(),
    [string[]] $CleanupInVm = @(),
    [string] $VmName,
    [int] $TimeoutSeconds = 900
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\VmCommon.ps1"
Assert-PlainArg 'OutDir' $OutDir
Assert-PlainArg 'LocalOut' $LocalOut
Assert-PlainArg 'VmName' $VmName
foreach ($f in $CopyIn) { Assert-PlainArg 'CopyIn' $f; Assert-SafeName (Split-Path $f -Leaf) 'CopyIn file name' }
foreach ($p in $CleanupInVm) { Assert-PlainArg 'CleanupInVm' $p; Assert-VmTfPath $p }

$running = @((Get-VM -Name 'TabForge-VM*' | Where-Object State -eq 'Running').Name | Sort-Object)
if ($VmName) {
    if ((Get-VM -Name $VmName -ErrorAction SilentlyContinue).State -ne 'Running') { throw "Test VM $VmName is not running. Start it, or drop -VmName to use any running TabForge-VM*." }
    $candidates = @($VmName)
} else {
    if (-not $running) { throw 'No TabForge-VM* test VM is running. Start one (Watch-Vms.ps1 starts stopped VMs).' }
    $candidates = $running
}

$waited = [Diagnostics.Stopwatch]::StartNew()
$lock = $null; $hbJob = $null; $s = $null; $outputCopied = $false
try {
    while (-not $lock) {
        foreach ($vm in $candidates) {
            Remove-VmLockIfStale $vm | Out-Null
            try { New-Item (Get-VmLockPath $vm) -ItemType Directory -ErrorAction Stop | Out-Null; $lock = Get-VmLockPath $vm; $VmName = $vm; break } catch { }
        }
        if (-not $lock) { if ($waited.Elapsed.TotalMinutes -gt 40) { throw 'All test VMs busy for 40 minutes' }; Start-Sleep 5 }
    }
    $hbPath = Get-VmHeartbeatPath $VmName
    Set-Content $hbPath (Get-Date -Format o)
    # Keeps the heartbeat fresh while this process waits on the VM; the job stops itself when this process is gone.
    $hbJob = Start-Job -ArgumentList $hbPath, $PID -ScriptBlock {
        param($file, $owner)
        while (Get-Process -Id $owner -ErrorAction SilentlyContinue) { Set-Content $file (Get-Date -Format o); Start-Sleep -Seconds 30 }
    }

    "VM: $VmName"
    $cred = Import-Clixml "$env:TEMP\tabforge-vm-cred.xml"
    $s = New-PSSession -VMName $VmName -Credential $cred
    foreach ($f in $CopyIn) { Copy-Item $f -Destination "C:\TF\$(Split-Path $f -Leaf)" -ToSession $s -Recurse -Force }
    # Each run has its own files and task name, so runs never overwrite each other's even if two share a VM.
    $id = [guid]::NewGuid().ToString('N').Substring(0, 8)
    $res = Invoke-Command -Session $s -ArgumentList $Command, $cred.UserName, $TimeoutSeconds, $id -ScriptBlock {
        param($cmd, $user, $limit, $id)
        function Stop-Tree($procId, $all) {
            foreach ($c in @($all | Where-Object { $_.ParentProcessId -eq $procId })) { Stop-Tree $c.ProcessId $all }
            Stop-Process -Id $procId -Force -ErrorAction SilentlyContinue
        }
        # The desktop must be active (not disconnected) for rendering and real input: reattach it to the console if needed.
        if ((query user 2>$null | Select-String 'Disc')) { tscon 1 /dest:console 2>$null | Out-Null; Start-Sleep 2 }
        $bat = "C:\TF\run-$id.cmd"; $log = "C:\TF\run-$id.log"; $done = "C:\TF\run-$id.done"; $task = "TabForgeTest-$id"
        # UTF-8 without a BOM, chcp first; the whole command group (not just its last command) goes to the log.
        $text = "@echo off`r`nchcp 65001>nul`r`n($cmd`r`n) > `"$log`" 2>&1`r`necho %ERRORLEVEL% > `"$done`"`r`n"
        [IO.File]::WriteAllText($bat, $text, (New-Object Text.UTF8Encoding $false))
        $code = 'TIMEOUT'; $logBytes = $null; $timedOut = $false
        try {
            $action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument "/c $bat"
            $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
            Register-ScheduledTask -TaskName $task -Action $action -Principal $principal -Force | Out-Null
            Start-ScheduledTask -TaskName $task
            $sw = [Diagnostics.Stopwatch]::StartNew()
            while (-not (Test-Path $done)) {
                if ($sw.Elapsed.TotalSeconds -gt $limit) { $timedOut = $true; break }
                Start-Sleep -Seconds 2
            }
            if (-not $timedOut) { $code = (Get-Content $done).Trim() }
        } finally {
            if ($timedOut) {
                # Kill only this run's batch and the processes it started: another run may already be using this VM.
                # Orphans whose parent already exited are left to Watch-Vms (it kills TabForge processes older than 25 minutes).
                $all = @(Get-CimInstance Win32_Process)
                foreach ($r in @($all | Where-Object { $_.CommandLine -like "*$bat*" })) { Stop-Tree $r.ProcessId $all }
            }
            Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue | Out-Null
            Unregister-ScheduledTask -TaskName $task -Confirm:$false -ErrorAction SilentlyContinue | Out-Null
            if (Test-Path $log) { try { $logBytes = [IO.File]::ReadAllBytes($log) } catch { } }
            Remove-Item "C:\TF\run-$id.*" -Force -ErrorAction SilentlyContinue
        }
        [pscustomobject]@{ Code = $code; Log = $logBytes }
    }
    "exit: $($res.Code)"
    if ($OutDir -and $LocalOut) {
        New-Item $LocalOut -ItemType Directory -Force | Out-Null
        Copy-Item $OutDir -Destination $LocalOut -FromSession $s -Recurse -Force
        # Fails the run here, with the folder named, instead of later in triage when the shots are already gone.
        $arrived = Join-Path $LocalOut (Split-Path $OutDir -Leaf)
        if (-not (Test-Path -LiteralPath $arrived)) { throw "Output folder $OutDir did not arrive at $arrived. It stays in the VM." }
        $outputCopied = $true
        if ($res.Log) { [IO.File]::WriteAllBytes("$LocalOut\run.log", [byte[]] $res.Log) }
    }
} finally {
    # The VM-side cleanup and the lock release run even when a step above failed.
    try {
        if ($s) {
            $leftovers = @($CopyIn | ForEach-Object { "C:\TF\$(Split-Path $_ -Leaf)" }) + @($CleanupInVm)
            # The output folder is removed only after its copy-back succeeded, so a failed copy keeps the shots in the VM.
            if ($OutDir -and -not $outputCopied) { $leftovers = @($leftovers | Where-Object { $_ -ne $OutDir }) }
            if ($leftovers) {
                Invoke-Command -Session $s -ArgumentList (, $leftovers) -ScriptBlock {
                    param($paths)
                    foreach ($p in $paths) { Remove-Item -LiteralPath $p -Recurse -Force -ErrorAction SilentlyContinue }
                } | Out-Null
            }
        }
    } catch { Write-Warning "VM cleanup failed: $($_.Exception.Message)" }
    if ($s) { Remove-PSSession $s -ErrorAction SilentlyContinue }
    if ($hbJob) { Stop-Job $hbJob -ErrorAction SilentlyContinue; Remove-Job $hbJob -Force -ErrorAction SilentlyContinue }
    if ($lock) { Remove-VmLock $VmName }
}
