@echo off
rem Publishes the current version to GitHub (see docs\guides\RELEASING.md).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Ship-Release.ps1" %*
exit /b %errorlevel%
