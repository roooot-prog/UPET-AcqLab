#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Instalator elevat UPET AcqLab: aplicatie, drivere USB-serial, ghid USB Spider8,
  diagnostic, scurtaturi Start Menu, dezinstalator (Apps & Features).
#>
param(
    [string]$PayloadDir = "",
    [string]$InstallDir = "${env:ProgramFiles(x86)}\UPET AcqLab",
    [switch]$SkipDrivers
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($PayloadDir)) {
    $PayloadDir = Join-Path $root "publish-v2"
}

$logDir = Join-Path $env:ProgramData "UPETAcqLab"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$setupLog = Join-Path $logDir "setup.log"
function Write-Log([string]$m) {
    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $m
    Add-Content $setupLog $line -Encoding UTF8
    Write-Host $line
}

function Find-InstallScript([string]$name) {
    foreach ($c in @(
        (Join-Path $PSScriptRoot $name),
        (Join-Path $PayloadDir $name),
        (Join-Path $PayloadDir ("installer\" + $name)),
        (Join-Path $InstallDir $name)
    )) {
        if (Test-Path $c) { return $c }
    }
    return $null
}

Write-Log "=== UPET AcqLab Setup ==="
Write-Log "Payload=$PayloadDir"
Write-Log "InstallDir=$InstallDir"

if (-not (Test-Path (Join-Path $PayloadDir "UPETAcqLab.exe"))) {
    throw "Payload invalid - lipseste UPETAcqLab.exe in $PayloadDir. Rulati mai intai publish."
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Write-Log "Copiere fisiere..."
Copy-Item -Path (Join-Path $PayloadDir "*") -Destination $InstallDir -Recurse -Force

$extraNames = @(
    "Uninstall-UPETAcqLab.ps1",
    "Uninstall-UPETAcqLab.cmd",
    "Diagnose-Spider8Usb.ps1",
    "Diagnose-Spider8Usb.cmd",
    "Install-UsbSerialDrivers.ps1",
    "CITESTE-MA-USB-SPIDER8.txt",
    "CITESTE-MA-INSTALARE.txt"
)
foreach ($name in $extraNames) {
    $src = Find-InstallScript $name
    if ($src) {
        Copy-Item $src (Join-Path $InstallDir $name) -Force
        Write-Log "Copiat: $name"
    } else {
        Write-Log "WARN: lipseste $name in pachet"
    }
}

$usbGuide = Join-Path $InstallDir "CITESTE-MA-USB-SPIDER8.txt"
$guideSrc = Find-InstallScript "CITESTE-MA-USB-SPIDER8.txt"
if ($guideSrc -and $guideSrc -ne $usbGuide) {
    Copy-Item $guideSrc $usbGuide -Force
}

$vendorDll = Join-Path $root "vendor\Spider32.dll"
if (Test-Path $vendorDll) {
    $destVendor = Join-Path $InstallDir "vendor"
    New-Item -ItemType Directory -Force -Path $destVendor | Out-Null
    Copy-Item $vendorDll (Join-Path $destVendor "Spider32.dll") -Force
    Write-Log "Copiat vendor\Spider32.dll"
} else {
    Write-Log "Spider32.dll absent - optional; USB nativ foloseste backend HBM USB"
}

$exe = Join-Path $InstallDir "UPETAcqLab.exe"
$uninstallCmd = Join-Path $InstallDir "Uninstall-UPETAcqLab.cmd"
$uninstallPs1 = Join-Path $InstallDir "Uninstall-UPETAcqLab.ps1"
$diagnoseCmd = Join-Path $InstallDir "Diagnose-Spider8Usb.cmd"
$wsh = New-Object -ComObject WScript.Shell
$desktopCommon = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonDesktopDirectory)
$userDesktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
if ([string]::IsNullOrWhiteSpace($desktopCommon)) { $desktopCommon = Join-Path $env:PUBLIC "Desktop" }
if ([string]::IsNullOrWhiteSpace($userDesktop)) { $userDesktop = [Environment]::GetFolderPath("Desktop") }

$startMenuCommon = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::CommonStartMenu)) "Programs"
$startMenuUser = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::StartMenu)) "Programs"
if ([string]::IsNullOrWhiteSpace($startMenuCommon) -or -not (Test-Path (Split-Path $startMenuCommon -Parent))) {
    $startMenuCommon = $startMenuUser
}
New-Item -ItemType Directory -Force -Path $startMenuCommon | Out-Null
if ($startMenuUser -and ($startMenuUser -ne $startMenuCommon)) {
    New-Item -ItemType Directory -Force -Path $startMenuUser | Out-Null
}

$ico = Join-Path $InstallDir "app.ico"
if (-not (Test-Path $ico)) {
    foreach ($c in @(
        (Join-Path $PayloadDir "app.ico"),
        (Join-Path $PayloadDir "Assets\app.ico"),
        (Join-Path $root "Spider8DAQ.App\Assets\app.ico")
    )) {
        if (Test-Path $c) { Copy-Item $c $ico -Force; Write-Log "Copiat icon: $c"; break }
    }
}
$iconLoc = if (Test-Path $ico) { "$ico,0" } else { "$exe,0" }

