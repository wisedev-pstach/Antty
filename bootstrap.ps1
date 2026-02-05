# Antty Bootstrap Installer
# This script downloads the repository and runs the full installer

$ErrorActionPreference = 'Stop'

Write-Host "Antty Installer" -ForegroundColor Cyan
Write-Host ""

$tempDir = Join-Path $env:TEMP "antty-installer-$(Get-Random)"
$zipPath = Join-Path $env:TEMP "antty-$(Get-Random).zip"

try {
    # Download repository as zip
    Write-Host "Downloading Antty..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri "https://github.com/wisedev-pstach/Antty/archive/refs/heads/main.zip" -OutFile $zipPath -UseBasicParsing
    
    # Extract
    Write-Host "Extracting..." -ForegroundColor Yellow
    Expand-Archive -Path $zipPath -DestinationPath $tempDir -Force
    
    # Run the real installer without changing directory
    $installerDir = Join-Path $tempDir "Antty-main"
    $installerPath = Join-Path $installerDir "install.ps1"
    
    Write-Host "Running installer..." -ForegroundColor Yellow
    Write-Host ""
    
    # Run installer with execution policy bypass
    Push-Location $installerDir
    try {
        powershell.exe -ExecutionPolicy Bypass -NoProfile -File $installerPath
    } finally {
        Pop-Location
    }
    
} finally {
    # Cleanup - give a moment for any file handles to close
    Start-Sleep -Milliseconds 500
    
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path $tempDir) {
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
