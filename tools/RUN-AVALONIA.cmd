@echo off
rem Quick feedback loop for TabForge: closes the running app,
rem builds incrementally into build\TabForge (no clean, no tests) and opens it.
setlocal
set OUT=%~dp0..\..\build\TabForge
taskkill /im TabForge.exe /f >nul 2>&1
dotnet build "%~dp0..\src\TabForge.Avalonia\TabForge.Avalonia.csproj" -c Release -o "%OUT%" -nologo -v:q -clp:ErrorsOnly
if errorlevel 1 (
  echo Build failed.
  pause
  exit /b 1
)
powershell -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Clear-LowIntegrity.ps1" -Output "%OUT%"
start "" "%OUT%\TabForge.exe"
