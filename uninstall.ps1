# Antty Uninstallation Script for Windows

Write-Host "Uninstalling Antty..." -ForegroundColor Cyan
Write-Host ""

# Get the project root directory
$projectRoot = $PSScriptRoot
if (-not $projectRoot) {
    $projectRoot = Get-Location
}

# Remove from PATH
$publishPath = Join-Path $projectRoot "publish"
Write-Host "Removing from PATH..." -ForegroundColor Yellow

$currentPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($currentPath -like "*$publishPath*") {
    $newPath = $currentPath -replace [regex]::Escape(";$publishPath"), ""
    $newPath = $newPath -replace [regex]::Escape("$publishPath;"), ""
    $newPath = $newPath -replace [regex]::Escape($publishPath), ""
    [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
    Write-Host "Removed from PATH" -ForegroundColor Green
} else {
    Write-Host "Not found in PATH" -ForegroundColor Yellow
}

# Delete published files
if (Test-Path $publishPath) {
    Write-Host "Deleting published files..." -ForegroundColor Yellow
    Remove-Item -Path $publishPath -Recurse -Force
    Write-Host "Published files deleted" -ForegroundColor Green
} else {
    Write-Host "No published files to delete" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Uninstallation complete!" -ForegroundColor Green
