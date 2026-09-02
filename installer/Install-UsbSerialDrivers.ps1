#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Instaleaza TOATE pachetele INF din installer\drivers\** (FTDI, CH340, CP210x, PL2303, etc.)
  Log: %ProgramData%\UPETAcqLab\driver-install.log
#>
param(
    [string]$DriversRoot = "",
    [switch]$SkipVcRedist
)

$ErrorActionPreference = "Continue"
$logDir = Join-Path $env:ProgramData "UPETAcqLab"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$log = Join-Path $logDir "driver-install.log"

function Write-Log([string]$msg) {
    $line = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $msg
    Add-Content -Path $log -Value $line -Encoding UTF8
    Write-Host $line
}

if ([string]::IsNullOrWhiteSpace($DriversRoot)) {
    $DriversRoot = Join-Path $PSScriptRoot "drivers"
}

Write-Log "=== UPET AcqLab driver install ==="
Write-Log "DriversRoot=$DriversRoot"

if (-not (Test-Path $DriversRoot)) {
    Write-Log "WARN: folder drivers lipseste - skip."
    exit 0
}

$infFiles = Get-ChildItem -Path $DriversRoot -Filter *.inf -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\vcredist\\' -and $_.FullName -notmatch '\\hbm-usb-io\\' }

Write-Log ("INF gasite: {0}" -f @($infFiles).Count)
$ok = 0
$fail = 0

foreach ($inf in $infFiles) {
    Write-Log ("pnputil /add-driver `"$($inf.FullName)`" /install")
    $out = & pnputil.exe /add-driver $inf.FullName /install 2>&1 | Out-String
    Write-Log $out.Trim()
    if ($LASTEXITCODE -eq 0 -or $out -match "successfully|already|Published name") {
        $ok++
    } else {
        $fail++
        Write-Log "WARN: exit=$LASTEXITCODE for $($inf.Name)"
    }
}

if (-not $SkipVcRedist) {
    $vc = Get-ChildItem -Path (Join-Path $DriversRoot "vcredist") -Filter "*.exe" -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($vc) {
        Write-Log "VC++ redist: $($vc.FullName)"
        $p = Start-Process -FilePath $vc.FullName -ArgumentList "/install","/quiet","/norestart" -Wait -PassThru
        Write-Log "VC++ exit=$($p.ExitCode)"
    } else {
        Write-Log "VC++ redist: nu este in drivers\vcredist (skip)"
    }
}

$cpExe = Get-ChildItem -Path (Join-Path $DriversRoot "cp210x") -Filter "CP210xVCPInstaller*.exe" -ErrorAction SilentlyContinue |
    Sort-Object Length -Descending | Select-Object -First 1
if ($cpExe) {
    Write-Log "CP210x EXE: $($cpExe.FullName)"
    $p = Start-Process -FilePath $cpExe.FullName -ArgumentList "/S" -Wait -PassThru -ErrorAction SilentlyContinue
    if ($p) { Write-Log "CP210x EXE exit=$($p.ExitCode)" }
}

Write-Log "HBM Spider32.dll: nu se instaleaza din acest pachet. Plasati fisierul oficial in vendor\ daca este disponibil."
Write-Log "HBM USB IO (usbhbm): NU se redistribuie. Vezi installer\drivers\hbm-usb-io\README.md"
$hbmIoOk = $false
foreach ($hbmIo in @("C:\Program Files\HBM\HBM USB IO Driver", "C:\Program Files (x86)\HBM\HBM USB IO Driver")) {
    if (Test-Path $hbmIo) {
        Write-Log "HBM USB IO Driver instalat: $hbmIo"
        Get-ChildItem $hbmIo -Filter "usbhbm*" -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Log ("  {0} ({1} bytes)" -f $_.Name, $_.Length)
        }
        $hbmIoOk = $true
    }
}
if (-not $hbmIoOk) {
    Write-Log "HBM USB IO Driver: ABSENT. Instalati pachetul oficial HBM daca folositi Spider8 pe USB."
}

try {
    $hbm = Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object {
        $_.InstanceId -match 'VID_10D1|USBHBM' -or $_.FriendlyName -match 'Spider8|HBM'
    }
    if ($hbm) {
        foreach ($d in $hbm) {
            Write-Log ("HBM USB detectat: Status={0}; Class={1}; Name={2}; Id={3}" -f $d.Status, $d.Class, $d.FriendlyName, $d.InstanceId)
        }
        Write-Log "UPET AcqLab: pentru USBHBM folositi backend HBM USB (nu Serial/COM)."
    } else {
        Write-Log "HBM USB: niciun dispozitiv VID_10D1 / USBHBM in Device Manager acum."
    }
} catch {
    Write-Log ("HBM USB detect: $($_.Exception.Message)")
}

Write-Log ("Rezumat: INF ok~=$ok warn/fail~=$fail - log=$log")
if (@($infFiles).Count -le 1) {
    Write-Log "WARN: putine fisiere INF in pachet. FTDI/CH340/PL2303 pot lipsi (doar DOWNLOAD.txt)."
}

Write-Host ""
Write-Host "============================================================"
Write-Host " IMPORTANT — Spider8 pe USB nativ (fara adaptor COM)"
Write-Host "============================================================"
Write-Host " Pachetul UPET instaleaza drivere USB-serial (FTDI/CH340/...)."
Write-Host " Acestea NU inlocuiesc driverul HBM USB IO (usbhbm.sys)."
Write-Host ""
if (-not $hbmIoOk) {
    Write-Host " PE ACEST PC LIPSESTE: HBM USB IO Driver"
    Write-Host " → Instalati din kitul oficial HBM / catman:"
    Write-Host "   DriverSetups\HBM USB IO Driver Setup.exe"
    Write-Host " → Apoi reporniti PC-ul si conectati Spider8."
    Write-Host " In UPET: Backend = HBM USB, Port = USBHBM…"
} else {
    Write-Host " HBM USB IO Driver: prezent pe acest PC."
}
Write-Host " Demo fara hardware: Backend = Simulator."
Write-Host "============================================================"
Write-Host ""

exit 0
