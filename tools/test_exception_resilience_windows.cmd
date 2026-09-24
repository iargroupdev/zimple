@echo off
setlocal

powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0test_exception_resilience_windows.ps1" %*
exit /b %ERRORLEVEL%
