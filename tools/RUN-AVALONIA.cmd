@echo off
rem Quick feedback loop for TabForge: builds incrementally into build\TabForge
rem (no clean, no tests) and opens it. It never kills a running TabForge: the
rem owner may be using one (and killing it also ends its engine and helpers).
setlocal
set OUT=%~dp0..\..\build\TabForge
tasklist /FI "IMAGENAME eq TabForge.exe" 2>nul | find /I "TabForge.exe" >nul
if not errorlevel 1 (
  echo TabForge is running. Close it before rebuilding; this script never kills an app it did not start.
  exit /b 2
)
dotnet build "%~dp0..\src\TabForge.Avalonia\TabForge.Avalonia.csproj" -c Release -o "%OUT%" -nologo -v:q -clp:ErrorsOnly
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Clear-LowIntegrity.ps1" -Output "%OUT%"
start "" "%OUT%\TabForge.exe"
