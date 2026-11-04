# Runs the full test suite in parallel processes under a watchdog (build first). Tests are split by the first letter of the class name, balanced on measured time.
# Kills the run when a shard finishes no test for -QuietSec (stuck or looping), a testhost passes -MaxMB, or the run passes -MaxMin, and names the last tests.
# Exit 0 pass, 1 test failures, 2 killed. Partial run: tools\test-watchdog.ps1 -Dir $env:TEMP\t -Filter "FullyQualifiedName~MenuOpen"
param(
    [Parameter(Mandatory)][string]$Dir,
    [string]$Filter = "",
    [int]$QuietSec = 120,
    [int]$MaxMB = 5000,
    [int]$MaxMin = 10,
    # Class-name prefixes per process, balanced on measured time. The last process takes everything not listed, so a new test class can never be skipped.
    [string[]]$Shards = @('FxC,ClipV,Vo,BandR,FretboardR,PianoRollL,Po,ClipDro,No,TrackS,LiveT,ClipA,TimelineV,Ac,Al,MainWindowN,ScoreG,Bo', 'Ta,BandS,TrackL,Lo,MainWindowL,PlaybackSt,Th,TimelineC,Te,PianoRollV,FretboardW,ScoreH,TimelineF,Gr,Gl,Ve,Gi,Sv', 'Rea,EngineG,PianoRollR,Sec,So,PianoRollT,Qu,FretboardN,Res,PianoRollK,ScoreT,ClipDragF,Up,TrackT,TimelineM,Xd,MainWindowV,Midd', 'VideoExportF,InstrumentPa,EngineS,InstrumentC,Mix,MidiI,FxP,Gp,MainWindowP,TimelineP,VideoEn,EngineC,ClipL,Sp,ClipDragL,PlaybackSu,Vie,MainWindowR', 'BandC,Sa,Ed,Playh,MainWindowD,Pro,PlaybackD,Ou,ClipT,Ch,Sk,FretboardH,PianoRollG,FretboardP,TrackH,PianoRollE,InstrumentPl,ScoreO', 'Pre,Co,Ar,Plu,MidiP,TrackI,Sca,Pal,FxE,BandF,TimelineR,Ent,MainB,Ra,Ne,FretboardM,Ly,PianoD', 'De,MidiC,ScoreC,TimelineW,MainWindowM,Pas,Part,PlaybackL,ClipS,PianoA,TimelineA,EngineF,MidiB,ScoreZ,MidiR,ScoreN,MidiW', 'Ren,LiveV,TimelineB,Em,Sh,Rf,PlaybackSe,FxA,Bar,TimelineI,TimelineZ,PianoRollO,Ad,Fi,Kn,InstrumentB,FxF,ScoreI', 'PlaybackSn,TrackM,ClipDragR,ScoreP,Ui,BandP,LiveM,Un,ScoreR,Pan,Im,PianoRollH,Mo,AvaloniaD,EngineP,Vib,Sn,PlaybackSc,Lin', 'Pari,MidiS,Na,AvaloniaC,MainWindowS,Pic,As,Cr,TimelineD,AvaloniaPa,PianoRollN,FxS,TrackA,MainWindowA,MainWindowC,An,ClipP,MainWindowG', 'Au,St,Bu,Rec,LiveI,Inse,BandE,Rep,MainWindowT,BandB,EngineL,ScoreS,TrackO,TrackG,Wo,MidiO,EngineA,FxL,MidiV', 'Tran,ClipDragP,EngineT,Sel,Mu,Ap,He,Ke,Ho,Pe,ClipE,Le,FxG,Si,InstrumentV,MidiN,ScoreU,MainWindowF,Vis', 'AvaloniaPlayba,VideoExportP,Gu,EngineR,Tit,Wi,TimelineS,LiveS,PianoRollP,Nc,PianoRollC,TrackD,Cla,Lig,PianoRollM,PlaybackO,FxW,Tro', 'Ca,InstrumentPi,Do,FretboardS,ScoreE,PlaybackT,Tu,Ip,TrackF,AvaloniaE,VideoExportC,Ir,MidiA,BandN,MidiT,TimelineH,Fl,ScoreA,FretboardK', 'TrackP,ScoreFollowS,Set,Me,PianoRollS,Dr,To,Af,MidiE,BandV,Clic,La,TrackC,FretboardV,PlaybackA,Be,Tie,AvaloniaPo,VideoD', 'Vs', '*')
)
$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"
$env:DOTNET_TieredPGO = "0"   # less JIT work and memory across many short-lived processes
$peakOne = 0; $peakAll = 0
$source = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force $Dir | Out-Null
$before = @(Get-Process testhost -ErrorAction SilentlyContinue | ForEach-Object Id)
if ($Filter) { $Shards = @('') }
$runs = @()
$index = 0
foreach ($letters in $Shards) {
    $index++
    $test = if ($Filter) { $Filter }
        elseif ($letters -eq '*') { (($Shards | Where-Object { $_ -ne '*' }) -join ',' -split ',' | ForEach-Object { "FullyQualifiedName!~.Tests.$_" }) -join '&' }
        else { ($letters -split ',' | ForEach-Object { "FullyQualifiedName~.Tests.$_" }) -join '|' }
    $log = Join-Path $Dir "shard$index.txt"
    $run = Start-Process dotnet -ArgumentList @('test', 'tests/TabForge.Avalonia.Tests', '--no-build', '--nologo', '--logger', '"console;verbosity=detailed"', '--filter', "`"$test`"") `
        -WorkingDirectory $source -RedirectStandardOutput $log -RedirectStandardError "$log.err" -NoNewWindow -PassThru
    $runs += [pscustomobject]@{ Name = "shard$index"; Log = $log; Run = $run; Size = -1; Moved = [Diagnostics.Stopwatch]::StartNew(); Took = $null }
}
$clock = [Diagnostics.Stopwatch]::StartNew()
$reason = $null
while ($runs | Where-Object { -not $_.Run.HasExited }) {
    Start-Sleep 2
    $hosts = @(Get-Process testhost -ErrorAction SilentlyContinue | Where-Object { $before -notcontains $_.Id })
    $mb = ($hosts | Measure-Object WorkingSet64 -Maximum).Maximum / 1MB
    $peakOne = [math]::Max($peakOne, $mb)
    $peakAll = [math]::Max($peakAll, ($hosts | Measure-Object WorkingSet64 -Sum).Sum / 1MB)
    foreach ($r in $runs) {
        if ($r.Run.HasExited) { if (-not $r.Took) { $r.Took = $clock.Elapsed }; continue }
        $now = (Get-Item $r.Log -ErrorAction SilentlyContinue).Length
        if ($now -ne $r.Size) { $r.Size = $now; $r.Moved.Restart() }
        if ($r.Moved.Elapsed.TotalSeconds -gt $QuietSec) { $reason = "$($r.Name): no test finished for $QuietSec s" }
    }
    if ($mb -gt $MaxMB) { $reason = "a testhost is at $([int]$mb) MB" }
    if ($clock.Elapsed.TotalMinutes -gt $MaxMin) { $reason = "run passed $MaxMin min" }
    if ($reason) { break }
}
$code = 0
if ($reason) {
    "WATCHDOG KILL: $reason after $($clock.Elapsed)"
    foreach ($r in $runs | Where-Object { -not $_.Run.HasExited }) {
        "Last finished in $($r.Name):"; Get-Content $r.Log -Tail 300 | Where-Object { $_ -match '^\s+(Passed|Failed) ' } | Select-Object -Last 2
    }
    $hosts | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep 3
    $runs | Where-Object { -not $_.Run.HasExited } | ForEach-Object { $_.Run | Stop-Process -Force -ErrorAction SilentlyContinue }
    $code = 2
}
"elapsed $($clock.Elapsed); peak MB: one testhost $([int]$peakOne), all $([int]$peakAll)"
foreach ($r in $runs) { "$($r.Name) took $($r.Took): " + (((Get-Content $r.Log -Tail 8) | Where-Object { $_ -match 'Passed:|Failed:|Total tests' } | ForEach-Object { $_.Trim() }) -join ' ') }
$failed = @(Select-String -Path ($runs.Log) -Pattern '^\s+Failed ' | ForEach-Object { $_.Line.Trim() })
"failed: $($failed.Count)"
$failed
if ($code -eq 0 -and $failed.Count -gt 0) { $code = 1 }
exit $code
