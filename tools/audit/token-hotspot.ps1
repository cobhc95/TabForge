# TabForge Token-Hotspot Auditor — READ-ONLY, no source edits
# Usage: pwsh -File Source/tools/audit/token-hotspot.ps1 -Root Source/src/TabForge.Core -Top 20
param([string]$Root = "Source/src", [int]$Top = 20)
$ErrorActionPreference = "Stop"
$files = Get-ChildItem -Path $Root -Filter *.cs -Recurse -File -ErrorAction SilentlyContinue |
  Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }
$rows = foreach ($f in $files) {
  $lines = (Get-Content $f.FullName | Measure-Object -Line).Lines
  $text = Get-Content $f.FullName -Raw
  $publics = ([regex]::Matches($text, '\bpublic\b')).Count
  $usings = ([regex]::Matches($text, '(?m)^\s*using\s')).Count
  $owns = if ($text -match '//\s*Owns:') { 'yes' } else { 'no' }
  [pscustomobject]@{
    Path = $f.FullName.Replace((Get-Location).Path + '\','')
    Lines = $lines; Bytes = $f.Length; Publics = $publics; Usings = $usings; Owns = $owns
    Cost = [math]::Round($f.Length / 1KB + $publics * 0.5 + $usings * 0.3, 1)
  }
}
$rows | Sort-Object Cost -Descending | Select-Object -First $Top |
  Format-Table Path, Lines, Bytes, Publics, Usings, Owns, Cost -AutoSize | Out-String -Width 220 | Write-Output
# CSV for team merge:
$rows | Sort-Object Cost -Descending | Select-Object -First $Top |
  Export-Csv -NoTypeInformation -Path "$Root-hotspot.csv"
Write-Output "CSV -> $Root-hotspot.csv"
