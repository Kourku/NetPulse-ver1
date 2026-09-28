<#
.SYNOPSIS
    Automated build, test, and release packaging script for NetPulse
#>

$ErrorActionPreference = "Stop"
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host " NetPulse - Master Build, Test, and Packaging Script " -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan

$RootDir = $PSScriptRoot
Set-Location $RootDir

# Step 1: Run Tests
Write-Host "`n[1/5] Running automated test suite..." -ForegroundColor Yellow
dotnet test tests/NetPulse.Tests/NetPulse.Tests.csproj -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "Tests failed! Aborting build."
    exit 1
}
Write-Host "All tests passed successfully!" -ForegroundColor Green

# Step 2: Publish App (Self-contained win-x64 single file)
Write-Host "`n[2/5] Publishing NetPulse.App (win-x64 self-contained single file)..." -ForegroundColor Yellow
$PortableDir = "$RootDir\dist\portable"
New-Item -ItemType Directory -Path $PortableDir -Force | Out-Null

dotnet publish src/NetPulse.App/NetPulse.App.csproj -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $PortableDir

Copy-Item "$RootDir\src\NetPulse.App\app.ico" "$PortableDir\app.ico" -Force
Write-Host "Application binary published to $PortableDir\NetPulse.exe" -ForegroundColor Green

# Step 3: Package Portable ZIP
Write-Host "`n[3/5] Packaging Portable ZIP archive..." -ForegroundColor Yellow
$ZipPath = "$RootDir\dist\NetPulse-1.0.0-Portable-win-x64.zip"
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path "$PortableDir\*" -DestinationPath $ZipPath -Force
$zipSize = (Get-Item $ZipPath).Length / 1MB
Write-Host "Portable archive created: $ZipPath ($([math]::Round($zipSize, 1)) MB)" -ForegroundColor Green

# Step 4: Publish Installer (Standalone Setup.exe)
Write-Host "`n[4/5] Publishing Standalone Windows Installer (NetPulse-Setup.exe)..." -ForegroundColor Yellow
$InstallerDir = "$RootDir\dist\installer"
New-Item -ItemType Directory -Path $InstallerDir -Force | Out-Null
New-Item -ItemType Directory -Path "$InstallerDir\payload" -Force | Out-Null

dotnet publish src/NetPulse.Installer/NetPulse.Installer.csproj -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $InstallerDir

Copy-Item "$PortableDir\NetPulse.exe" "$InstallerDir\payload\NetPulse.exe" -Force
Copy-Item "$RootDir\src\NetPulse.App\app.ico" "$InstallerDir\payload\app.ico" -Force
Copy-Item "$RootDir\installer\Setup-NetPulse.ps1" "$InstallerDir\Setup-NetPulse.ps1" -Force

Write-Host "Standalone installer built at $InstallerDir\NetPulse-Setup.exe" -ForegroundColor Green

# Step 5: Summary
Write-Host "`n=====================================================" -ForegroundColor Green
Write-Host " NetPulse Build & Release Packaging Complete!        " -ForegroundColor Green
Write-Host "=====================================================" -ForegroundColor Green
Write-Host "Deliverables:"
Write-Host " 1. Portable Executable:   $PortableDir\NetPulse.exe"
Write-Host " 2. Portable ZIP Archive:  $ZipPath"
Write-Host " 3. Windows Setup (.exe):  $InstallerDir\NetPulse-Setup.exe"
Write-Host " 4. PowerShell Setup:      $InstallerDir\Setup-NetPulse.ps1"
Write-Host " 5. Inno Setup Script:     $RootDir\installer\NetPulse.iss"
Write-Host "=====================================================" -ForegroundColor Green
