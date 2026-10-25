# Agent watchdog: watches the shared coordination log and reports new entries.
#
# Why: the lead should not have to poll by hand, and must not block a turn waiting.
# This runs as a background job, exits as soon as an agent posts DONE or BLOCKED
# (or after -Minutes), and prints a compact summary either way.
#
# Usage:
#   pwsh -File Source/tools/watch-agents.ps1 -Minutes 20
#   pwsh -File Source/tools/watch-agents.ps1 -Minutes 5 -IntervalSec 10

param(
    [int]$Minutes = 20,
    [int]$IntervalSec = 20,
    [string]$Repo = "C:\Users\COBHC\Downloads\TabForge"
)

$log = Join-Path $Repo "AGENT-CHAT.md"
$deadline = (Get-Date).AddMinutes($Minutes)
$seen = (Get-Content $log -ErrorAction Stop | Measure-Object -Line).Lines

Write-Output "watchdog: watching $log from line $seen for $Minutes min (poll ${IntervalSec}s)"

function Show-Activity {
    Write-Output "watchdog: files changed in the last 15 min:"
    Get-ChildItem (Join-Path $Repo "Source\src"), (Join-Path $Repo "Source\tests") -Recurse -Include *.cs -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -gt (Get-Date).AddMinutes(-15) } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 8 |
        ForEach-Object { Write-Output ("  {0}  {1}" -f $_.LastWriteTime.ToString('HH:mm:ss'), $_.FullName.Replace($Repo + '\', '')) }
}

while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds $IntervalSec
    $lines = Get-Content $log -ErrorAction SilentlyContinue
    $count = ($lines | Measure-Object -Line).Lines
    if ($count -gt $seen) {
        $new = $lines | Select-Object -Skip $seen
        Write-Output "---- new log entries at $(Get-Date -Format 'HH:mm:ss') ----"
        $new | ForEach-Object { Write-Output $_ }
        $seen = $count
        if ($new -match 'STATUS:\s*(DONE|BLOCKED)') {
            Show-Activity
            Write-Output "watchdog: agent posted DONE/BLOCKED - exiting so the lead can act"
            exit 0
        }
    }
}

Write-Output "watchdog: no new DONE/BLOCKED within $Minutes min - job still running elsewhere"
Show-Activity
exit 0
