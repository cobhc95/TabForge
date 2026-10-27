@echo off
setlocal EnableExtensions
cd /d "%~dp0..\.."
title TabForge - Build

set "BUILD_MODE=portable self-contained"
set "PUBLISH_OUTPUT=build\TabForge"
set "SELF_CONTAINED=true"
if /I "%~1"=="compact" (
    set "BUILD_MODE=framework-dependent compact"
    set "PUBLISH_OUTPUT=build\TabForge-compact"
    set "SELF_CONTAINED=false"
)

echo ============================================================
echo  TabForge - one-click Windows build
echo ============================================================
echo.

set "DOTNET_EXE=dotnet"
where dotnet >nul 2>nul
if errorlevel 1 (
    echo A trusted .NET 10 SDK is required. Install it from https://dotnet.microsoft.com/download/dotnet/10.0 and rerun this build.
    goto :fail
)

"%DOTNET_EXE%" --list-sdks | findstr /R "^10\." >nul
if errorlevel 1 (
    echo .NET 10 SDK was not found. TabForge does not download or execute SDK installers during normal builds.
    echo Install a trusted .NET 10 SDK and rerun this build.
    goto :fail
)

echo Using: %DOTNET_EXE%
"%DOTNET_EXE%" --version
if errorlevel 1 goto :fail

echo.
echo Removing intermediate caches of all projects...
for %%D in ("Source\src\TabForge.Avalonia\bin" "Source\src\TabForge.Avalonia\obj" "Source\src\TabForge.AudioEngine\bin" "Source\src\TabForge.AudioEngine\obj" "Source\src\TabForge.Audio.Contracts\bin" "Source\src\TabForge.Audio.Contracts\obj" "Source\src\TabForge.Core\bin" "Source\src\TabForge.Core\obj") do (
    if exist %%D rmdir /s /q %%D
    if exist %%D ( echo Could not delete %%D - is a file still open? & goto :fail )
)

echo [1/3] Restoring packages...
"%DOTNET_EXE%" restore "Source\src\TabForge.Avalonia\TabForge.Avalonia.csproj" -r win-x64 --locked-mode
if errorlevel 1 goto :fail

echo.
echo [2/3] Publishing %BUILD_MODE% Windows x64 build...
if exist "%PUBLISH_OUTPUT%" rmdir /s /q "%PUBLISH_OUTPUT%"
"%DOTNET_EXE%" publish "Source\src\TabForge.Avalonia\TabForge.Avalonia.csproj" -c Release -r win-x64 --no-restore --self-contained %SELF_CONTAINED% -p:PublishSingleFile=true -p:PublishTrimmed=false -p:IncludeNativeLibrariesForSelfExtract=true -o "%PUBLISH_OUTPUT%"
if errorlevel 1 goto :fail
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "Source\tools\Clear-LowIntegrity.ps1" -Output "%PUBLISH_OUTPUT%"

echo.
echo [3/3] Creating timestamped source-only backup in Backups...
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "Source\tools\New-SourceBackup.ps1" -Label source
if errorlevel 1 (
    echo WARNING: the source backup was not created; see the message above.
) else (
    echo Source backup created.
)

echo.
echo ============================================================
echo  BUILD SUCCEEDED
echo  EXE: %CD%\%PUBLISH_OUTPUT%\TabForge.exe
if /I "%BUILD_MODE%"=="framework-dependent compact" echo  Requires .NET 10 Windows Desktop Runtime
echo ============================================================
exit /b 0

:fail
echo.
echo ============================================================
echo  BUILD FAILED - see the error above.
echo ============================================================
pause
exit /b 1
