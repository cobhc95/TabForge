# Test VMs (tests/parity/vm)

Hyper-V guests named `TabForge-VM`, `TabForge-VM2`, ... with automatic sign-in. Real mouse, keyboard and WPF rendering
run there, never on the owner's PC.

## Setup (owner, once per host)
- Each VM signs in automatically as the test user.
- Save the guest password outside the repo: `Get-Credential | Export-Clixml $env:TEMP\tabforge-vm-cred.xml`
- Optional watchdogs: `.\Watch-Vms.ps1` and `.\Watch-Progress.ps1` (both run until stopped).

## Scripts (one example each)
- `VmCommon.ps1`: shared helpers (lock names, the stale rule, back-off, argument checks). Dot-sourced, never run directly.
- `Test-VmCommon.ps1`: checks the helpers without a VM. `.\Test-VmCommon.ps1` prints `N passed, M failed`.
- `Invoke-InVm.ps1`: run one command in a free VM, copy an output folder back.
  `.\Invoke-InVm.ps1 -Command 'cmd /c ver'` prints `VM: TabForge-VM` and `exit: 0`.
- `Run-AB.ps1`: old and new build in one VM, then triage. `.\Run-AB.ps1 -Old C:\TF\old -New C:\TF\main -Out $env:TEMP\vm-ab`
- `Publish-ToVm.ps1`: build a checkout and put it in the VMs at `C:\TF\<Name>`. `.\Publish-ToVm.ps1 -Source C:\...\worktree -Name main`
  Default: every running TabForge-VM* (callers rely on this). `-VmName TabForge-VM2` limits it to one VM.
- `Run-Recorder.ps1`: FlaUI ParityRecorder in the VM. `.\Run-Recorder.ps1 -Name ab1 -RecorderArgs '--ab C:\TF\old\TabForge.exe C:\TF\main\TabForge.exe --out C:\TF\rec-out\ab1'`
- `Watch-Vms.ps1`: starts stopped VMs, reattaches desktops, removes dead locks, kills hung runs. Log: `%TEMP%\tf-vm-watch.log`.
- `Watch-Progress.ps1`: writes `%TEMP%\tf-progress.txt` (one line per item, `FLAG` lines for stalls).
- `triage.py`: compare two shot folders. `py -I triage.py <dirA> <dirB> <out>`

## Locks and heartbeats
- A run holds `%TEMP%\tf-vm-<vm>.lock` (a folder) for its whole duration and rewrites `%TEMP%\tf-vm-<vm>.heartbeat` every 30 s.
- The one stale rule (VmCommon.ps1): a heartbeat older than 3 minutes means a dead run. Invoke-InVm and Watch-Vms free the
  lock only while the heartbeat is still that stale one (re-checked right before the delete). A lock with no heartbeat is
  judged by its own age.
- A timed-out run kills its process tree, unregisters its task and deletes its `C:\TF\run-<id>.*` files. Input files
  (`-CopyIn`) and `-CleanupInVm` folders are deleted in the VM when the run ends. A run killed with its host leaves them
  behind; Watch-Vms deletes such leftovers after 3 hours.
- Publish-ToVm takes `%TEMP%\tf-vmdir-<Name>.lock` (older than 30 minutes is dead) and refuses to replace `C:\TF\<Name>`
  while a process runs from it. Pick a new `-Name` when it says so.
- Ceiling: the in-use check sees running processes only, so a run queued but not yet started can race a publish.

## Credentials
- Three rejected passwords in a row pause polling of that VM for 30 minutes. The count is shared by all watchdogs on
  this PC (`%TEMP%\tf-vm-auth-<vm>.txt`). Refresh `tabforge-vm-cred.xml`; do not retry in a loop.

## Arguments
- `-OutDir`, `-LocalOut`, `-VmName`, `-CopyIn`, `-CleanupInVm`, `-Old`, `-New`, `-RecorderArgs` may not contain
  `& | ^ % < > ( )`, a double quote or a line break; the scripts refuse them with a message. Paths with parentheses are refused too.
- `-Name` (Publish-ToVm, Run-Recorder) is a folder name under C:\TF: letters, digits, `.`, `_`, `-`; not `.` or `..`.
- `-CopyIn` file names must pass the same name rule. `-CleanupInVm` entries must sit directly under `C:\TF`.
- `-Command` is the exception: callers compose it with `&`, `>` and quotes (Run-AB, Run-Recorder). Quote every path in it.
- The command runs as `( command ) > run.log 2>&1` in a UTF-8 batch file, so the whole command group is logged.

## Adding a VM
1. Create a Hyper-V VM named `TabForge-VM<n>` with automatic sign-in as the test user.
2. Every script finds it by that name. Nothing else to change.
3. Run `.\Publish-ToVm.ps1 -Source <checkout> -Name main` so the new VM gets the build.

## Do not
- Publish into a folder another agent uses. Test with `-Name v1test`.
- Put the password in the repo or in a command line.
