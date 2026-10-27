# Chooses the self-test areas and narrow tests for a change, prints the commands to run, and with -Run runs them.
# Usage:
#   Source\tools\Select-TestAreas.ps1                            changes since main (HEAD~1 when on main), plus working-tree changes
#   Source\tools\Select-TestAreas.ps1 -Base <ref> [-Head <ref>]  changes from Base to Head (working-tree changes only when Head is HEAD)
#   Source\tools\Select-TestAreas.ps1 [-Base <ref>] -Run         also builds the full-suite copy and runs both commands
# The path table below is the only place that maps paths to tests. Area names are checked against "## Areas" and test
# names against the AreaOf table in Source/src/TabForge.Avalonia/SelfTests/SelfTest.cs.
# The basic areas always run. Unknown paths run the basic areas only and print a warning.
# -Run builds the app with -p:TabForgeFullSuite=true into %TEMP%\tf-select-fs (incremental) under a build slot: mkdir %TEMP%\tf-slotN.lock for
# N = 1, 2, 3, retried every 20 s for up to 90 minutes; the slot directory is removed in finally.
# Exit codes with -Run: 0 all passed, 1 a test failed or there is no result line, 2 build failed or no slot in 90 minutes,
# 124 a step timed out and was killed.
param(
    [string] $Base,
    [string] $Head = 'HEAD',
    [switch] $Run,
    [int] $TimeoutSec = 600
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $repo 'Source\src\TabForge.Avalonia\TabForge.Avalonia.csproj'
$temp = $env:TEMP
$buildDir = Join-Path $temp 'tf-select-fs'
$profileDir = Join-Path $temp 'tf-select-profile'
$exe = Join-Path $buildDir 'TabForge.exe'
$areasLog = Join-Path $temp 'tf-select-areas.log'
$onlyLog = Join-Path $temp 'tf-select-only.log'

# Path rules, first match decides nothing: every matching rule adds its areas and tests.
# Match: regular expression on the repository-relative path with forward slashes.
# Areas: the values of the AreaOf tables. Tests: the names registered with Guard or GuardGroup.
$Rules = @(
    @{ Name = 'learn'; Match = '^Source/src/TabForge\.Core/KeyboardMode/'; Areas = @('ui'); Tests = @('TestKeyboardModeNoteStream') },
    @{ Name = 'video'; Match = '^Source/src/TabForge\.Core/Services/Video/|^Source/src/TabForge\.Avalonia/(Video/|Views/VideoExportWindow\.cs$)'; Areas = @('recording'); Tests = @('TestVideoExport', 'TestVideoEncoderGolden', 'TestVideoFrameGolden', 'TestVideoFrameSourceGolden', 'TestVideoEncoderMp4', 'TestLiveVideoRecord', 'TestVideoAudioPitch') },
    @{ Name = 'engine'; Match = '^Source/src/TabForge\.(AudioEngine|Audio\.Contracts)/'; Areas = @('engine', 'recording', 'playback'); Tests = @() },
    @{ Name = 'technique'; Match = '^Source/src/TabForge\.Core/Models/Technique|^Source/src/TabForge\.Core/Playback/[^/]*Compiler[^/]*\.cs$'; Areas = @('notation', 'guitarpro', 'playback'); Tests = @('TestTechniqueEngraving', 'TestTimelineTechniques', 'TestTechniqueCoverage', 'TestTechniqueInfoTable', 'TestTechniqueGpExportGolden', 'TestTechniqueMusicXmlGolden', 'TestTechniquePlaybackGolden') },
    @{ Name = 'settings'; Match = '^Source/src/TabForge\.Core/Services/Settings/'; Areas = @('settings'); Tests = @('TestPreferencesCatalog', 'TestSettingsStoreSharedAcrossWindows', 'TestHotkeySettingsMigration') },
    @{ Name = 'menus'; Match = '/MainMenu/|[^/]*Menus?[^/]*\.cs$|^Source/tests/full-suite/Menus/'; Areas = @('ui'); Tests = @('TestMainMenuTreeGolden', 'TestMainMenuTable') },
    @{ Name = 'features'; Match = '^Source/src/TabForge\.Core/Services/Features/'; Areas = @('ui'); Tests = @('TestFeatureModuleContributions') },
    @{ Name = 'docking'; Match = '^Source/src/TabForge\.Core/Docking/'; Areas = @('ui'); Tests = @('TestDockPaneTable') },
    @{ Name = 'diagnostics'; Match = '^Source/src/TabForge\.Core/Diagnostics/'; Areas = @('ui'); Tests = @() },
    @{ Name = 'playback'; Match = '^Source/src/TabForge\.Core/Playback/'; Areas = @('playback'); Tests = @() },
    @{ Name = 'window-and-views'; Match = '^Source/src/TabForge\.Avalonia/MainWindow/|^Source/src/TabForge\.Avalonia/Views/'; Areas = @('ui', 'interactions'); Tests = @() }
)
# Documents and build files: no tests of their own (hygiene and architecture, in the basic areas, check them).
# Golden text files under tests/ are test inputs, so they take the rules above.
$DocPattern = '\.(md|txt)$'
$BuildPattern = '^(Source/tools/|Source/installer/|\.github/)|^Source/[^/]*\.(props|sln)$|^Source/tools/[^/]*\.cmd$|^Source/src/TabForge\.Avalonia/ArchitectureBudget\.json$'
# Test sources: the tests they define are selected by name (the names must be registered).
$TestFilePattern = '^Source/src/TabForge\.Avalonia/SelfTests/.*\.cs$'

# Reads the area and test names from the registry source: the AreaOf table (SelfTest.cs) and the Guard / GuardGroup calls.
# A test with no AreaOf entry is in area core, as TestRegistryReader reports it.
$registrySource = [IO.File]::ReadAllText((Join-Path $repo 'Source\src\TabForge.Avalonia\SelfTests\SelfTest.cs'))
$knownAreas = @{}
$testArea = @{}
foreach ($m in [regex]::Matches($registrySource, '\["(Test\w+)"\]\s*=\s*"([^"]+)"')) { $testArea[$m.Groups[1].Value] = $m.Groups[2].Value; $knownAreas[$m.Groups[2].Value] = $true }
foreach ($m in [regex]::Matches($registrySource, 'Guard(?:Group)?\(\s*(?:"[^"]+"\s*,\s*)?(Test\w+)')) { if (-not $testArea.ContainsKey($m.Groups[1].Value)) { $testArea[$m.Groups[1].Value] = 'core' } }

$warnings = New-Object System.Collections.Generic.List[string]
foreach ($r in $Rules) {
    foreach ($a in $r.Areas) { if (-not $knownAreas.ContainsKey($a)) { $warnings.Add("rule $($r.Name): area '$a' has no AreaOf entry") } }
    foreach ($t in $r.Tests) { if (-not $testArea.ContainsKey($t)) { $warnings.Add("rule $($r.Name): test $t is not registered") } }
}

function Get-GitLines {
    param([string[]] $GitArgs)
    $out = & git -C $repo @GitArgs
    if ($LASTEXITCODE -ne 0) { throw "git $($GitArgs -join ' ') failed (exit $LASTEXITCODE)" }
    return @($out | Where-Object { $_ -and $_.Trim() } | ForEach-Object { $_.Trim().Replace('\', '/') })
}

if (-not $Base) {
    $branch = Get-GitLines @('rev-parse', '--abbrev-ref', 'HEAD') | Select-Object -First 1
    if ($branch -eq 'main') { $Base = 'HEAD~1' } else { $Base = 'main' }
}

$paths = @(Get-GitLines @('diff', '--name-only', "${Base}...${Head}"))
$worktreeIncluded = ($Head -eq 'HEAD')
if ($worktreeIncluded) {
    $paths += @(Get-GitLines @('diff', '--name-only', 'HEAD'))
    $paths += @(Get-GitLines @('ls-files', '--others', '--exclude-standard'))
}
$paths = @($paths | Where-Object { $_ -notlike '.claude/*' } | Select-Object -Unique)

$areaSet = @('basic')
$testSet = @()
$pathLines = @()
foreach ($p in $paths) {
    if (($p -match '\.md$') -or (($p -match $DocPattern) -and ($p -notlike 'Source/tests/*'))) { $pathLines += "  docs     $p"; continue }
    if ($p -match $BuildPattern) { $pathLines += "  build    $p"; continue }
    $matched = $false
    foreach ($r in $Rules) {
        if ($p -match $r.Match) {
            $matched = $true
            $pathLines += "  rule     $($r.Name)  $p"
            foreach ($a in $r.Areas) { if ($knownAreas.ContainsKey($a) -and $areaSet -notcontains $a) { $areaSet += $a } }
            foreach ($t in $r.Tests) { if ($testArea.ContainsKey($t) -and $testSet -notcontains $t) { $testSet += $t } }
        }
    }
    if ($p -match $TestFilePattern) {
        $file = Join-Path $repo ($p -replace '/', '\')
        $names = @()
        if (Test-Path -LiteralPath $file) {
            foreach ($m in [regex]::Matches([IO.File]::ReadAllText($file), 'void\s+(Test\w+)\s*\(')) { $names += $m.Groups[1].Value }
        }
        $known = @($names | Where-Object { $testArea.ContainsKey($_) } | Select-Object -Unique)
        if ($known.Count -gt 0) {
            $matched = $true
            $pathLines += "  tests    $($known.Count) test(s) defined in $p"
            foreach ($t in $known) { if ($testSet -notcontains $t) { $testSet += $t } }
        } else {
            $matched = $true
            $pathLines += "  tests    none registered in $p"
            $warnings.Add("no registered test is defined in $p; basic areas only for it")
        }
    }
    if (-not $matched) {
        $pathLines += "  UNKNOWN  $p"
        $warnings.Add("no rule for $p; basic areas only (add a row to the path table in Source/tools/Select-TestAreas.ps1 if it changes tests)")
    }
}

$areaArg = $areaSet -join ','
$exeQ = '"' + $exe + '"'
$profileArg = '--profile "' + $profileDir + '"'
$buildArgs = 'build "' + $project + '" -c Release -v q -p:TabForgeFullSuite=true -o "' + $buildDir + '"'
$cmdBuild = 'dotnet ' + $buildArgs
$cmdAreas = $exeQ + ' ' + $profileArg + ' --selftest "' + $areasLog + '" --areas ' + $areaArg
$cmdOnly = $exeQ + ' ' + $profileArg + ' --selftest "' + $onlyLog + '" --only ' + ($testSet -join ',')

Write-Host "Base: $Base   Head: $Head   working tree: $(if ($worktreeIncluded) { 'included' } else { 'not included' })"
Write-Host "Changed paths: $($paths.Count)"
foreach ($l in $pathLines) { Write-Host $l }
Write-Host ''
Write-Host "Areas (--areas): $areaArg"
if ($testSet.Count -gt 0) { Write-Host "Narrow tests (--only): $($testSet -join ',')" } else { Write-Host 'Narrow tests (--only): none' }
foreach ($w in $warnings) { Write-Host "WARNING: $w" }
Write-Host ''
Write-Host 'Commands (the full-suite build is a superset of the normal build):'
Write-Host "  $cmdBuild"
Write-Host "  $cmdAreas"
if ($testSet.Count -gt 0) { Write-Host "  $cmdOnly" }

if (-not $Run) { exit 0 }

function Invoke-Timed {
    param([string] $File, [string] $Arguments, [int] $Seconds, [string] $StdOut, [string] $StdErr)
    $p = if ($StdOut) {
        Start-Process -FilePath $File -ArgumentList $Arguments -PassThru -NoNewWindow -RedirectStandardOutput $StdOut -RedirectStandardError $StdErr
    } else {
        Start-Process -FilePath $File -ArgumentList $Arguments -PassThru -NoNewWindow
    }
    # Reading the handle once makes ExitCode valid after a timed WaitForExit in Windows PowerShell 5.1.
    $null = $p.Handle
    if (-not $p.WaitForExit($Seconds * 1000)) {
        & taskkill.exe /PID $p.Id /T /F | Out-Null
        return 124
    }
    return $p.ExitCode
}

function Take-BuildSlot {
    $deadline = (Get-Date).AddMinutes(90)
    while ($true) {
        foreach ($n in 1..3) {
            $slot = Join-Path $temp "tf-slot$n.lock"
            try {
                New-Item -ItemType Directory -Path $slot -ErrorAction Stop | Out-Null
                return $slot
            } catch {
                # Taken by another job: try the next slot.
            }
        }
        if ((Get-Date) -gt $deadline) { throw 'No build slot (tf-slot1..3.lock) was free for 90 minutes.' }
        Start-Sleep -Seconds 20
    }
}

try { $slot = Take-BuildSlot } catch { Write-Host $_; exit 2 }
$worst = 0
$totalPassed = 0
$totalFailed = 0
try {
    New-Item -ItemType Directory -Force -Path $profileDir | Out-Null
    $buildOut = Join-Path $temp 'tf-select-build.out.log'
    $buildErr = Join-Path $temp 'tf-select-build.err.log'
    $code = Invoke-Timed -File 'dotnet' -Arguments $buildArgs -Seconds $TimeoutSec -StdOut $buildOut -StdErr $buildErr
    if ($code -ne 0) {
        Write-Host "Build failed (exit $code). Output: $buildOut ; errors: $buildErr"
        $worst = $(if ($code -eq 124) { 124 } else { 2 })
    } else {
        $steps = @([pscustomobject]@{ Log = $areasLog; Selector = '--areas ' + $areaArg })
        if ($testSet.Count -gt 0) { $steps += [pscustomobject]@{ Log = $onlyLog; Selector = '--only ' + ($testSet -join ',') } }
        foreach ($step in $steps) {
            if (Test-Path -LiteralPath $step.Log) { Remove-Item -LiteralPath $step.Log -Force }
            $runArgs = '--profile "' + $profileDir + '" --selftest "' + $step.Log + '" ' + $step.Selector
            $code = Invoke-Timed -File $exe -Arguments $runArgs -Seconds $TimeoutSec
            if ($code -eq 124) { Write-Host "Timed out after $TimeoutSec s and killed: $($step.Selector)"; $worst = [Math]::Max($worst, 124); continue }
            $hit = $null
            if (Test-Path -LiteralPath $step.Log) { $hit = Select-String -LiteralPath $step.Log -Pattern 'TabForge self-test: \d+ passed, \d+ failed' | Select-Object -Last 1 }
            if (-not $hit) { Write-Host "No result line in $($step.Log) (exit $code). Read the log."; $worst = [Math]::Max($worst, 1); continue }
            $summary = $hit.Line.Trim()
            Write-Host "$($step.Selector): $summary"
            if ($summary -match '(\d+) passed, (\d+) failed') {
                $totalPassed += [int]$Matches[1]
                $totalFailed += [int]$Matches[2]
                if ([int]$Matches[2] -gt 0) { $worst = [Math]::Max($worst, 1) }
            }
            Select-String -LiteralPath $step.Log -CaseSensitive -Pattern '^FAIL\b' | Select-Object -First 20 | ForEach-Object { Write-Host $_.Line.Trim() }
        }
        Write-Host "Total: $totalPassed passed, $totalFailed failed"
    }
} finally {
    Remove-Item -LiteralPath $slot -Recurse -Force -ErrorAction SilentlyContinue
}
exit $worst
