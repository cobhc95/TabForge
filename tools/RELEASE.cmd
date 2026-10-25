@echo off
rem Builds the dist\ portable zip + installer for GitHub releases (run REBUILD.cmd first).
cd /d "%~dp0..\.."
powershell -NoProfile -ExecutionPolicy Bypass -File Source\tools\Package-Release.ps1 %*
exit /b %errorlevel%
