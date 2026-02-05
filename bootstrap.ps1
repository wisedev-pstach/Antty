# Antty Bootstrap Installer - Fully Self-Contained
# This script does the complete installation without relying on other scripts

$ErrorActionPreference = 'Stop'

Write-Host "Antty Installer" -ForegroundColor Cyan
Write-Host ""

$tempDir = Join-Path $env:TEMP "antty-install-$(Get-Random)"
$zipPath = Join-Path $env:TEMP "antty-$(Get-Random).zip"
$installDir = Join-Path $env:LOCALAPPDATA "Antty"

try {
    # Download repository as zip
    Write-Host "Downloading Antty..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri "https://github.com/wisedev-pstach/Antty/archive/refs/heads/main.zip" -OutFile $zipPath -UseBasicParsing
    
    # Extract
    Write-Host "Extracting..." -ForegroundColor Yellow
    Expand-Archive -Path $zipPath -DestinationPath $tempDir -Force
    
    $projectRoot = Join-Path $tempDir "Antty-main"
    Write-Host ""
    
    # Check for .NET
    Write-Host "Checking for .NET SDK..." -ForegroundColor Yellow
    $dotnetExists = $false
    try {
        $dotnetVer = dotnet --version 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Found .NET SDK version: $dotnetVer" -ForegroundColor Green
            $dotnetExists = $true
        }
    } catch { }
    
    if (-not $dotnetExists) {
        Write-Host "Installing .NET 10 SDK..." -ForegroundColor Yellow
        $dotnetInstaller = Join-Path $env:TEMP "dotnet-install-$(Get-Random).ps1"
        
        try {
            Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $dotnetInstaller -UseBasicParsing
            & $dotnetInstaller -Channel 10.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"
            
            $dotnetPath = "$env:LOCALAPPDATA\Microsoft\dotnet"
            $env:Path = "$dotnetPath;$env:Path"
            
            $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
            if ($userPath -notlike "*$dotnetPath*") {
                [Environment]::SetEnvironmentVariable('Path', "$dotnetPath;$userPath", 'User')
                Write-Host "Added .NET to PATH" -ForegroundColor Green
            }
        } finally {
            if (Test-Path $dotnetInstaller) { Remove-Item $dotnetInstaller -Force -ErrorAction SilentlyContinue }
        }
    }
    
    Write-Host ""
    
    # Build and publish
    Write-Host "Building Antty..." -ForegroundColor Yellow
    Push-Location $projectRoot
    try {
        dotnet publish src/Antty.csproj --configuration Release --output publish --framework net10.0
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Build failed!" -ForegroundColor Red
            exit 1
        }
    } finally {
        Pop-Location
    }
    
    Write-Host "Build successful!" -ForegroundColor Green
    Write-Host ""
    
    # Copy to permanent location
    Write-Host "Installing to $installDir..." -ForegroundColor Yellow
    $tempPublish = Join-Path $projectRoot "publish"
    
    if (Test-Path $installDir) {
        Remove-Item $installDir -Recurse -Force
    }
    
    Copy-Item -Path $tempPublish -Destination $installDir -Recurse -Force
    Write-Host "Files copied" -ForegroundColor Green
    Write-Host ""
    
    # Configure PATH
    Write-Host "Configuring PATH..." -ForegroundColor Yellow
    $currentPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    
    # Remove old antty paths first
    $pathParts = $currentPath -split ';' | Where-Object { $_ -notlike '*antty*' -or $_ -eq $installDir }
    $cleanPath = ($pathParts -join ';')
    
    if ($cleanPath -notlike "*$installDir*") {
        [Environment]::SetEnvironmentVariable('Path', "$installDir;$cleanPath", 'User')
        Write-Host "Added to PATH: $installDir" -ForegroundColor Green
        $env:Path = "$installDir;$env:Path"
    } else {
        Write-Host "PATH already configured" -ForegroundColor Green
    }
    
    Write-Host ""
    Write-Host "Installation complete!" -ForegroundColor Green
    Write-Host ""
    Write-Host "IMPORTANT: Restart your terminal, then run 'antty' from any directory!" -ForegroundColor Yellow
    Write-Host ""
    
} catch {
    Write-Host ""
    Write-Host "Installation failed: $_" -ForegroundColor Red
    exit 1
} finally {
    Start-Sleep -Milliseconds 500
    
    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path $tempDir) {
        Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
