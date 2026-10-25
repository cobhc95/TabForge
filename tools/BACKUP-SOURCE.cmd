@echo off
setlocal
cd /d "%~dp0..\.."
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "Source\tools\New-SourceBackup.ps1" -Label source
if errorlevel 1 exit /b %errorlevel%
