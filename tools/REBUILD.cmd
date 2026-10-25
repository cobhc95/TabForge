@echo off
setlocal EnableExtensions
set "AUTO=0"
if /I "%~1"=="auto" set "AUTO=1"
set "FRESH=0"
if /I "%~1"=="fresh" set "FRESH=1"
if /I "%~2"=="fresh" set "FRESH=1"
rem Self-test policy (see AGENTS.md): a rebuild runs the small BASIC set only (architecture, hygiene, a few
rem sub-second smoke tests; about 5 seconds, no test that opens a window), plus the narrow tests named in TABFORGE_SELFTEST_ONLY (TestA,TestB) and/or the areas in
rem TABFORGE_SELFTEST_AREAS, plus the short headless playback test. The big suite never runs implicitly.
rem   fast      = incremental publish and launch an empty TabForge: no cache wipe, no tests, no backup, no ReadyToRun precompile (well under a minute).
rem   quick     = the same without the playback test (small UI or bug checks).
rem   release   = runs the release gate (--areas release: basic plus save/recovery, import, playback/edit, document-context and security
rem               tests, about 2 minutes) instead of the basic set. Release packaging runs the same gate.
set "QUICK=0"
set "FAST=0"
set "FASTPUB="
set "SELFTEST_RESULT=0"
set "TITLE_MATCH=TabForge*"
set "RELEASEGATE=0"
for %%A in (%*) do (
    if /I "%%~A"=="release" set "RELEASEGATE=1"
    if /I "%%~A"=="quick" set "QUICK=1"
    if /I "%%~A"=="fast" set "FAST=1"& set "FASTPUB=-p:PublishReadyToRun=false"
)
cd /d "%~dp0..\.."
title TabForge - Clean rebuild

echo ============================================================
echo  TabForge - clean rebuild (kill, wipe caches, build, test, backup, launch)
echo ============================================================

echo.
echo [1/7] Stopping running TabForge instances...
taskkill /F /IM TabForge.exe >nul 2>nul
powershell -NoProfile -Command "Get-Process TabForge -ErrorAction SilentlyContinue | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue"
rem Settings are kept (plug-in folders, audio driver, colours...). "REBUILD.cmd fresh" resets them to defaults.
if "%FRESH%"=="1" (
    echo Resetting TabForge settings to defaults...
    del /f /q "%APPDATA%\TabForge\settings.json" >nul 2>nul
) else (
    echo Keeping your TabForge settings ^(REBUILD.cmd fresh resets them^).
)

if "%FAST%"=="1" goto :publish
echo [2/7] Removing previous build output and caches...
for %%D in ("build\TabForge" "build\TabForge-compact" "Source\src\TabForge.Avalonia\bin" "Source\src\TabForge.Avalonia\obj" "Source\src\TabForge.AudioEngine\bin" "Source\src\TabForge.AudioEngine\obj" "Source\src\TabForge.Audio.Contracts\bin" "Source\src\TabForge.Audio.Contracts\obj" "Source\src\TabForge.Core\bin" "Source\src\TabForge.Core\obj") do (
    if exist %%D rmdir /s /q %%D
    if exist %%D ( echo Could not delete %%D - is a file still open? & goto :fail )
)

:publish
echo [3/7] Restoring and publishing a fresh Release build...
dotnet restore "Source\src\TabForge.Avalonia\TabForge.Avalonia.csproj" -r win-x64 --locked-mode
if errorlevel 1 goto :fail
dotnet publish "Source\src\TabForge.Avalonia\TabForge.Avalonia.csproj" -c Release -r win-x64 --no-restore --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true %FASTPUB% -o "build\TabForge"
if errorlevel 1 goto :fail
rem A Low integrity label inherited from a sandboxed workspace would start the app (and the self-tests) unable to write user folders.
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "Source\tools\Clear-LowIntegrity.ps1" -Output "build\TabForge"
if "%FAST%"=="1" goto :launchnosong

echo [4/7] Running headless self-tests...
set "SELFTEST_LOG=%TEMP%\tabforge-selftest.log"
set "SELFTEST_RESULT=0"
rem Every run has a 10-minute limit (No endless runs): a hung run is killed and reported as exit 124.
set "TF_EXE=%CD%\build\TabForge\TabForge.exe"
set "TF_TIMEOUT_MS=600000"
set "BASIC_ARG=|--areas|basic"
if "%RELEASEGATE%"=="1" set "BASIC_ARG=|--areas|release"
echo Basic set:
set "CUR_LOG=%SELFTEST_LOG%"
set "TF_ARGS=--selftest|%CUR_LOG%%BASIC_ARG%"
call :selftest
if defined TABFORGE_SELFTEST_ONLY (
    echo Narrow tests: %TABFORGE_SELFTEST_ONLY%
    set "CUR_LOG=%TEMP%\tabforge-selftest-only.log"
    set "TF_ARGS=--selftest|%TEMP%\tabforge-selftest-only.log|--only|%TABFORGE_SELFTEST_ONLY%"
    call :selftest
)
if defined TABFORGE_SELFTEST_AREAS (
    echo Focused areas: %TABFORGE_SELFTEST_AREAS%
    set "CUR_LOG=%TEMP%\tabforge-selftest-areas.log"
    set "TF_ARGS=--selftest|%TEMP%\tabforge-selftest-areas.log|--areas|%TABFORGE_SELFTEST_AREAS%"
    call :selftest
)

