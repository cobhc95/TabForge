$ws = New-Object -ComObject WScript.Shell
if (-not $ws.AppActivate('TabForge')) { Write-Host "ACTIVATE-FAIL"; exit 1 }
Write-Host "ACTIVATED"
Start-Sleep -Seconds 2
$ws.SendKeys('^%V')
Write-Host "KEYS-SENT"
Start-Sleep -Seconds 12
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap($b.Width, $b.Height)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
$g.Dispose()
$bmp.Save($args[0], [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "SHOT-SAVED"
