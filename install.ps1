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

# Check if .NET SDK is installed
Write-Host "Checking for .NET SDK..." -ForegroundColor Yellow
$dotnetExists = $false
try {
    $dotnetVersion = dotnet --version 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Found .NET SDK version: $dotnetVersion" -ForegroundColor Green
        $dotnetExists = $true
    }
} catch {
    # dotnet not found
}

if (-not $dotnetExists) {
    Write-Host "⚠ .NET SDK not found. Installing .NET 10..." -ForegroundColor Yellow
    Write-Host ""
    
    # Download and run the official .NET install script
    $installScript = "$env:TEMP\dotnet-install.ps1"
    
    try {
        Write-Host "Downloading .NET installer..." -ForegroundColor Yellow
        Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $installScript -UseBasicParsing
        
        Write-Host "Installing .NET 10 SDK (this may take a few minutes)..." -ForegroundColor Yellow
        & $installScript -Channel 10.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"
        
        # Add .NET to PATH for current session
        $dotnetPath = "$env:LOCALAPPDATA\Microsoft\dotnet"
        $env:Path = "$dotnetPath;$env:Path"
        
        # Add .NET to User PATH permanently
        $currentUserPath = [Environment]::GetEnvironmentVariable("Path", "User")
        if ($currentUserPath -notlike "*$dotnetPath*") {
            $newUserPath = "$dotnetPath;$currentUserPath"
            [Environment]::SetEnvironmentVariable("Path", $newUserPath, "User")
            Write-Host "✓ Added .NET to PATH" -ForegroundColor Green
        }
        
        # Verify installation
        $dotnetVersion = & "$dotnetPath\dotnet.exe" --version 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✓ .NET SDK $dotnetVersion installed successfully!" -ForegroundColor Green
        } else {
            Write-Host "⚠ .NET installation completed but verification failed. You may need to restart your terminal." -ForegroundColor Yellow
        }
        
        Write-Host ""
    } catch {
        Write-Host "✗ Failed to install .NET SDK automatically." -ForegroundColor Red
        Write-Host "Please install .NET 10 manually from: https://dotnet.microsoft.com/download" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Error: $_" -ForegroundColor Red
        exit 1
    } finally {
        # Clean up installer script
        if (Test-Path $installScript) {
            Remove-Item $installScript -Force
        }
    }
}

Write-Host ""

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
