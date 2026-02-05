@echo off
REM Antty Installation Wrapper for Windows
REM This batch file downloads and runs the PowerShell installer with execution policy bypass
REM No need to change PowerShell execution policy settings

echo Installing Antty...
echo.

REM Download the PowerShell installer to the same temp directory
set "INSTALL_PS1=%TEMP%\antty-install-script.ps1"
echo Downloading installer script...
powershell.exe -NoProfile -Command "Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/wisedev-pstach/Antty/main/install.ps1' -OutFile '%INSTALL_PS1%' -UseBasicParsing"

if %ERRORLEVEL% neq 0 (
    echo.
    echo Failed to download installer script!
    pause
    exit /b 1
)

echo Running installer...
echo.

REM Run the PowerShell installer with execution policy bypass
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%INSTALL_PS1%"

if %ERRORLEVEL% neq 0 (
    echo.
    echo Installation failed!
    del "%INSTALL_PS1%" 2>nul
    pause
    exit /b 1
)

REM Clean up
del "%INSTALL_PS1%" 2>nul

echo.
pause
