$parent = 'C:\Users\COBHC\Videos\TabForgeProofParent'
if (Test-Path $parent) { Remove-Item $parent -Recurse -Force }
New-Item -ItemType Directory -Path $parent | Out-Null
$acl = Get-Acl $parent
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule($env:USERNAME, 'CreateDirectories, Write', 'ContainerInherit, ObjectInherit', 'None', 'Deny')
$acl.AddAccessRule($rule)
Set-Acl $parent $acl
Write-Host "ACL-DENY-APPLIED"
$target = Join-Path $parent 'Sub'
try { New-Item -ItemType Directory -Path $target -ErrorAction Stop | Out-Null; Write-Host "UNEXPECTED: create succeeded" }
catch { Write-Host ("DENY-CONFIRMED: " + $_.Exception.GetType().Name + " : " + $_.Exception.Message) }
