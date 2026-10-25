<#
.SYNOPSIS
  Builds TabForge, runs every capture script in tools/capture and writes the PNG/GIF shots plus an index.html contact sheet.
.PARAMETER Out
  Output folder (default: Docs\Release-draft\images-test beside the Source folder). Files are written under it with the names the scripts give
  (images/..., docs/screenshots/..., docs/animations/...).
.PARAMETER ArtifactsPath
  Optional build output root (passed to dotnet build as -p:ArtifactsPath, quote it when it has spaces); default is the repository build folder.
.PARAMETER Scripts
  Optional script file names to run (default: every *.json in tools/capture).
.EXAMPLE
  powershell -File tools\capture.ps1 -Out C:\temp\shots
#>
param(
    [string]$Out,
    [string]$ArtifactsPath,
    [string[]]$Scripts
)
$ErrorActionPreference = 'Stop'
$source = Split-Path -Parent $PSScriptRoot
if (-not $Out) { $Out = Join-Path (Split-Path -Parent $source) 'Docs\Release-draft\images-test' }
New-Item -ItemType Directory -Force -Path $Out | Out-Null

$build = @('build', (Join-Path $source 'src\TabForge.Avalonia'))
if ($ArtifactsPath) { $build += "-p:ArtifactsPath=$ArtifactsPath" }
& dotnet @build
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
$root = if ($ArtifactsPath) { $ArtifactsPath } else { Join-Path (Split-Path -Parent $source) 'build' }
$exe = Join-Path $root 'bin\TabForge.Avalonia\debug\TabForge.exe'
if (-not (Test-Path $exe)) { throw "TabForge.exe not found at $exe" }

$files = Get-ChildItem (Join-Path $PSScriptRoot 'capture') -Filter *.json | Where-Object { -not $Scripts -or $Scripts -contains $_.Name }
$failed = 0
foreach ($script in $files) {
    Write-Host "capture: $($script.Name)"
    $p = Start-Process -FilePath $exe -ArgumentList @('--capture', "`"$($script.FullName)`"", '--out', "`"$Out`"") -PassThru -Wait
    if ($p.ExitCode -ne 0) { $failed++; Write-Warning "$($script.Name) exit code $($p.ExitCode); see $Out\$($script.BaseName).log" }
}

# Contact sheet: every shot of every report with its name, size and theme.
$rows = foreach ($report in Get-ChildItem $Out -Filter *.report.json) {
    $json = Get-Content $report.FullName -Raw | ConvertFrom-Json
    foreach ($shot in $json.shots) { [pscustomobject]@{ Name = $shot.Id; File = $shot.File; Size = "$($shot.Width) x $($shot.Height)"; Theme = $shot.Theme; Script = $json.script } }
}
$cells = $rows | Sort-Object Name | ForEach-Object {
    "<figure><a href=`"$($_.File)`"><img src=`"$($_.File)`" loading=`"lazy`"></a><figcaption><b>$($_.Name)</b><br>$($_.Size) &middot; $($_.Theme) &middot; $($_.Script)</figcaption></figure>"
}
$html = "<!doctype html><meta charset=utf-8><title>TabForge capture contact sheet</title><style>body{background:#1b1d22;color:#ddd;font:13px Segoe UI}figure{display:inline-block;width:320px;margin:8px;vertical-align:top}img{max-width:320px;max-height:240px;background:#000}</style><h1>Capture contact sheet ($(@($rows).Count) shots)</h1>" + ($cells -join "`n")
Set-Content -Path (Join-Path $Out 'index.html') -Value $html -Encoding UTF8
Write-Host "done: $(@($rows).Count) shots, $failed script(s) with failures; $Out\index.html"
