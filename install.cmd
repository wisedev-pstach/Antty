@echo off
REM Antty Installation Wrapper for Windows
REM This batch file creates and runs the PowerShell installer locally
REM No need to download or worry about encoding issues

echo Installing Antty...
echo.

REM Create the PowerShell script inline to avoid download encoding issues
set "INSTALL_PS1=%TEMP%\antty-install-inline.ps1"

echo Creating installer script...
(
echo $ErrorActionPreference = 'Stop'
echo Write-Host "Installing Antty..." -ForegroundColor Cyan
echo Write-Host ""
echo.
echo # Get current directory
echo $projectRoot = Get-Location
echo.
echo # Check if we're running from a cloned repo or need to clone
echo if ^(Test-Path "src\Antty.csproj"^) {
echo     Write-Host "Found Antty project in current directory" -ForegroundColor Green
echo } else {
echo     Write-Host "Cloning Antty repository..." -ForegroundColor Yellow
echo     git clone https://github.com/wisedev-pstach/Antty.git antty-repo
echo     if ^($LASTEXITCODE -ne 0^) {
echo         Write-Host "Failed to clone repository. Please install git or clone manually." -ForegroundColor Red
echo         exit 1
echo     }
echo     Set-Location antty-repo
echo     $projectRoot = Get-Location
echo }
echo.
echo # Check for .NET
echo Write-Host "Checking for .NET SDK..." -ForegroundColor Yellow
echo try {
echo     $dotnetVersion = dotnet --version 2^^^>$null
echo     if ^($LASTEXITCODE -eq 0^) {
echo         Write-Host "Found .NET SDK version: $dotnetVersion" -ForegroundColor Green
echo     } else {
echo         throw "dotnet not found"
echo     }
echo } catch {
echo     Write-Host ".NET SDK not found. Please install .NET 10 from: https://dotnet.microsoft.com/download" -ForegroundColor Red
echo     Write-Host "After installing .NET, run this script again." -ForegroundColor Yellow
echo     exit 1
echo }
echo.
echo # Build and publish
echo Write-Host "Publishing application..." -ForegroundColor Yellow
echo dotnet publish src/Antty.csproj --configuration Release --output publish --framework net10.0
echo.
echo if ^($LASTEXITCODE -ne 0^) {
echo     Write-Host "Build failed!" -ForegroundColor Red
echo     exit 1
echo }
echo.
echo Write-Host "Build successful!" -ForegroundColor Green
echo.
echo # Configure PATH
echo $publishPath = Join-Path $projectRoot "publish"
echo $currentPath = [Environment]::GetEnvironmentVariable^("Path", "User"^)
echo.
echo if ^($currentPath -notlike "*$publishPath*"^) {
echo     $newPath = "$currentPath;$publishPath"
echo     [Environment]::SetEnvironmentVariable^("Path", $newPath, "User"^)
echo     Write-Host "Added to PATH: $publishPath" -ForegroundColor Green
echo     $env:Path = "$env:Path;$publishPath"
echo } else {
echo     Write-Host "PATH already configured" -ForegroundColor Green
echo }
echo.
echo Write-Host ""
echo Write-Host "Installation complete!" -ForegroundColor Green
echo Write-Host ""
echo Write-Host "IMPORTANT: Restart your terminal, then run 'antty' from any directory!" -ForegroundColor Yellow
) > "%INSTALL_PS1%"

REM Run the script
powershell.exe -ExecutionPolicy Bypass -NoProfile -File "%INSTALL_PS1%"

set RESULT=%ERRORLEVEL%

REM Clean up
del "%INSTALL_PS1%" 2>nul

if %RESULT% neq 0 (
    echo.
    echo Installation failed!
    pause
    exit /b 1
)

echo.
pause
