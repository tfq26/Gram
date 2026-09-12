@echo off
setlocal
cd /d "%~dp0"
title Gram Desktop Launcher
powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\launch-desktop.ps1" %*
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Gram Desktop terminated with an error (Exit code: %ERRORLEVEL%).
    pause
)
