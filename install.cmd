@echo off
REM Antty Installation Wrapper for Windows
REM This batch file runs the PowerShell installer with execution policy bypass
REM No need to change PowerShell execution policy settings

echo Installing Antty...
echo.

REM 
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%~dp0install.ps1"

if %ERRORLEVEL% neq 0 (
    echo.
    echo Installation failed!
    pause
    exit /b 1
)

echo.
pause
