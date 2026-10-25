# Checks the shared VM helpers in VmCommon.ps1 without any VM: lock staleness and removal, argument checks, the credential
# back-off. Uses a fake VM name and cleans up its files. Usage: powershell -NoProfile -File Test-VmCommon.ps1
# Prints "N passed, M failed" and exits 1 when anything failed.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\VmCommon.ps1"
$vm = 'TabForge-VMselftest'
$script:pass = 0; $script:fail = 0

function Check([string] $Name, [bool] $Ok) {
    if ($Ok) { $script:pass++ } else { $script:fail++; "FAIL $Name" }
}
function Throws([scriptblock] $Block) {
    try { & $Block | Out-Null; $false } catch { $true }
}
function Clean {
    Remove-VmLock $vm
    Remove-Item "$env:TEMP\tf-vm-auth-$vm.txt" -Force -ErrorAction SilentlyContinue
}

# Lock staleness: a lock is stale only when its heartbeat is older than 3 minutes.
Clean
Check 'no lock is not stale' (-not (Test-VmLockStale $vm))
Check 'no lock has age -1' ((Get-VmLockAgeMinutes $vm) -eq -1)
New-Item (Get-VmLockPath $vm) -ItemType Directory | Out-Null
Check 'lock without heartbeat, fresh, is not stale' (-not (Test-VmLockStale $vm))
Set-Content (Get-VmHeartbeatPath $vm) 'x'
Check 'fresh heartbeat is not stale' (-not (Test-VmLockStale $vm))
(Get-Item (Get-VmHeartbeatPath $vm)).LastWriteTime = (Get-Date).AddMinutes(-2)
Check 'heartbeat 2 minutes old is not stale' (-not (Test-VmLockStale $vm))
(Get-Item (Get-VmHeartbeatPath $vm)).LastWriteTime = (Get-Date).AddMinutes(-5)
Check 'heartbeat 5 minutes old is stale' (Test-VmLockStale $vm)
Check 'Remove-VmLockIfStale removes a stale lock' (Remove-VmLockIfStale $vm)
Check 'lock gone after removal' (-not (Test-Path (Get-VmLockPath $vm)))
Check 'heartbeat gone after removal' (-not (Test-Path (Get-VmHeartbeatPath $vm)))

# The re-check: a holder that refreshed the heartbeat keeps its lock.
New-Item (Get-VmLockPath $vm) -ItemType Directory | Out-Null
Set-Content (Get-VmHeartbeatPath $vm) 'x'
Check 'Remove-VmLockIfStale keeps a fresh lock' (-not (Remove-VmLockIfStale $vm))
Check 'fresh lock still present' (Test-Path (Get-VmLockPath $vm))
Clean

# Argument checks: characters that would break the batch text are refused, plain paths pass.
$badChars = @('a&b', 'a|b', 'a^b', 'a%b', 'a<b', 'a>b', 'a"b', 'a(b', 'a)b', "a`nb", "a`rb")
for ($i = 0; $i -lt $badChars.Count; $i++) {
    $bad = $badChars[$i]
    Check "Assert-PlainArg refuses bad character case $i" (Throws { Assert-PlainArg 'Test' $bad })
}
Check 'Assert-PlainArg accepts a plain path' (-not (Throws { Assert-PlainArg 'Test' 'C:\TF\out-1' }))
Check 'Assert-PlainArg accepts an empty value' (-not (Throws { Assert-PlainArg 'Test' '' }))

# Safe names: a name becomes a folder deleted recursively under C:\TF.
foreach ($good in @('main', 'v1test', 'a.b-c_d', 'x1')) {
    Check "Assert-SafeName accepts '$good'" (-not (Throws { Assert-SafeName $good }))
}
foreach ($bad in @('', '.', '..', '../x', 'a b', 'a\b', 'x;y', 'a*', 'a&b', 'a/b')) {
    Check "Assert-SafeName refuses '$bad'" (Throws { Assert-SafeName $bad })
}
Check 'Assert-VmTfPath accepts C:\TF\pA-abc' (-not (Throws { Assert-VmTfPath 'C:\TF\pA-abc' }))
Check 'Assert-VmTfPath refuses a path outside C:\TF' (Throws { Assert-VmTfPath 'C:\Windows\x' })
Check 'Assert-VmTfPath refuses traversal' (Throws { Assert-VmTfPath 'C:\TF\..\x' })
Check 'Assert-VmTfPath refuses a sub-folder' (Throws { Assert-VmTfPath 'C:\TF\a\b' })

# Credential back-off: three rejections in a row pause the VM; a successful connection clears the count.
Clean
$cat = [System.Management.Automation.ErrorCategory]::NotSpecified
$authError = [System.Management.Automation.ErrorRecord]::new([Exception]::new('The user name or password is incorrect.'), 'test', $cat, $null)
$otherError = [System.Management.Automation.ErrorRecord]::new([Exception]::new('The VM is not reachable.'), 'test', $cat, $null)
Check 'no back-off at start' (-not (Test-VmBackoff $vm))
Check 'first rejection does not start a pause' (-not (Register-VmAuthOutcome $vm $authError))
Check 'second rejection does not start a pause' (-not (Register-VmAuthOutcome $vm $authError))
Check 'unreachable error is not counted' (-not (Register-VmAuthOutcome $vm $otherError))
Check 'third rejection starts a pause' (Register-VmAuthOutcome $vm $authError)
Check 'pause is active' (Test-VmBackoff $vm)
Register-VmOk $vm
Check 'success clears the pause' (-not (Test-VmBackoff $vm))
Check 'count restarts after success' (-not (Register-VmAuthOutcome $vm $authError))
Clean

Clean
"$script:pass passed, $script:fail failed"
if ($script:fail -gt 0) { exit 1 }
