# Antty Uninstallation Script for Windows

Write-Host "Uninstalling Antty..." -ForegroundColor Cyan
Write-Host ""

# Installation directory
$installDir = Join-Path $env:LOCALAPPDATA "Antty"

# Remove from PATH
Write-Host "Removing from PATH..." -ForegroundColor Yellow
$currentPath = [Environment]::GetEnvironmentVariable('Path', 'User')

# Remove all paths containing 'antty' (case insensitive)
$pathParts = $currentPath -split ';' | Where-Object { $_ -notlike '*antty*' }
$cleanPath = ($pathParts -join ';')

if ($currentPath -ne $cleanPath) {
    [Environment]::SetEnvironmentVariable('Path', $cleanPath, 'User')
    Write-Host "Removed from PATH" -ForegroundColor Green
} else {
    Write-Host "Not found in PATH" -ForegroundColor Yellow
}

# Delete installation directory
if (Test-Path $installDir) {
    Write-Host "Deleting installation files from $installDir..." -ForegroundColor Yellow
    Remove-Item -Path $installDir -Recurse -Force
    Write-Host "Installation files deleted" -ForegroundColor Green
} else {
    Write-Host "Installation directory not found" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Uninstallation complete!" -ForegroundColor Green
Write-Host "Please restart your terminal for PATH changes to take effect." -ForegroundColor Yellow
