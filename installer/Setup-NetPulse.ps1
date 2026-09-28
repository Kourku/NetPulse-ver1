<#
.SYNOPSIS
    NetPulse One-Click Windows Installer Script
.DESCRIPTION
    Installs NetPulse self-contained application, creates desktop and start menu shortcuts,
    and registers uninstaller in Windows Add/Remove Programs.
#>

param(
    [string]$InstallPath = "$env:LOCALAPPDATA\Programs\NetPulse",
    [switch]$NoShortcuts,
    [switch]$Launch
)

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "   NetPulse Diagnostics Installer v1.0.0" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$SourceExe = "$ScriptDir\..\dist\portable\NetPulse.exe"
if (-not (Test-Path $SourceExe)) {
    $SourceExe = "$ScriptDir\payload\NetPulse.exe"
}
if (-not (Test-Path $SourceExe)) {
    $SourceExe = "$ScriptDir\NetPulse.exe"
}

if (-not (Test-Path $SourceExe)) {
    Write-Error "Could not find NetPulse.exe binary. Build or copy it first."
    exit 1
}

Write-Host "[1/4] Creating installation directory at $InstallPath..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null

Write-Host "[2/4] Copying files..." -ForegroundColor Yellow
Copy-Item $SourceExe "$InstallPath\NetPulse.exe" -Force
$IconSource = "$ScriptDir\..\src\NetPulse.App\app.ico"
if (Test-Path $IconSource) {
    Copy-Item $IconSource "$InstallPath\app.ico" -Force
}

if (-not $NoShortcuts) {
    Write-Host "[3/4] Creating Desktop and Start Menu shortcuts..." -ForegroundColor Yellow
    $WshShell = New-Object -ComObject WScript.Shell

    # Desktop shortcut
    $DesktopPath = [Environment]::GetFolderPath("Desktop")
    $Shortcut = $WshShell.CreateShortcut("$DesktopPath\NetPulse.lnk")
    $Shortcut.TargetPath = "$InstallPath\NetPulse.exe"
    $Shortcut.WorkingDirectory = $InstallPath
    $Shortcut.Description = "NetPulse - Real-time Network Quality Diagnostics"
    if (Test-Path "$InstallPath\app.ico") {
        $Shortcut.IconLocation = "$InstallPath\app.ico"
    }
    $Shortcut.Save()

    # Start Menu shortcut
    $ProgramsPath = [Environment]::GetFolderPath("Programs")
    $ShortcutStart = $WshShell.CreateShortcut("$ProgramsPath\NetPulse.lnk")
    $ShortcutStart.TargetPath = "$InstallPath\NetPulse.exe"
    $ShortcutStart.WorkingDirectory = $InstallPath
    $ShortcutStart.Description = "NetPulse - Real-time Network Quality Diagnostics"
    if (Test-Path "$InstallPath\app.ico") {
        $ShortcutStart.IconLocation = "$InstallPath\app.ico"
    }
    $ShortcutStart.Save()
}

Write-Host "[4/4] Registering with Windows Add/Remove Programs..." -ForegroundColor Yellow
$UninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\NetPulse"
New-Item -Path $UninstallKey -Force | Out-Null
Set-ItemProperty -Path $UninstallKey -Name "DisplayName" -Value "NetPulse Real-Time Network Quality & Path Diagnostics"
Set-ItemProperty -Path $UninstallKey -Name "DisplayVersion" -Value "1.0.0"
Set-ItemProperty -Path $UninstallKey -Name "Publisher" -Value "NetPulse Diagnostics"
Set-ItemProperty -Path $UninstallKey -Name "DisplayIcon" -Value "$InstallPath\NetPulse.exe,0"
Set-ItemProperty -Path $UninstallKey -Name "InstallLocation" -Value $InstallPath
Set-ItemProperty -Path $UninstallKey -Name "UninstallString" -Value "powershell.exe -ExecutionPolicy Bypass -File `"$InstallPath\Uninstall.ps1`""

# Create uninstaller script
$UninstallScript = @"
Write-Host "Uninstalling NetPulse..." -ForegroundColor Yellow
Remove-Item "$DesktopPath\NetPulse.lnk" -ErrorAction SilentlyContinue
Remove-Item "$ProgramsPath\NetPulse.lnk" -ErrorAction SilentlyContinue
Remove-Item -Path "$UninstallKey" -Recurse -ErrorAction SilentlyContinue
Start-Process cmd.exe -ArgumentList '/c timeout /t 2 /nobreak > NUL & rmdir /s /q "$InstallPath"' -WindowStyle Hidden
Write-Host "NetPulse uninstalled successfully." -ForegroundColor Green
"@
Set-Content -Path "$InstallPath\Uninstall.ps1" -Value $UninstallScript

Write-Host "==========================================" -ForegroundColor Green
Write-Host " Installation Successful!" -ForegroundColor Green
Write-Host " Location: $InstallPath\NetPulse.exe" -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green

if ($Launch) {
    Start-Process "$InstallPath\NetPulse.exe"
}
