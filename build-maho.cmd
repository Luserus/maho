@echo off
setlocal
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0build-maho.ps1" %*
exit /b %ERRORLEVEL%
