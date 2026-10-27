$out = "$env:USERPROFILE\Downloads\TabForge\Source\tools\audit\wmi-probe.txt"
try {
  [void][System.IO.Directory]::CreateDirectory("$env:USERPROFILE\AppData\Local\TabForge\VideosWmiTest")
  $s = New-Object System.IO.FileStream("$env:USERPROFILE\AppData\Local\TabForge\VideosWmiTest\probe.tmp", [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
  $s.WriteByte(0)
  $s.Flush($true)
  $s.Dispose()
  Remove-Item "$env:USERPROFILE\AppData\Local\TabForge\VideosWmiTest\probe.tmp" -Force
  Remove-Item "$env:USERPROFILE\AppData\Local\TabForge\VideosWmiTest" -Force
  'WMI-PROBE-OK' | Out-File $out -Encoding ascii
} catch {
  ('WMI-FAIL: ' + $_.Exception.GetType().Name + ' : ' + $_.Exception.Message) | Out-File $out -Encoding ascii
}
