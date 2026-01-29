# Antty Installation Script for Windows
# This script publishes the application and adds it to your PATH
# Note: This modifies USER PATH only, no admin privileges required

Write-Host "Installing Antty..." -ForegroundColor Cyan
Write-Host ""

# Get the project root directory (where this script is located)
$projectRoot = $PSScriptRoot
if (-not $projectRoot) {
    $projectRoot = Get-Location
}
Set-Location $projectRoot

# Build and publish the application (framework-dependent to avoid LlamaSharp conflicts)
Write-Host "Publishing application..." -ForegroundColor Yellow

# Use framework-dependent deployment - no runtime identifier to avoid native lib conflicts
dotnet publish src/Antty.csproj `
    --configuration Release `
    --output publish `
    --framework net10.0

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Build successful!" -ForegroundColor Green
Write-Host ""

# Get the full path to the published executable
$publishPath = Join-Path $projectRoot "publish"
$exePath = Join-Path $publishPath "Antty.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "Executable not found at: $exePath" -ForegroundColor Red
    exit 1
}

# Clean up old PATH entries (from previous installations)
Write-Host "Configuring PATH..." -ForegroundColor Yellow
$currentPath = [Environment]::GetEnvironmentVariable("Path", "User")

# Remove old Antty paths
$oldPaths = @(
    Join-Path $projectRoot "publish\win-x64"
    Join-Path $projectRoot "publish\linux-x64"
    Join-Path $projectRoot "publish\osx-x64"
    "$projectRoot\publish\win-x64"
    "$projectRoot\publish\linux-x64"
    "$projectRoot\publish\osx-x64"
)

foreach ($oldPath in $oldPaths) {
    if ($currentPath -like "*$oldPath*") {
        $currentPath = $currentPath -replace [regex]::Escape(";$oldPath"), ""
        $currentPath = $currentPath -replace [regex]::Escape("$oldPath;"), ""
        $currentPath = $currentPath -replace [regex]::Escape($oldPath), ""
        Write-Host "Removed old PATH entry: $oldPath" -ForegroundColor Yellow
    }
}

# Add new path if not already present
if ($currentPath -notlike "*$publishPath*") {
    $newPath = "$currentPath;$publishPath"
    [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
    Write-Host "Added to PATH: $publishPath" -ForegroundColor Green
    
    # Also update the current session
    $env:Path = "$env:Path;$publishPath"
} else {
    Write-Host "PATH already configured" -ForegroundColor Green
}

Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "IMPORTANT: Close and reopen your terminal for PATH changes to take effect!" -ForegroundColor Yellow
Write-Host ""
Write-Host "Then you can use 'antty' from any directory!" -ForegroundColor Cyan
Write-Host ""
Write-Host "Try it now in a NEW terminal: antty" -ForegroundColor Cyan
