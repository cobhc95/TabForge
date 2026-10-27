# Runs the full self-test suite as parallel process lanes and merges the result (Source/docs/guides/TESTING.md, "Sharded full suite").
# Usage: Source\tools\run-suite.ps1 [-Lanes L0,P3] [-DryRun] [-Quick] [-MaxParallel 4] [-Timeout 5]
#
# Builds the app once into %TEMP%\tf-suite-build, then starts child TabForge.exe processes, each with its own
# --profile %TEMP%\tf-suite-<job>, log, --timing csv and --group-report json, working directory = the repository root.
#   Phase 1  L0 alone, first: window-lifetime group + basic set + core tests (shard 0), then the single-instance hand-over.
#   Phase 2  P1, P2, P3, W and the monkey shards in parallel (at most -MaxParallel children at once).
#   Phase 3  R (recording, audio and benchmarks) alone, last: real-time audio is load-sensitive, so no other child runs.
# Afterwards the group reports are merged (--merge-group-reports ... --require ci,document-context) and one TOTAL line is printed.
# The whole run holds ONE build slot (%TEMP%\tf-slot1..3.lock); parallel children need the machine to themselves.
# Exit codes: 0 all lanes passed and the merged gate holds; 1 a lane failed or timed out, or the gate failed; 2 build or layout error.
# Every area in the AreaOf tables must be in exactly one lane; a new area fails the run at start until it is placed below.
# TABFORGE_MONKEY_FULL=1: 20 monkey seeds in 4 shards of 5; otherwise 8 seeds in 2 shards of 4 (the serial runner always runs 20).
param(
    [string[]] $Lanes,
    [switch] $DryRun,
    [switch] $Quick,
    [int] $MaxParallel = 0,
    [int] $Timeout = 5
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$temp = $env:TEMP
$buildDir = Join-Path $temp 'tf-suite-build'
$exe = Join-Path $buildDir 'TabForge.exe'
$project = Join-Path $repo 'Source\src\TabForge.Avalonia\TabForge.Avalonia.csproj'
if ($MaxParallel -le 0) { $MaxParallel = [Math]::Max(1, [Math]::Min(4, [int][Math]::Floor([Environment]::ProcessorCount / 3))) }
if ($Lanes) { $Lanes = @($Lanes | ForEach-Object { $_ -split ',' } | Where-Object { $_ } | ForEach-Object { $_.Trim().ToUpperInvariant() }) }

# ---- lane layout: Name, Lane (what -Lanes selects), Phase, Args (after the common options) ----
$full = $env:TABFORGE_MONKEY_FULL -eq '1'
$monkeyTotal = if ($full) { 20 } else { 8 }
$monkeyShards = if ($full) { 4 } else { 2 }
$per = $monkeyTotal / $monkeyShards

# NOTE (cleanup 2026-10-19): Source\tests\full-suite\SelfTestFullSuite.cs was removed in Batch 2 (0 files on disk); registry currently resolves via SelfTest.cs until the reference is restored. See Source/STALE_CONFIG_REPORT.md section 3. No files deleted.
# Areas and tests come from the registry sources (the AreaOf tables in SelfTest.cs and the full-suite registration).
$registrySource = (@('Source\src\TabForge.Avalonia\SelfTests\SelfTest.cs', 'Source\tests\full-suite\SelfTestFullSuite.cs') | ForEach-Object { [IO.File]::ReadAllText((Join-Path $repo $_)) }) -join "`n"
$areaOf = @{}
foreach ($m in [regex]::Matches($registrySource, '\["(Test\w+)"\]\s*=\s*"([^"]+)"')) { $areaOf[$m.Groups[1].Value] = $m.Groups[2].Value }
$mdAreas = @($areaOf.Values | Sort-Object -Unique)
# The workflow area minus the monkey: listed by name so the monkey shards own the monkey.
$wfTests = @($areaOf.GetEnumerator() | Where-Object { $_.Value -eq 'workflow' } | ForEach-Object { $_.Key } | Sort-Object)

# Areas each lane covers (the coverage self-check below). core is covered by L0 (--core-shard 0); other lanes pass --core-shard 1.
$jobs = @(
    @{ Name = 'L0a'; Lane = 'L0'; Phase = 1; Areas = @('window-lifetime', 'architecture', 'hygiene', 'smoke', 'essential'); Args = '--areas window-lifetime,basic,essential --core-shard 0' }
    @{ Name = 'L0b'; Lane = 'L0'; Phase = 1; Areas = @('release'); Args = '--areas release --only TestSingleInstanceProcessHandover --core-shard 1' }
    @{ Name = 'P1'; Lane = 'P1'; Phase = 2; Areas = @('engine', 'playback', 'ui'); Args = '--areas engine,playback,ui --core-shard 1' }
    @{ Name = 'P2'; Lane = 'P2'; Phase = 2; Areas = @('guitarpro', 'interactions', 'document-operations', 'persistence', 'document-context', 'synthetic'); Args = '--areas guitarpro,interactions,document-operations,persistence,document-context,synthetic --core-shard 1' }
    @{ Name = 'P3'; Lane = 'P3'; Phase = 2; Areas = @('settings', 'notation', 'midi', 'leaks', 'fuzz', 'tutorial'); Args = '--areas settings,notation,midi,leaks,fuzz,tutorial --core-shard 1' }
    @{ Name = 'W'; Lane = 'W'; Phase = 2; Areas = @('workflow'); Args = '' }
)
$jobs[-1].Args = '--areas workflow --only ' + ((@($wfTests | Where-Object { $_ -ne 'TestWorkflowMonkey' })) -join ',') + ' --core-shard 1'
for ($m = 1; $m -le $monkeyShards; $m++) {
    $first = ($m - 1) * $per + 1; $last = $m * $per
    $jobs += @{ Name = "M$m"; Lane = "M$m"; Phase = 2; Monkey = $true; Areas = @(); Args = "--only TestWorkflowMonkey --monkey-first $first --monkey-seeds $last --core-shard 1" }
}
$jobs += @{ Name = 'R'; Lane = 'R'; Phase = 3; Areas = @('recording', 'audioaudit', 'bench'); Args = '--areas recording,audioaudit,bench --core-shard 1' }

# ---- coverage self-check: every area of the AreaOf tables in exactly one lane ----
$bad = @()
foreach ($a in $mdAreas) {
    if ($a -eq 'core') { continue }
    $n = @($jobs | Where-Object { $_.Areas -contains $a }).Count
    if ($n -ne 1) { $bad += "area '$a' is in $n lanes (must be exactly 1)" }
}
foreach ($j in $jobs) { foreach ($a in $j.Areas) { if ($mdAreas -notcontains $a) { $bad += "lane $($j.Name) names area '$a' that is not in the AreaOf tables" } } }
if ($wfTests -notcontains 'TestWorkflowMonkey') { $bad += 'TestWorkflowMonkey is not in the workflow area (AreaOf)' }
if ($bad.Count -gt 0) { $bad | ForEach-Object { Write-Host "COVERAGE ERROR: $_" }; Write-Host 'Fix the lane table in tools/run-suite.ps1.'; exit 2 }

$selected = @($jobs | Where-Object {
        ($null -eq $Lanes -or $Lanes -contains $_.Lane) -and -not ($Quick -and ($_.Lane -eq 'R' -or $_.Monkey))
    })
$subset = ($selected.Count -ne $jobs.Count)
if ($selected.Count -eq 0) { Write-Host 'No lane selected.'; exit 2 }

function Get-ChildArgs($j) {
    $n = $j.Name
    return ('--profile "{0}\tf-suite-{1}" --selftest "{0}\tf-suite-{1}.log" --timing "{0}\tf-suite-{1}.csv" --group-report "{0}\tf-suite-{1}.json" {2}' -f $temp, $n, $j.Args)
}

Write-Host ("Lane table ({0} areas in AreaOf, each in exactly one lane; core runs in L0a via --core-shard 0): MaxParallel {1}, timeout {2} min, monkey {3} seeds in {4} shards" -f $mdAreas.Count, $MaxParallel, $Timeout, $monkeyTotal, $monkeyShards)
foreach ($j in $jobs) {
    $mark = if ($selected -contains $j) { '*' } else { ' ' }
    Write-Host ("{0} phase {1} {2,-4} {3}" -f $mark, $j.Phase, $j.Name, (Get-ChildArgs $j))
}
if ($DryRun) { Write-Host "Dry run: working directory for every child is $repo ; exe $exe ; nothing was built or started."; exit 0 }

# ---- build slot ----
$slot = $null
function Get-Slot {
    $deadline = (Get-Date).AddMinutes(90)
    while ($true) {
        foreach ($i in 1..3) {
            $p = Join-Path $temp "tf-slot$i.lock"
            try { New-Item -ItemType Directory -Path $p -ErrorAction Stop | Out-Null; return $p } catch { }
        }
        if ((Get-Date) -gt $deadline) { throw 'Gave up after 90 minutes waiting for a build slot (%TEMP%\tf-slot1..3.lock).' }
        Start-Sleep -Seconds 20
    }
}

# Starts a process, kills its tree on timeout. The Handle read gives a valid ExitCode after WaitForExit(ms) in Windows PowerShell 5.1.
function Start-Child([string] $File, [string] $Arguments, [string] $StdOut, [string] $StdErr) {
    $a = @{ FilePath = $File; ArgumentList = $Arguments; PassThru = $true; WorkingDirectory = $repo; WindowStyle = 'Hidden' }
    if ($StdOut) { $a.RedirectStandardOutput = $StdOut; $a.RedirectStandardError = $StdErr }
    $p = Start-Process @a
    $null = $p.Handle
    return $p
}

function Stop-Tree($p) { & taskkill.exe /PID $p.Id /T /F 2>&1 | Out-Null }

function Get-Result([string] $log) {
    $r = @{ Pass = 0; Fail = 0; Skip = 0; Has = $false }
    if (Test-Path -LiteralPath $log) {
        $m = Select-String -LiteralPath $log -Pattern '^TabForge self-test: (\d+) passed, (\d+) failed(?:, (\d+) skipped)?' | Select-Object -Last 1
        if ($m) {
            $r.Has = $true; $r.Pass = [int]$m.Matches[0].Groups[1].Value; $r.Fail = [int]$m.Matches[0].Groups[2].Value
            if ($m.Matches[0].Groups[3].Success) { $r.Skip = [int]$m.Matches[0].Groups[3].Value }
        }
    }
    return $r
}

$wall = [Diagnostics.Stopwatch]::StartNew()
$slot = Get-Slot
$exit = 0
try {
    Write-Host "Build slot: $slot"
    $bo = Join-Path $temp 'tf-suite-build.out.log'; $be = Join-Path $temp 'tf-suite-build.err.log'
    $b = Start-Child 'dotnet' ('build "' + $project + '" -c Release -v q -o "' + $buildDir + '"') $bo $be
    if (-not $b.WaitForExit(30 * 60 * 1000)) { Stop-Tree $b; Write-Host 'Build timed out.'; exit 2 }
    if ($b.ExitCode -ne 0) { Write-Host "Build failed (exit $($b.ExitCode)). See $bo and $be"; exit 2 }

    foreach ($j in $selected) { foreach ($e in 'log', 'csv', 'json') { Remove-Item -LiteralPath (Join-Path $temp "tf-suite-$($j.Name).$e") -Force -ErrorAction SilentlyContinue } }
    $done = @{}   # Name -> @{ Code; Secs }

    foreach ($phase in 1..3) {
        $queue = New-Object System.Collections.Queue
        $selected | Where-Object { $_.Phase -eq $phase } | ForEach-Object { $queue.Enqueue($_) }
        $limit = if ($phase -eq 2) { $MaxParallel } else { 1 }   # phases 1 and 3 run one child at a time: L0 first and alone, R last and alone
        $running = @()
        while ($queue.Count -gt 0 -or $running.Count -gt 0) {
            while ($queue.Count -gt 0 -and $running.Count -lt $limit) {
                $j = $queue.Dequeue()
                $p = Start-Child $exe (Get-ChildArgs $j)
                Write-Host ("start {0} (pid {1})" -f $j.Name, $p.Id)
                $running += @{ Job = $j; Proc = $p; Watch = [Diagnostics.Stopwatch]::StartNew() }
            }
            Start-Sleep -Milliseconds 500
            $still = @()
            foreach ($r in $running) {
                $p = $r.Proc
                if ($p.HasExited) { $done[$r.Job.Name] = @{ Code = $p.ExitCode; Secs = $r.Watch.Elapsed.TotalSeconds }; Write-Host ("end   {0} exit {1} in {2:0} s" -f $r.Job.Name, $p.ExitCode, $r.Watch.Elapsed.TotalSeconds) }
                elseif ($r.Watch.Elapsed.TotalMinutes -ge $Timeout) { Stop-Tree $p; $done[$r.Job.Name] = @{ Code = 124; Secs = $r.Watch.Elapsed.TotalSeconds }; Write-Host "TIMED OUT $($r.Job.Name) after $Timeout min, killed" }
                else { $still += $r }
            }
            $running = $still
        }
    }

    # ---- report ----
    $rows = @(); $pass = 0; $fail = 0; $skip = 0; $laneFailed = $false
    foreach ($lane in ($selected | ForEach-Object { $_.Lane } | Select-Object -Unique)) {
        $lp = 0; $lf = 0; $ls = 0; $secs = 0; $codes = @(); $ok = $true
        foreach ($j in ($selected | Where-Object { $_.Lane -eq $lane })) {
            $d = $done[$j.Name]; $r = Get-Result (Join-Path $temp "tf-suite-$($j.Name).log")
            $lp += $r.Pass; $lf += $r.Fail; $ls += $r.Skip; $secs += $d.Secs; $codes += $d.Code
            if ($d.Code -ne 0 -or -not $r.Has -or $r.Fail -gt 0) { $ok = $false }
        }
        $pass += $lp; $fail += $lf; $skip += $ls
        $note = if ($ok) { '' } elseif ($codes -contains 124) { '  TIMED OUT (124)' } else { '  FAILED (exit ' + ($codes -join ',') + ')' }
        if (-not $ok) { $laneFailed = $true }
        Write-Host ("lane {0,-3} {1} passed, {2} failed, {3} skipped, {4:0} s{5}" -f $lane, $lp, $lf, $ls, $secs, $note)
    }
    foreach ($j in $selected) {
        $log = Join-Path $temp "tf-suite-$($j.Name).log"
        if (Test-Path -LiteralPath $log) { Select-String -LiteralPath $log -Pattern '^\s+FAIL\s' -CaseSensitive | Select-Object -First 10 | ForEach-Object { Write-Host ("  [{0}] {1}" -f $j.Name, $_.Line.Trim()) } }
    }

    # ---- merge the group gates ----
    $reports = @($selected | ForEach-Object { Join-Path $temp "tf-suite-$($_.Name).json" } | Where-Object { Test-Path -LiteralPath $_ })
    $gateFailed = $false
    if ($reports.Count -gt 0) {
        $mlog = Join-Path $temp 'tf-suite-merge.log'
        $margs = '--profile "' + $temp + '\tf-suite-merge" --selftest "' + $mlog + '" --merge-group-reports "' + ($reports -join ',') + '"'
        if (-not $subset) { $margs += ' --require ci,document-context' }
        $m = Start-Child $exe $margs
        if (-not $m.WaitForExit(120000)) { Stop-Tree $m; $gateFailed = $true; Write-Host 'Merge timed out.' }
        elseif ($m.ExitCode -ne 0) { $gateFailed = $true }
        if (Test-Path -LiteralPath $mlog) { Select-String -LiteralPath $mlog -Pattern 'required: group|FAIL' -CaseSensitive | ForEach-Object { Write-Host ("  [merge] " + $_.Line.Trim()) } }
        if ($subset) { Write-Host 'Subset run: the merged gate was not enforced (--require ci needs every lane).' }
    } else { $gateFailed = -not $subset; Write-Host 'No group reports were written.' }

    # ---- slowest tests ----
    $slow = @()
    foreach ($j in $selected) {
        $csv = Join-Path $temp "tf-suite-$($j.Name).csv"
        if (Test-Path -LiteralPath $csv) { $slow += Import-Csv -LiteralPath $csv | Where-Object { $_.test -notlike '(*' } | ForEach-Object { [pscustomobject]@{ Lane = $j.Name; Test = $_.test; Sec = [double]$_.ms / 1000 } } }
    }
    Write-Host 'Slowest tests:'
    $slow | Sort-Object Sec -Descending | Select-Object -First 10 | ForEach-Object { Write-Host ("  {0,7:0.0} s  {1}  [{2}]" -f $_.Sec, $_.Test, $_.Lane) }

    $line = "TabForge self-test: $pass passed, $fail failed" + $(if ($skip -gt 0) { ", $skip skipped" } else { '' })
    Write-Host ("TOTAL {0}  (wall {1:0} s{2})" -f $line, $wall.Elapsed.TotalSeconds, $(if ($subset) { ', subset' } else { '' }))
    Write-Host $line
    if ($laneFailed -or $gateFailed -or $fail -gt 0) { $exit = 1 }
} finally {
    if ($slot) { Remove-Item -LiteralPath $slot -Recurse -Force -ErrorAction SilentlyContinue }
}
exit $exit
