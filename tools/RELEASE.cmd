@echo off
rem Builds the dist\ portable zip + installer locally, without publishing. To publish, use SHIP.cmd.
cd /d "%~dp0..\.."
powershell -NoProfile -ExecutionPolicy Bypass -File Source\tools\Package-Release.ps1 %*
exit /b %errorlevel%
