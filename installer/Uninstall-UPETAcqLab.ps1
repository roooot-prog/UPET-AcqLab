#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Dezinstaleaza UPET AcqLab (Program Files, scurtaturi, inregistrare Apps & Features).
  Nu elimina driverele USB/COM (raman disponibile pentru alte aplicatii).
#>
param(
    [string]$InstallDir = "${env:ProgramFiles(x86)}\UPET AcqLab",
    [switch]$Quiet
)

$ErrorActionPreference = "Continue"
$logDir = Join-Path $env:ProgramData "UPETAcqLab"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir "uninstall.log"
function Write-Log([string]$m) {
    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $m
    Add-Content $log $line -Encoding UTF8
    Write-Host $line
}

Write-Log "=== UPET AcqLab Uninstall ==="
Write-Log "InstallDir=$InstallDir"

if (-not $Quiet) {
    $r = Read-Host "Dezinstalati UPET AcqLab din '$InstallDir'? (D/N)"
    if ($r -notmatch '^[DdYy]') {
        Write-Log "Anulat de utilizator."
        exit 0
    }
}

# Stop running app
Get-Process -Name "UPETAcqLab","Spider8DAQ" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Log "Opreste proces: $($_.Name) ($($_.Id))"
    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Milliseconds 400

# Shortcuts
$wshTargets = New-Object System.Collections.Generic.List[string]
$desktopCommon = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)
$userDesktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
if ([string]::IsNullOrWhiteSpace($desktopCommon)) { $desktopCommon = Join-Path $env:PUBLIC "Desktop" }
$startMenuCommon = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonStartMenu)) "Programs"
$startMenuUser = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::StartMenu)) "Programs"

foreach ($d in @($desktopCommon, $userDesktop)) {
    if ([string]::IsNullOrWhiteSpace($d)) { continue }
    $wshTargets.Add((Join-Path $d "UPET AcqLab.lnk"))
    $wshTargets.Add((Join-Path $d "Dezinstalare UPET AcqLab.lnk"))
    $wshTargets.Add((Join-Path $d "Deschide UPET AcqLab.bat"))
    $wshTargets.Add((Join-Path $d "Diagnostic Spider8 USB.lnk"))
    $wshTargets.Add((Join-Path $d "Citeste-ma USB Spider8.lnk"))
}
foreach ($sm in @($startMenuCommon, $startMenuUser)) {
    if ([string]::IsNullOrWhiteSpace($sm)) { continue }
    $wshTargets.Add((Join-Path $sm "UPET AcqLab.lnk"))
    $wshTargets.Add((Join-Path $sm "Dezinstalare UPET AcqLab.lnk"))
    $folder = Join-Path $sm "UPET AcqLab"
    if (Test-Path $folder) {
        Write-Log "Sterge folder Start: $folder"
        Remove-Item $folder -Recurse -Force -ErrorAction SilentlyContinue
    }
}

foreach ($p in $wshTargets) {
    if (Test-Path -LiteralPath $p) {
        Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue
        Write-Log "Sters: $p"
    }
}

# Uninstall registry (Apps & Features) — both 32/64 views
$uninstallKeys = @(
    "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\UPETAcqLab",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\UPETAcqLab"
)
foreach ($k in $uninstallKeys) {
    if (Test-Path $k) {
        Remove-Item $k -Recurse -Force -ErrorAction SilentlyContinue
        Write-Log "Sterge cheie: $k"
    }
}

# Application files
if (Test-Path -LiteralPath $InstallDir) {
    Write-Log "Sterge folder aplicatie..."
    try {
        Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction Stop
        Write-Log "Sters: $InstallDir"
    } catch {
        Write-Log "WARN: nu am putut sterge tot folderul: $($_.Exception.Message)"
        # Retry after short delay (file locks)
        Start-Sleep -Seconds 1
        Remove-Item -LiteralPath $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Log "Folder instalare deja absent."
}

Write-Log "Dezinstalare finalizata. (Licenta locala / inregistrari raman in %LocalAppData%\UPETAcqLab daca exista.)"
Write-Log "Log: $log"
if (-not $Quiet) {
    Write-Host ""
    Write-Host "UPET AcqLab a fost dezinstalat."
    Write-Host "Log: $log"
    pause
}
exit 0