# Asociere .upet → iconiță logo UPET + deschidere cu UPET AcqLab
try {
    $progId = "UPETAcqLab.Report.1"
    foreach ($ext in @(".upet", ".upetr")) {
        New-Item -Path "HKCU:\Software\Classes\$ext" -Force | Out-Null
        Set-ItemProperty -Path "HKCU:\Software\Classes\$ext" -Name "(default)" -Value $progId
    }
    New-Item -Path "HKCU:\Software\Classes\$progId" -Force | Out-Null
    Set-ItemProperty -Path "HKCU:\Software\Classes\$progId" -Name "(default)" -Value "Raport UPET AcqLab"
    New-Item -Path "HKCU:\Software\Classes\$progId\DefaultIcon" -Force | Out-Null
    Set-ItemProperty -Path "HKCU:\Software\Classes\$progId\DefaultIcon" -Name "(default)" -Value $iconLoc
    New-Item -Path "HKCU:\Software\Classes\$progId\shell\open\command" -Force | Out-Null
    Set-ItemProperty -Path "HKCU:\Software\Classes\$progId\shell\open\command" -Name "(default)" -Value "`"$exe`" `"%1`""
    Write-Log "Asociere .upet inregistrata (icon UPET)"
} catch {
    Write-Log "Asociere .upet: $_"
}

$linkPaths = New-Object System.Collections.Generic.List[string]
foreach ($d in @($desktopCommon, $userDesktop)) {
    if (-not [string]::IsNullOrWhiteSpace($d) -and (Test-Path $d)) {
        $lp = Join-Path $d "UPET AcqLab.lnk"
        if (-not $linkPaths.Contains($lp)) { $linkPaths.Add($lp) }
    }
}

$startFolders = New-Object System.Collections.Generic.List[string]
foreach ($sm in @($startMenuCommon, $startMenuUser)) {
    if ([string]::IsNullOrWhiteSpace($sm)) { continue }
    $folder = Join-Path $sm "UPET AcqLab"
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    if (-not $startFolders.Contains($folder)) { $startFolders.Add($folder) }
    $legacy = Join-Path $sm "UPET AcqLab.lnk"
    if (Test-Path $legacy) { Remove-Item $legacy -Force -ErrorAction SilentlyContinue }
}

foreach ($linkPath in $linkPaths) {
    $sc = $wsh.CreateShortcut($linkPath)
    $sc.TargetPath = $exe
    $sc.WorkingDirectory = $InstallDir
    $sc.Description = "UPET AcqLab - Universitatea din Petrosani"
    $sc.IconLocation = $iconLoc
    $sc.Save()
    Write-Log "Scurtatura: $linkPath"
}

foreach ($folder in $startFolders) {
    $appLnk = Join-Path $folder "UPET AcqLab.lnk"
    $sc = $wsh.CreateShortcut($appLnk)
    $sc.TargetPath = $exe
    $sc.WorkingDirectory = $InstallDir
    $sc.Description = "UPET AcqLab - Universitatea din Petrosani"
    $sc.IconLocation = $iconLoc
    $sc.Save()

    if (Test-Path $uninstallCmd) {
        $uLnk = Join-Path $folder "Dezinstalare UPET AcqLab.lnk"
        $uc = $wsh.CreateShortcut($uLnk)
        $uc.TargetPath = $uninstallCmd
        $uc.WorkingDirectory = $InstallDir
        $uc.Description = "Dezinstaleaza UPET AcqLab"
        $uc.IconLocation = $iconLoc
        $uc.Save()
        Write-Log "Scurtatura dezinstalare: $uLnk"
    }

    if (Test-Path $diagnoseCmd) {
        $dLnk = Join-Path $folder "Diagnostic Spider8 USB.lnk"
        $dc = $wsh.CreateShortcut($dLnk)
        $dc.TargetPath = $diagnoseCmd
        $dc.WorkingDirectory = $InstallDir
        $dc.Description = "Diagnostic driver HBM USB / USBHBM / Connect"
        $dc.IconLocation = $iconLoc
        $dc.Save()
        Write-Log "Scurtatura diagnostic: $dLnk"
    }

    if (Test-Path $usbGuide) {
        $gLnk = Join-Path $folder "Citeste-ma USB Spider8.lnk"
        $gc = $wsh.CreateShortcut($gLnk)
        $gc.TargetPath = $usbGuide
        $gc.WorkingDirectory = $InstallDir
        $gc.Description = "Ghid conectare Spider8 pe USB"
        $gc.Save()
        Write-Log "Scurtatura ghid USB: $gLnk"
    }
}

foreach ($d in @($userDesktop, $desktopCommon)) {
    if ([string]::IsNullOrWhiteSpace($d)) { continue }
    $legacyBat = Join-Path $d "Deschide UPET AcqLab.bat"
    if (Test-Path $legacyBat) {
        Remove-Item $legacyBat -Force
        Write-Log "Eliminat launcher vechi: $legacyBat"
    }
}

