# Quick Essential Test Gate — LIGHT (<60s, no full build, no feature risk)
# Usage: pwsh -File Source/tools/audit/quick-essential.ps1 (workspace root = TabForge/)
# Checks: 1) git diff files only expected scopes 2) diff lines are comment/markdown-only 3) Owns counts up 4) tutorial image refs resolve
param()
$ErrorActionPreference = "Continue"
Write-Output "=== QUICK ESSENTIAL GATE ==="
$diffFiles = git diff --name-only
if (-not $diffFiles) { $diffFiles = @() }
Write-Output "Changed files: $($diffFiles.Count)"
$diffFiles | ForEach-Object { Write-Output " - $_" }
# 2) logic check: non-comment diff lines (exclude +//, +#, +<!--, +!, +[, +(, blank, +{})
$diff = git diff -U0
$nonComment = $diff | Where-Object { $_ -match '^\++[^+]' } | Where-Object {
  $_ -notmatch '^\+\s*(//|///|#|<!--|!\[|\[|\(|\)|\s*$|\{|\}|<|\*|---|\+\+\+)' -and $_ -notmatch '^\+\s*using '
}
if ($nonComment.Count -eq 0 -or $null -eq $nonComment) { Write-Output "PASS: headers/comments/markdown-only (0 logic lines)" }
else { Write-Output "WARN: $($nonComment.Count) possible logic lines:"; $nonComment | Select-Object -First 20 | ForEach-Object { Write-Output "  $_" } }
# 3) Owns counts (fast grep)
$coreTotal = (Get-ChildItem Source/src/TabForge.Core -Filter *.cs -Recurse -File | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }).Count
$coreOwns = (Select-String -Path Source/src/TabForge.Core/*.cs -Recurse -Pattern '// Owns:' -ErrorAction SilentlyContinue | Where-Object { $_.Path -notmatch '\\(obj|bin)\\' }).Count
Write-Output "Core: $coreOwns/$coreTotal with Owns"
$avTotal = (Get-ChildItem Source/src/TabForge.Avalonia -Filter *.cs -Recurse -File | Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }).Count
$avOwns = (Select-String -Path Source/src/TabForge.Avalonia/*.cs -Recurse -Pattern '// Owns:' -ErrorAction SilentlyContinue | Where-Object { $_.Path -notmatch '\\(obj|bin)\\' }).Count
Write-Output "Avalonia: $avOwns/$avTotal with Owns"
# 4) tutorial refs
$missing = @()
Select-String -Path Source/docs/tutorial/**/*.md -Pattern '\(([^)]*\.png)\)' -ErrorAction SilentlyContinue | ForEach-Object {
  $rel = $_.Matches.Groups[1].Value; if ($rel -match '^http') { return }
  $base = Split-Path $_.Path -Parent; $full = Join-Path $base ($rel -replace '/','\')
  if (-not (Test-Path $full)) { $missing += "$($_.Path):$($_.LineNumber) -> $rel" }
}
if ($missing.Count -eq 0) { Write-Output "PASS: all tutorial png refs resolve" } else { Write-Output "FAIL: $($missing.Count) broken refs:"; $missing | Select-Object -First 10 | ForEach-Object { Write-Output "  $_" } }
Write-Output "=== GATE DONE ==="
