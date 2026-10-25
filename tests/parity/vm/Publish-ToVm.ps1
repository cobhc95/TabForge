# Publishes TabForge from a source checkout as a self-contained build and puts it in the test VMs at C:\TF\<Name>.
# Each call builds into its own %TEMP% folder and stages the copy inside the VM, so calls never share files.
# Two publishes to the same name cannot overlap (a lock per name), and a VM folder that a process is running from is never replaced.
# Usage: .\Publish-ToVm.ps1 -Source C:\...\TabForge (repo root of a checkout or worktree) -Name main
# Default: every running TabForge-VM* gets the build (callers rely on this). -VmName TabForge-VM2 limits it to one VM.
param([Parameter(Mandatory)] [string] $Source, [Parameter(Mandatory)] [string] $Name, [string] $VmName)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\VmCommon.ps1"
Assert-SafeName $Name
$tag = [guid]::NewGuid().ToString('N').Substring(0, 8)
$pub = "$env:TEMP\vm-pub-$Name-$tag"
$nameLock = "$env:TEMP\tf-vmdir-$Name.lock"
# A name lock older than 30 minutes is left by a publish that died (a publish takes a few minutes).
if ((Test-Path $nameLock) -and (Get-Item $nameLock).CreationTime -lt (Get-Date).AddMinutes(-30)) { Remove-Item $nameLock -Force -Recurse }
try { New-Item $nameLock -ItemType Directory -ErrorAction Stop | Out-Null } catch { throw "Another publish to C:\TF\$Name is running. Pick a new -Name (lock: $nameLock)." }
try {
    dotnet publish "$Source\Source\src\TabForge.Avalonia\TabForge.Avalonia.csproj" -c Release -r win-x64 --self-contained true -o $pub -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw "publish failed" }
    Compress-Archive -Path "$pub\*" -DestinationPath "$pub.zip" -CompressionLevel Fastest
    $cred = Import-Clixml "$env:TEMP\tabforge-vm-cred.xml"
    $vms = if ($PSBoundParameters.ContainsKey('VmName')) { @($VmName) } else { @((Get-VM -Name 'TabForge-VM*' | Where-Object State -eq 'Running').Name) }
    if (-not $vms) { throw 'No running TabForge-VM* test VM to publish to.' }
    foreach ($vm in $vms) {
        $s = New-PSSession -VMName $vm -Credential $cred
        try {
            Copy-Item "$pub.zip" -Destination "C:\TF\$Name.$tag.zip" -ToSession $s -Force
            Invoke-Command -Session $s -ArgumentList $Name, $tag -ScriptBlock {
                param($n, $t)
                $dir = "C:\TF\$n"; $zip = "C:\TF\$n.$t.zip"; $stage = "C:\TF\$n.$t"
                try {
                    $busy = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -like "$dir\*" })
                    if ($busy) { throw "C:\TF\$n is in use on this VM ($($busy[0].Name) runs from it). Publish with a new -Name, e.g. -Name $n-2." }
                    Expand-Archive $zip $stage -Force
                    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
                    Move-Item $stage $dir
                } finally { Remove-Item $zip, $stage -Recurse -Force -ErrorAction SilentlyContinue }
            } | Out-Null
            "$vm C:\TF\$Name ready"
        } finally { Remove-PSSession $s }
    }
} finally {
    Remove-Item $pub, "$pub.zip" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $nameLock -Recurse -Force -ErrorAction SilentlyContinue
}
