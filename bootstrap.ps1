# Antty Bootstrap Installer
# This script clones the repository and runs the full installer

$ErrorActionPreference = 'Stop'

Write-Host "Antty Installer" -ForegroundColor Cyan
Write-Host ""

$tempDir = Join-Path $env:TEMP "antty-installer"
$zipPath = Join-Path $env:TEMP "antty.zip"

try {
    # Download repository as zip
    Write-Host "Downloading Antty..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri "https://github.com/wisedev-pstach/Antty/archive/refs/heads/main.zip" -OutFile $zipPath -UseBasicParsing
    
    # Extract
    Write-Host "Extracting..." -ForegroundColor Yellow
    if (Test-Path $tempDir) {
        Remove-Item $tempDir -Recurse -Force
    }
    Expand-Archive -Path $zipPath -DestinationPath $tempDir -Force
    
    # Run the real installer
    $installerPath = Join-Path $tempDir "Antty-main\install.ps1"
    Write-Host "Running installer..." -ForegroundColor Yellow
    Write-Host ""
    
    Set-Location (Join-Path $tempDir "Antty-main")
    & $installerPath
    
} finally {
    # Cleanup
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
    }
    if (Test-Path $tempDir) {
        Remove-Item $tempDir -Recurse -Force
    }
}
