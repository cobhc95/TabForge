# Shared helpers for the test-VM scripts (dot-sourced by them; see README.md).
# Owns: the run-lock file names, the one stale-lock rule and its removal, the shared credential back-off, the argument checks.
# Does not own: taking locks, running anything in a VM, or the watchdogs' loops (those stay in Invoke-InVm and the watchdogs).
# Tests: tests/parity/vm/Test-VmCommon.ps1 (runs without a VM).

# Lock = directory %TEMP%\tf-vm-<vm>.lock (held for a whole run); heartbeat = file %TEMP%\tf-vm-<vm>.heartbeat next to it,
# rewritten every 30 seconds by the holder.
function Get-VmLockPath([string] $Vm) { "$env:TEMP\tf-vm-$Vm.lock" }
function Get-VmHeartbeatPath([string] $Vm) { "$env:TEMP\tf-vm-$Vm.heartbeat" }

# The time the holder last proved it is alive: the heartbeat's write time, or the lock's own creation time when no heartbeat
# was ever written. $null when the VM has no lock.
function Get-VmHeartbeatStamp([string] $Vm) {
    $hb = Get-Item (Get-VmHeartbeatPath $Vm) -ErrorAction SilentlyContinue
    if ($hb) { return $hb.LastWriteTime }
    $lock = Get-Item (Get-VmLockPath $Vm) -ErrorAction SilentlyContinue
    if ($lock) { return $lock.CreationTime }
    $null
}

# Minutes since the holder last proved it is alive; -1 when the VM has no lock.
function Get-VmLockAgeMinutes([string] $Vm) {
    $stamp = Get-VmHeartbeatStamp $Vm
    if ($null -eq $stamp) { return -1 }
    [int]((Get-Date) - $stamp).TotalMinutes
}

# The one stale-lock rule: a lock whose heartbeat is older than 3 minutes belongs to a dead run.
function Test-VmLockStale([string] $Vm) {
    (Get-VmLockAgeMinutes $Vm) -gt 3
}

function Remove-VmLock([string] $Vm) {
    Remove-Item (Get-VmLockPath $Vm), (Get-VmHeartbeatPath $Vm) -Recurse -Force -ErrorAction SilentlyContinue
}

# Removes a lock only while its heartbeat is still the stale one that was judged: a holder that refreshed the heartbeat
# after the judgement keeps its lock. A refresh in the few milliseconds between the re-check and the delete is not caught.
function Remove-VmLockIfStale([string] $Vm) {
    $judged = Get-VmHeartbeatStamp $Vm
    if ($null -eq $judged -or ((Get-Date) - $judged).TotalMinutes -le 3) { return $false }
    if ((Get-VmHeartbeatStamp $Vm) -ne $judged) { return $false }
    Remove-VmLock $Vm
    $true
}

# True when an error from New-PSSession or Invoke-Command looks like a rejected guest password.
function Test-VmAuthError($ErrorRecord) {
    $ErrorRecord.Exception.Message -match 'password|credential|access is denied|logon|authenticat|unauthori'
}

# Credential back-off, shared by every watchdog on this PC through %TEMP%\tf-vm-auth-<vm>.txt ("fails;until").
# Three rejections in a row pause polling of that VM for 30 minutes, so a stale saved password cannot lock the guest account.
# A successful connection clears the count.
function Read-VmAuthState([string] $Vm) {
    $fails = 0; $until = $null
    $file = "$env:TEMP\tf-vm-auth-$Vm.txt"
    if (Test-Path $file) {
        $parts = (([string](Get-Content $file -Raw -ErrorAction SilentlyContinue)).Trim() -split ';')
        if ($parts[0]) { $fails = [int]$parts[0] }
        if ($parts.Count -gt 1 -and $parts[1]) { $until = [datetime]::Parse($parts[1], [Globalization.CultureInfo]::InvariantCulture) }
    }
    [pscustomobject]@{ Fails = $fails; Until = $until }
}

function Write-VmAuthState([string] $Vm, [int] $Fails, $Until) {
    $untilText = if ($null -ne $Until) { $Until.ToString('o') } else { '' }
    Set-Content "$env:TEMP\tf-vm-auth-$Vm.txt" "$Fails;$untilText"
}

function Test-VmBackoff([string] $Vm) {
    $state = Read-VmAuthState $Vm
    [bool]($state.Until -and (Get-Date) -lt $state.Until)
}

# Counts a failed connection. Returns $true on the call that starts a 30-minute pause; other errors are not counted.
function Register-VmAuthOutcome([string] $Vm, $ErrorRecord) {
    if (-not (Test-VmAuthError $ErrorRecord)) { return $false }
    $fails = 1 + (Read-VmAuthState $Vm).Fails
    if ($fails -lt 3) { Write-VmAuthState $Vm $fails $null; return $false }
    Write-VmAuthState $Vm 0 ((Get-Date).AddMinutes(30))
    $true
}

function Register-VmOk([string] $Vm) { Write-VmAuthState $Vm 0 $null }

# Refuses values that would break the batch-file text written into the VM (the command group is wrapped in parentheses).
# Callers quote paths themselves in the commands they build.
function Assert-PlainArg([string] $Name, [string] $Value) {
    if ($Value -and $Value -match '[&|^%<>"()\r\n]') {
        throw "-$Name has a character not allowed in VM run arguments (& | ^ % < > ( ) or double quote, or a line break): $Value"
    }
}

# A name that becomes a folder under C:\TF (it is deleted recursively there): letters, digits, '.', '_', '-'; not '.' or '..'.
function Assert-SafeName([string] $Name, [string] $Param = 'Name') {
    if ($Name -notmatch '^[A-Za-z0-9._-]+$' -or $Name -eq '.' -or $Name -eq '..') {
        throw "$Param must be letters, digits, '.', '_' or '-' only, and not '.' or '..': $Name"
    }
}

# A path the scripts may delete inside a VM: directly under C:\TF with a safe name.
function Assert-VmTfPath([string] $Path) {
    if ($Path -match '^C:\\TF\\(.+)$') { Assert-SafeName $Matches[1] 'VM cleanup path' }
    else { throw "VM cleanup path must be directly under C:\TF: $Path" }
}
