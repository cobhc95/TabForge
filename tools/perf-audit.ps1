# TabForge resource audit: RAM, CPU, handles and threads.
# - measures idle, playback and post-playback cost
# - checks that idle does not spin the CPU and that memory does not creep when nothing is happening
# Runs under Windows PowerShell (Desktop edition) only: it drives the app through the UIAutomation client
# (UIAutomationClient / UIAutomationTypes), the .NET Framework WPF assemblies that host those types. The app
# itself is the Avalonia build, which exposes the same Win32 UIA peers as the WPF build did.
#Requires -PSEdition Desktop
param(
    [string]$Exe = "$PSScriptRoot\..\..\build\TabForge\TabForge.exe",
    [string]$Song = "$PSScriptRoot\..\..\Tabs\As I Lay Dying - Blinded (ver 2 by president100).gp5",
    [int]$IdleSeconds = 6,
    [int]$PlaySeconds = 10
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$script:fail = 0
function Report($name, $value, $ok, $detail = "") {
    if ($ok) { Write-Host ("  PASS  {0}: {1}" -f $name, $value) -ForegroundColor Green }
    else { $script:fail++; Write-Host ("  FAIL  {0}: {1} {2}" -f $name, $value, $detail) -ForegroundColor Red }
}

function Sample($p) {
    $p.Refresh()
    return [pscustomobject]@{
        WorkingSet = $p.WorkingSet64
        Private    = $p.PrivateMemorySize64
        Handles    = $p.HandleCount
        Threads    = $p.Threads.Count
        Cpu        = $p.TotalProcessorTime.TotalMilliseconds
        GcHeap     = [System.GC]::GetTotalMemory($false)
    }
}

function CpuPercent($before, $after, $seconds) {
    $cores = [Environment]::ProcessorCount
    $delta = $after.Cpu - $before.Cpu
    return [Math]::Round(100.0 * $delta / ($seconds * 1000.0 * $cores), 2)
}

$mb = { param($v) [Math]::Round($v / 1MB, 1) }

Get-Process TabForge -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item "$env:APPDATA\TabForge\settings.json" -ErrorAction SilentlyContinue

Write-Host "== launch =="
$t0 = Get-Date
$proc = Start-Process -FilePath $Exe -ArgumentList @("`"$Song`"") -PassThru
for ($i = 0; $i -lt 200 -and $proc.MainWindowHandle -eq 0; $i++) { Start-Sleep -Milliseconds 150; $proc.Refresh() }
$startupMs = [int]((Get-Date) - $t0).TotalMilliseconds
Start-Sleep -Seconds 4
$h = [IntPtr]$proc.MainWindowHandle
$afterLoad = Sample $proc
Report "startup" "$startupMs ms" ($startupMs -lt 15000)
Report "memory after loading a 167-bar / 7-track score" ("$( & $mb $afterLoad.WorkingSet) MB working set") ($afterLoad.WorkingSet -lt 700MB) "private=$( & $mb $afterLoad.Private) MB"

Write-Host "== idle =="
$a = Sample $proc
Start-Sleep -Seconds $IdleSeconds
$b = Sample $proc
$idleCpu = CpuPercent $a $b $IdleSeconds
$idleGrowth = $b.WorkingSet - $a.WorkingSet
Report "idle CPU over $IdleSeconds s" "$idleCpu %" ($idleCpu -lt 1.0)
Report "idle memory drift over $IdleSeconds s" "$( & $mb $idleGrowth) MB" ([Math]::Abs($idleGrowth) -lt 12MB)

Write-Host "== playback =="
$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$btnCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
$play = $null
foreach ($element in $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)) {
    if ($element.Current.Name -eq "Play or pause") { $play = $element; break }
}
if ($play) { $play.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
Start-Sleep -Milliseconds 800
$c = Sample $proc
Start-Sleep -Seconds $PlaySeconds
$d = Sample $proc
$playCpu = CpuPercent $c $d $PlaySeconds
# Budget: live playback repaints the visible systems on every note boundary (that is what makes the
# note highlight exact), so this is the score-repaint cost. Measured 7-13% on this machine depending
# on desktop load; the ceiling leaves room for a busy desktop without hiding a real regression.
Report "playback CPU over $PlaySeconds s" "$playCpu %" ($playCpu -lt 14.0) "budget 14%"
Report "memory while playing" "$( & $mb $d.WorkingSet) MB working set" ($d.WorkingSet -lt 900MB)

Write-Host "== stop and settle =="
if ($play) { try { $play.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch { } }
Start-Sleep -Seconds 1
$stop = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond) | Where-Object { $_.Current.Name -eq "Stop" } | Select-Object -First 1
if ($stop) { try { $stop.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch { } }
Start-Sleep -Seconds 2
$e = Sample $proc
Start-Sleep -Seconds $IdleSeconds
$f = Sample $proc
$settleCpu = CpuPercent $e $f $IdleSeconds
Report "CPU after stopping" "$settleCpu %" ($settleCpu -lt 1.0)
Report "handles" $f.Handles ($f.Handles -lt 2500) ""
Report "threads" $f.Threads ($f.Threads -lt 60) ""

Write-Host "== repeated tab churn (leak probe) =="
$btnCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)

function Churn($rounds) {
    $before = Sample $proc
    for ($i = 0; $i -lt $rounds; $i++) {
        $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
        $newTab = $all | Where-Object { $_.Current.Name -eq "New tab" } | Select-Object -First 1
        if ($newTab) { try { $newTab.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch { } }
        Start-Sleep -Milliseconds 200
        $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
        $close = $all | Where-Object { $_.Current.Name -eq "Close tab" } | Select-Object -First 1
        if ($close) { try { $close.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() } catch { } }
        Start-Sleep -Milliseconds 200
    }
    Start-Sleep -Seconds 2
    $after = Sample $proc
    return $after.WorkingSet - $before.WorkingSet
}

$round1 = Churn 20
$round2 = Churn 20
Report "first 20 open/close tab cycles" "$( & $mb $round1) MB growth" ($round1 -lt 40MB)
# A leak grows linearly with the number of cycles; caches and GC settle. The second round must be
# clearly smaller than the first, otherwise something is being retained per tab.
Report "second 20 cycles (leak probe)" "$( & $mb $round2) MB growth" ($round2 -lt ($round1 / 2 + 8MB)) "round1=$( & $mb $round1) MB"

Report "window still responsive" "ok" (-not $proc.HasExited)
$proc.Refresh()
Report "window not hung" "ok" $proc.Responding

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Write-Host ""
if ($script:fail -eq 0) { Write-Host "== resource audit: all checks passed ==" -ForegroundColor Green }
else { Write-Host "== resource audit: $script:fail check(s) failed ==" -ForegroundColor Red; exit 1 }