$displayVersion = "3.3.21"
try {
    $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
    if ($vi.FileVersion) { $displayVersion = $vi.FileVersion }
} catch { }

$uninstallString = if (Test-Path $uninstallCmd) { "`"$uninstallCmd`"" }
    elseif (Test-Path $uninstallPs1) { "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallPs1`" -Quiet" }
    else { "" }

if (-not [string]::IsNullOrWhiteSpace($uninstallString)) {
    foreach ($reg in @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\UPETAcqLab",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\UPETAcqLab"
    )) {
        try {
            New-Item -Path $reg -Force | Out-Null
            New-ItemProperty -Path $reg -Name "DisplayName" -Value "UPET AcqLab" -PropertyType String -Force | Out-Null
            New-ItemProperty -Path $reg -Name "DisplayVersion" -Value $displayVersion -PropertyType String -Force | Out-Null
            New-ItemProperty -Path $reg -Name "Publisher" -Value "Universitatea din Petroșani" -PropertyType String -Force | Out-Null
            New-ItemProperty -Path $reg -Name "InstallLocation" -Value $InstallDir -PropertyType String -Force | Out-Null
            New-ItemProperty -Path $reg -Name "DisplayIcon" -Value $iconLoc -PropertyType String -Force | Out-Null
            New-ItemProperty -Path $reg -Name "UninstallString" -Value $uninstallString -PropertyType String -Force | Out-Null
            New-ItemProperty -Path $reg -Name "QuietUninstallString" -Value ("powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallPs1`" -Quiet") -PropertyType String -Force | Out-Null
            New-ItemProperty -Path $reg -Name "NoModify" -Value 1 -PropertyType DWord -Force | Out-Null
            New-ItemProperty -Path $reg -Name "NoRepair" -Value 1 -PropertyType DWord -Force | Out-Null
            New-ItemProperty -Path $reg -Name "EstimatedSize" -Value ([int]((Get-ChildItem $InstallDir -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum / 1KB)) -PropertyType DWord -Force | Out-Null
            Write-Log "Inregistrat Apps & Features: $reg"
        } catch {
            Write-Log "WARN registry $reg : $($_.Exception.Message)"
        }
    }
}

if (-not $SkipDrivers) {
    $drvScript = Find-InstallScript "Install-UsbSerialDrivers.ps1"
    $drvRoot = Join-Path $PSScriptRoot "drivers"
    if (-not (Test-Path $drvRoot)) { $drvRoot = Join-Path $PayloadDir "installer\drivers" }
    if (-not (Test-Path $drvRoot)) { $drvRoot = Join-Path $InstallDir "installer\drivers" }
    if ($drvScript -and (Test-Path $drvRoot)) {
        Write-Log "Instalare drivere USB/COM din $drvRoot ..."
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $drvScript -DriversRoot $drvRoot
        Write-Log "Driver script exit=$LASTEXITCODE"
    } else {
        Write-Log "WARN: nu am gasit Install-UsbSerialDrivers.ps1 sau folder drivers"
    }
} else {
    Write-Log "Skip drivers (-SkipDrivers)"
}

$hbmOk = (Test-Path "C:\Program Files\HBM\HBM USB IO Driver") -or
         (Test-Path "C:\Program Files (x86)\HBM\HBM USB IO Driver")
Write-Log ("HBM USB IO Driver prezent: {0}" -f $hbmOk)

Write-Host ""
Write-Host "============================================================"
Write-Host " UPET AcqLab — instalare completa: $InstallDir"
Write-Host "============================================================"
Write-Host " Start Menu: UPET AcqLab"
Write-Host "   · UPET AcqLab"
Write-Host "   · Diagnostic Spider8 USB"
Write-Host "   · Citeste-ma USB Spider8"
Write-Host "   · Dezinstalare UPET AcqLab"
Write-Host ""
if (-not $hbmOk) {
    Write-Host " ATENTIE: pe acest PC LIPSESTE HBM USB IO Driver."
    Write-Host " Spider8 pe USB nativ NU va functiona pana il instalati din kitul HBM/catman:"
    Write-Host "   DriverSetups\HBM USB IO Driver Setup.exe"
    Write-Host " Apoi: Backend = HBM USB, Port = USBHBM…, catman INCHIS."
    Write-Host " Deschid ghidul CITESTE-MA-USB-SPIDER8.txt ..."
    if (Test-Path $usbGuide) {
        Start-Process -FilePath "notepad.exe" -ArgumentList "`"$usbGuide`"" -ErrorAction SilentlyContinue
    }
} else {
    Write-Host " HBM USB IO Driver: OK pe acest PC."
    Write-Host " In UPET: Backend = HBM USB, Port = USBHBM… (catman inchis)."
}
Write-Host " Demo fara hardware: Backend = Simulator."
Write-Host " Loguri: $logDir"
Write-Host "============================================================"
Write-Host ""

Write-Log "Setup finalizat."
if (-not $hbmOk) {
    Write-Log "SETUP WARN: HBM USB IO Driver absent — Connect pe USB nativ va esua."
}
