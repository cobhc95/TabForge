param([string]$desk, [string]$name, [Parameter(ValueFromRemainingArguments=$true)][string[]]$ops)
# Exploration helper: runs driver ops on a desktop, then captures the main window and crops the score and status bar.
$D='C:\Users\COBHC\Downloads\TabForge\work\gp5diff'
& "$D\bin\Gp5Driver.exe" do $desk @ops sleep:200 "shot:$D\shots\$name.png"
Add-Type -AssemblyName System.Drawing
$b=[Drawing.Bitmap]::FromFile("$D\shots\$name.png")
$b.Clone((New-Object Drawing.Rectangle 700,330,1300,330),$b.PixelFormat).Save("$D\shots\${name}_c.png")
$b.Clone((New-Object Drawing.Rectangle 0,($b.Height-30),900,30),$b.PixelFormat).Save("$D\shots\${name}_s.png")
$b.Dispose()
