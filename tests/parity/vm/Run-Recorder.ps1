# Runs the FlaUI ParityRecorder inside the test VM (never on the owner's PC) and copies its output back.
# The recorder is published self-contained with the demo song (samples\) and a wrapper that reattaches the VM's
# signed-in session to the console first (tscon), so real mouse and keyboard input reaches the desktop.
# Usage: .\Run-Recorder.ps1 -Name ab1 -RecorderArgs '--ab C:\TF\old\TabForge.exe C:\TF\main\TabForge.exe --out C:\TF\rec-out\ab1'
#        .\Run-Recorder.ps1 -Name chk1 -RecorderArgs '--exe C:\TF\main\TabForge.exe --check --baseline C:\TF\rec\baseline' -Baseline <folder>
# Output: %TEMP%\vm-rec-<Name> (run.log holds the recorder's console output: the AB or BASELINE line is last).
param(
    [Parameter(Mandatory)] [string] $Name,
    [Parameter(Mandatory)] [string] $RecorderArgs,
    [string] $Baseline,
    [string] $VmName,
    [int] $TimeoutSeconds = 2700
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\VmCommon.ps1"
Assert-SafeName $Name
Assert-PlainArg 'RecorderArgs' $RecorderArgs
$root = Split-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot)))
$tag = [guid]::NewGuid().ToString('N').Substring(0, 8)
$pub = "$env:TEMP\tf-rec-pub-$tag"; $zip = "$env:TEMP\tf-rec-$tag.zip"
try {
    dotnet publish "$root\Source\tests\parity\flaui\ParityRecorder\ParityRecorder.csproj" -c Release -r win-x64 --self-contained true -o $pub -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw 'recorder publish failed' }
    New-Item "$pub\samples" -ItemType Directory -Force | Out-Null
    Copy-Item "$root\samples\*Ashen Meridian*.gp" "$pub\samples\"
    if ($Baseline) { Copy-Item $Baseline "$pub\baseline" -Recurse }
    Set-Content "$pub\vm-run.cmd" -Encoding ascii -Value @'
@echo off
query user | find "Disc" >nul && tscon 1 /dest:console
cd /d C:\TF\rec
C:\TF\rec\ParityRecorder.exe %*
'@
    Compress-Archive -Path "$pub\*" -DestinationPath $zip -CompressionLevel Fastest
    $out = "C:\TF\rec-out\$Name"
    $cmd = "powershell -NoProfile -Command `"Remove-Item -Recurse -Force C:\TF\rec,$out -ErrorAction SilentlyContinue; New-Item -ItemType Directory -Force $out | Out-Null; Expand-Archive -Force C:\TF\tf-rec-$tag.zip C:\TF\rec`" && C:\TF\rec\vm-run.cmd $RecorderArgs > $out\recorder.log 2>&1"
    # The zip is copied into C:\TF and deleted there by Invoke-InVm after the run.
    & "$PSScriptRoot\Invoke-InVm.ps1" -Command $cmd -CopyIn $zip -OutDir $out -LocalOut "$env:TEMP\vm-rec-$Name" -TimeoutSeconds $TimeoutSeconds -VmName $VmName
} finally {
    Remove-Item $pub, $zip -Recurse -Force -ErrorAction SilentlyContinue
}
