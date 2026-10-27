$dll = "$env:USERPROFILE\Downloads\TabForge\src\TabForge.Avalonia\bin\Release\net8.0-windows\TabForge.Core.dll"
Add-Type -Path $dll
$fp = [TabForge.Services.FilePathPolicy]
$denied = "$env:USERPROFILE\Videos\TabForgeProofParent\Sub"
Write-Host "--- Validate ---"
try { $v = $fp::ValidateWritableFolder($denied, "video folder"); Write-Host ("VALIDATE-OK: " + $v) }
catch { Write-Host ("VALIDATE-THREW: " + $_.Exception.GetType().Name + " : " + $_.Exception.Message) }
Write-Host "--- Probe ---"
$ok = $fp::TryProbeWritableFolder($denied, [ref]$err)
Write-Host ("PROBE-RESULT: " + $ok)
if ($err.Value) { Write-Host ("PROBE-ERR: " + $err.Value.GetType().Name + " : " + $err.Value.Message + " HResult=0x" + $err.Value.HResult.ToString("X8")); Write-Host ("ISACCESSDENIED: " + $fp::IsAccessDenied($err.Value)) }
Write-Host "--- Fallback ---"
$fb = $fp::FallbackWritableFolder("Videos")
if ($fb -eq $null) { Write-Host "FALLBACK: <NULL>" } else { Write-Host ("FALLBACK: " + $fb) }
Write-Host "--- Create (raw, like controller L91) ---"
try { [System.IO.Directory]::CreateDirectory($denied) | Out-Null; Write-Host "CREATE-OK" }
catch { Write-Host ("CREATE-THREW: " + $_.Exception.GetType().Name + " : " + $_.Exception.Message) }
