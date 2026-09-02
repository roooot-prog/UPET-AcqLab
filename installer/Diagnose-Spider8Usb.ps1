#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Diagnostic USB / Spider8 pe PC-ul tinta (UPET AcqLab).
  Log: %ProgramData%\UPETAcqLab\diagnose-usb.log
#>
$ErrorActionPreference = "Continue"
$logDir = Join-Path $env:ProgramData "UPETAcqLab"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir "diagnose-usb.log"
function W([string]$m) {
    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $m
    Add-Content $log $line -Encoding UTF8
    Write-Host $line
}

W "=== UPET AcqLab — diagnostic USB / Spider8 ==="
W ("PC: {0}  OS: {1}" -f $env:COMPUTERNAME, [Environment]::OSVersion.VersionString)

# 1) USB-serial INF packages from UPET kit (optional helpers)
W "-- Drivere USB-serial din pachetul UPET (FTDI/CH340/CP210x) --"
W "Acestea ajuta DOAR daca Spider8 e pe adaptor USB→COM (apare ca COMx)."
W "Spider8 USB nativ HBM NU foloseste FTDI/CH340."

# 2) HBM USB IO — obligatoriu pentru USBHBM
W "-- Driver HBM USB IO (usbhbm) — OBLIGATORIU pentru Spider8 pe USB --"
$hbmPaths = @(
    "C:\Program Files\HBM\HBM USB IO Driver",
    "C:\Program Files (x86)\HBM\HBM USB IO Driver"
)
$hbmOk = $false
foreach ($p in $hbmPaths) {
    if (Test-Path $p) {
        W "GASIT: $p"
        Get-ChildItem $p -Filter "usbhbm*" -ErrorAction SilentlyContinue | ForEach-Object {
            W ("  {0} ({1} bytes)" -f $_.Name, $_.Length)
        }
        $hbmOk = $true
    }
}
if (-not $hbmOk) {
    W "LIPSA: folder HBM USB IO Driver. Fara acesta Windows NU creeaza USBHBM…"
    W "SOLUTIE: instalati pe acest PC «HBM USB IO Driver» din kitul oficial HBM"
    W "  (catman Easy / Spider8 Setup → DriverSetups → HBM USB IO Driver Setup.exe),"
    W "  apoi reporniti PC-ul, conectati Spider8, deschideti Device Manager."
}

$svc = Get-Service -Name "USBHBM" -ErrorAction SilentlyContinue
if ($svc) {
    W ("Serviciu USBHBM: Status={0} StartType={1}" -f $svc.Status, $svc.StartType)
} else {
    W "Serviciu USBHBM: ABSENT (driver HBM neinstalat)."
}

# 3) PnP devices
W "-- Dispozitive PnP HBM / Spider8 --"
$hbmDev = @()
try {
    $hbmDev = @(Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object {
        $_.InstanceId -match 'VID_10D1|USBHBM' -or $_.FriendlyName -match 'Spider8|HBM USB'
    })
} catch { W "Get-PnpDevice: $($_.Exception.Message)" }

if ($hbmDev.Count -eq 0) {
    W "Niciun dispozitiv VID_10D1 / USBHBM in Device Manager."
    W "Verificati: cablu USB, alimentare Spider8, alt port USB, Driver HBM instalat."
} else {
    foreach ($d in $hbmDev) {
        W ("  Status={0}; Class={1}; Name={2}; Id={3}" -f $d.Status, $d.Class, $d.FriendlyName, $d.InstanceId)
        if ($d.Status -ne "OK") {
            W "  → Status NU e OK: in Device Manager instalati/reparati driverul HBM USB IO."
        }
    }
}

# 4) COM ports
W "-- Porturi COM --"
try {
    $coms = [System.IO.Ports.SerialPort]::GetPortNames() | Sort-Object
    if ($coms) { W ("COM: " + ($coms -join ", ")) }
    else { W "Niciun COMx. Normal daca Spider8 e USBHBM (fara COM)." }
} catch { W "COM list: $($_.Exception.Message)" }

# 5) catman exclusive lock
$catman = Get-Process -Name "catmanEASY","catman","catmanEasy" -ErrorAction SilentlyContinue
if ($catman) {
    W "ATENTIE: catman ruleaza ($(($catman | ForEach-Object Name) -join ', ')). USBHBM e exclusiv — INCHIDETI catman inainte de Connect in UPET."
} else {
    W "catman: nu ruleaza (OK)."
}

# 6) App install
$app = "${env:ProgramFiles(x86)}\UPET AcqLab\UPETAcqLab.exe"
if (Test-Path $app) {
    $ver = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($app).FileVersion
    W "UPET AcqLab instalat: $app (v$ver)"
} else {
    W "UPET AcqLab: nu e in Program Files (x86)\UPET AcqLab"
}

W ""
W "=== Cum conectati in UPET AcqLab ==="
W "1) Backend = HBM USB   (NU Serial, daca nu aveti adaptor COM)"
W "2) Port = USBHBM… din lista (Refresh Porturi)"
W "3) catman Easy INCHIS"
W "4) Connect"
W "Demo fara hardware: Backend = Simulator → Connect → Start"
W ""
W "Log salvat: $log"
Write-Host ""
Write-Host "Log: $log"
pause
