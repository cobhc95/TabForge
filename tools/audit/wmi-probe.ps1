$out = 'C:\Users\COBHC\Downloads\TabForge\Source\tools\audit\wmi-probe.txt'
try {
  [void][System.IO.Directory]::CreateDirectory('C:\Users\COBHC\AppData\Local\TabForge\VideosWmiTest')
  $s = New-Object System.IO.FileStream('C:\Users\COBHC\AppData\Local\TabForge\VideosWmiTest\probe.tmp', [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
  $s.WriteByte(0)
  $s.Flush($true)
  $s.Dispose()
  Remove-Item 'C:\Users\COBHC\AppData\Local\TabForge\VideosWmiTest\probe.tmp' -Force
  Remove-Item 'C:\Users\COBHC\AppData\Local\TabForge\VideosWmiTest' -Force
  'WMI-PROBE-OK' | Out-File $out -Encoding ascii
} catch {
  ('WMI-FAIL: ' + $_.Exception.GetType().Name + ' : ' + $_.Exception.Message) | Out-File $out -Encoding ascii
}
