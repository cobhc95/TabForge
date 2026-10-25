# Runs the parity capture script on the old and the new build inside one test VM (same virtual GPU, resolution and pipeline
# for both sides), copies both shot folders back and triages them (big picture first).
# Usage: .\Run-AB.ps1 [-Old C:\TF\old] [-New C:\TF\main] [-Out $env:TEMP\vm-ab] [-Script <capture json>] [-VmName TabForge-VM2]
# Prints the "VM: x" and "exit: n" lines of each run, then the triage summary ("AB: n shots, m identical, k different").
# Each run writes into its own folder <Out>\<tag>, so runs that share -Out never delete each other's shots.
# The VM's profile and output folders are deleted only after their shots are copied back; the copied capture script is deleted here.
param([string] $Old = 'C:\TF\old', [string] $New = 'C:\TF\main', [string] $Out = "$env:TEMP\vm-ab", [string] $Script = "$PSScriptRoot\..\parity.capture.json", [string] $VmName)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\VmCommon.ps1"
Assert-PlainArg 'Old' $Old
Assert-PlainArg 'New' $New
$vm = if ($VmName) { $VmName } else { $null }  # -VmName pins both sides to one VM
$tag = [guid]::NewGuid().ToString('N').Substring(0, 6)  # this A/B's own folders and script name inside the VM
$runOut = Join-Path $Out $tag  # this run's local folder: both side outputs and the triage go here
$hostScript = "$env:TEMP\ab-$tag.json"
try {
    foreach ($side in @(@{ Name = 'A'; Exe = $Old }, @{ Name = 'B'; Exe = $New })) {
        $n = $side.Name
        $exe = "$($side.Exe)\TabForge.exe"
        $runArgs = @{
            CopyIn = @(); TimeoutSeconds = 1200
            Command = ('rmdir /s /q "C:\TF\p{0}-{1}" & rmdir /s /q "C:\TF\out{0}-{1}" & "{2}" --profile "C:\TF\p{0}-{1}" --capture "C:\TF\ab-{1}.json" "C:\TF\out{0}-{1}"' -f $n, $tag, $exe)
            OutDir = "C:\TF\out$n-$tag"; LocalOut = $runOut
            CleanupInVm = @("C:\TF\p$n-$tag", "C:\TF\out$n-$tag")
        }
        Copy-Item (Resolve-Path $Script).Path $hostScript -Force
        $runArgs.CopyIn = @($hostScript)
        if ($vm) { $runArgs.VmName = $vm }
        $result = & "$PSScriptRoot\Invoke-InVm.ps1" @runArgs
        $result
        if (-not $vm) { $vm = (($result | Where-Object { $_ -like 'VM: *' }) -replace '^VM: ', '') | Select-Object -First 1 }
    }
} finally {
    Remove-Item $hostScript -Force -ErrorAction SilentlyContinue
}
$ErrorActionPreference = 'Continue'
py -I -W ignore "$PSScriptRoot\triage.py" "$runOut\outA-$tag" "$runOut\outB-$tag" "$runOut\triage"