rem Song used by the playtest and the final launch: set TABFORGE_PLAYTEST_SONG to use another file.
rem Default is the bundled demo song in Samples; when the song does not exist (clean checkout, CI) the playtest is
rem skipped and the app is launched without a song.
set "SONG=%TABFORGE_PLAYTEST_SONG%"
if not defined SONG set "SONG=%CD%\Samples\TabForge Demo - Ashen Meridian.gp"
echo [5/7] Running headless playback test (6 s of live MIDI dispatch on %SONG%)...
if not exist "%SONG%" goto :noplaytest
rem The Avalonia build has no --playtest mode yet (playback is covered by the in-app self-tests); skip unless TABFORGE_PLAYTEST=1.
if not "%TABFORGE_PLAYTEST%"=="1" (echo Playback test skipped: not available in the Avalonia build yet; the self-tests cover playback.& goto :playtestdone)
if "%QUICK%"=="1" (echo Quick build: playback test skipped.& goto :noplaytest)
set "PLAYTEST_LOG=%TEMP%\tabforge-playtest.log"
set "TF_ARGS=--playtest|%SONG%|6|%PLAYTEST_LOG%"
rem The playtest has a 60-second hard limit (6 s of playback); only the PID this script started is closed.
set "TF_TIMEOUT_MS=60000"
call :runtimed
set "PLAYTEST_RESULT=%errorlevel%"
set "TF_TIMEOUT_MS=600000"
findstr /B /C:"dispatches=" /C:"latency ms:" /C:"early(" "%PLAYTEST_LOG%"
if "%PLAYTEST_RESULT%"=="124" (echo PLAYBACK TEST TIMED OUT after 60 s - the process was killed & set "SELFTEST_RESULT=1" & goto :playtestdone)
if "%PLAYTEST_RESULT%"=="0" if not exist "%PLAYTEST_LOG%" set "PLAYTEST_RESULT=1"
if "%PLAYTEST_RESULT%"=="0" (echo Playback test: PASS) else (echo PLAYBACK TEST FAILED - see %PLAYTEST_LOG% & set "SELFTEST_RESULT=1")
goto :playtestdone
:noplaytest
echo Playback test SKIPPED: song not found. Set TABFORGE_PLAYTEST_SONG to a .gp/.gp5 file to enable it.
:playtestdone

echo [6/7] Creating timestamped source-only backup...
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "Source\tools\New-SourceBackup.ps1" -Label source
if errorlevel 1 echo WARNING: source backup was not created.
echo [7/7] Launching TabForge (with the song when one is available)...
rem Launch via ShellExecute so the app does NOT inherit this script's stdout/stderr handles;
rem with "start" a caller that redirects output would wait until TabForge is closed.
rem The main window title is always "TabForge" (MainWindow.PreviewTitle); the open song's name is not in it.
set "TITLE_MATCH=TabForge*"
if not exist "%SONG%" goto :launchnosong
powershell -NoProfile -Command "Start-Process -FilePath '%CD%\build\TabForge\TabForge.exe' -WorkingDirectory '%CD%' -ArgumentList '\"%SONG%\"'"
goto :launched
:launchnosong
powershell -NoProfile -Command "Start-Process -FilePath '%CD%\build\TabForge\TabForge.exe' -WorkingDirectory '%CD%'"
:launched
powershell -NoProfile -Command "$p=$null; for($i=0;$i -lt 60;$i++){Start-Sleep -Milliseconds 500; $p=Get-Process TabForge -ErrorAction SilentlyContinue | Where-Object {$_.MainWindowHandle -ne 0 -and $_.Responding -and $_.MainWindowTitle -like $env:TITLE_MATCH}; if($p){break}}; if($p){Write-Output ('Window up: ' + $p[0].MainWindowTitle)} else {Write-Output 'WARNING: TabForge window did not appear within 30 s'; exit 1}"

echo.
echo ============================================================
if "%SELFTEST_RESULT%"=="0" (echo  REBUILD SUCCEEDED) else (echo  BUILT, BUT A TEST FAILED - see above)
echo ============================================================
if "%AUTO%"=="0" pause
exit /b %SELFTEST_RESULT%

:fail
echo.
echo ============================================================
echo  REBUILD FAILED - see the error above.
echo ============================================================
if "%AUTO%"=="0" pause
exit /b 1

:runtimed
rem Runs %TF_EXE% with the "|"-separated %TF_ARGS% and waits at most 10 minutes; exit 124 = timed out (killed).
powershell -NoProfile -Command "$q=[char]34; $a=@($env:TF_ARGS.Split('|') | ForEach-Object { $q + $_ + $q }); $p=Start-Process -FilePath $env:TF_EXE -ArgumentList $a -PassThru; $null=$p.Handle; if(-not $p.WaitForExit([int]$env:TF_TIMEOUT_MS)){ Stop-Process -Id $p.Id -Force; exit 124 }; $p.WaitForExit(); exit $p.ExitCode"
exit /b %errorlevel%

:selftest
rem Runs %TF_EXE% with the "|"-separated %TF_ARGS% (log: %CUR_LOG%), prints the summary and sets SELFTEST_RESULT=1 on any failure.
call :runtimed
set "RUN_RESULT=%errorlevel%"
findstr /C:"self-test:" "%CUR_LOG%"
rem The process can exit 0 even when checks failed, so the log decides too.
if "%RUN_RESULT%"=="0" findstr /R /C:"self-test:.* [1-9][0-9]* failed" "%CUR_LOG%" >nul && set "RUN_RESULT=1"
if "%RUN_RESULT%"=="124" echo SELF-TEST TIMED OUT after 10 minutes - the process was killed.
if not "%RUN_RESULT%"=="0" (
    echo SELF-TEST FAILED - failing checks:
    findstr /C:"FAIL" "%CUR_LOG%"
    echo Full log: %CUR_LOG%
    set "SELFTEST_RESULT=1"
)
exit /b 0
