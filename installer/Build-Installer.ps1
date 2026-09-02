param(
    [switch]$SkipPublish,
    [switch]$SkipDriverFetch,
    [switch]$SkipDesktopCopy
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$dotnet = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) { $dotnet = "dotnet" }

$env:Path = "C:\Program Files\dotnet;" + $env:Path
Set-Location $root

$csproj = Join-Path $root "Spider8DAQ.App\Spider8DAQ.App.csproj"
$appVersion = "3.3.21"
if (Test-Path $csproj) {
    $m = Select-String -Path $csproj -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    if ($m) { $appVersion = $m.Matches[0].Groups[1].Value }
}

$publish = Join-Path $root "publish-v2"
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

if (-not $SkipPublish) {
    Write-Host "== Publish self-contained win-x86 v$appVersion =="
    & $dotnet publish (Join-Path $root "Spider8DAQ.App\Spider8DAQ.App.csproj") -c Release -r win-x86 --self-contained true -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publish
    if ($LASTEXITCODE -ne 0) { throw "publish failed" }

    $srcIco = Join-Path $root "Spider8DAQ.App\Assets\app.ico"
    if (Test-Path $srcIco) {
        Copy-Item $srcIco (Join-Path $publish "app.ico") -Force
    }

    # PublishSingleFile can omit native vendor binaries; force-copy what the app needs beside the EXE.
    $vendorSrc = Join-Path $root "vendor"
    $vendorDst = Join-Path $publish "vendor"
    New-Item -ItemType Directory -Force -Path $vendorDst | Out-Null
    foreach ($dll in @("Spider32.dll", "Interlnk.dll", "Intfac32.dll", "Papo32.dll")) {
        $src = Join-Path $vendorSrc $dll
        if (Test-Path -LiteralPath $src) {
            Copy-Item $src (Join-Path $vendorDst $dll) -Force
        }
    }
    foreach ($doc in @("sensors.json", "README.md", "README-Spider32.md")) {
        $src = Join-Path $vendorSrc $doc
        if (Test-Path -LiteralPath $src) {
            Copy-Item $src (Join-Path $vendorDst $doc) -Force
        }
    }
    $ntSrc = Join-Path $vendorSrc "nt_iodrv"
    $ntDst = Join-Path $vendorDst "nt_iodrv"
    if (Test-Path -LiteralPath $ntSrc) {
        New-Item -ItemType Directory -Force -Path $ntDst | Out-Null
        Copy-Item -Path (Join-Path $ntSrc "*") -Destination $ntDst -Force
    }

    $manualSrc = Join-Path $root "docs\Manual-UPET-AcqLab.pdf"
    if (Test-Path -LiteralPath $manualSrc) {
        $docsDst = Join-Path $publish "docs"
        New-Item -ItemType Directory -Force -Path $docsDst | Out-Null
        Copy-Item $manualSrc (Join-Path $docsDst "Manual-UPET-AcqLab.pdf") -Force
    }

    & $dotnet publish (Join-Path $root "tools\LicenseGen\LicenseGen.csproj") -c Release -o (Join-Path $root "tools\LicenseGen\bin\Release\net8.0")
}

if (-not (Test-Path (Join-Path $publish "UPETAcqLab.exe"))) {
    throw "Lipseste publish-v2\UPETAcqLab.exe — rulati fara -SkipPublish."
}

if (-not $SkipDriverFetch) {
    Write-Host "== Fetch USB-serial drivers =="
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Fetch-Drivers.ps1")
}

$readme = @"
================================================================================
  UPET AcqLab v$appVersion — pachet instalare (Universitatea din Petroșani)
================================================================================

PE ALT PC (Windows 10/11, 32 sau 64 bit):

  Varianta A — INSTALARE COMPLETA (recomandat)
  -------------------------------------------
  1. Copiati intregul folder pe USB / retea pe PC-ul tinta.
  2. Clic dreapta pe UPETAcqLab-Setup.cmd → Ruleaza ca administrator.
  3. Asteptati copierea in:
       C:\Program Files (x86)\UPET AcqLab\
  4. Pe Desktop / Start apare scurtatura «UPET AcqLab».
  5. La primul start: activati licenta (vezi LICENSE-PERSONAL.txt).
  6. Raport proprietar: Analiza → Export .upet (doar UPET AcqLab il citeste;
     iconita logo UPET in Explorer dupa instalare / prima pornire).
  7. Dezinstalare: Start → UPET AcqLab → «Dezinstalare UPET AcqLab»
     sau Setari Windows → Aplicatii → UPET AcqLab → Dezinstalare.

  Varianta B — FARA instalare (portabil)
  --------------------------------------
  1. Extrati UPETAcqLab-Portable.zip undeva (ex. D:\UPET\).
  2. Porniti UPETAcqLab.exe din folderul extras.
  3. Demo: Backend = Simulator → Connect → Start.

CERINTE
  - Nu e nevoie de .NET Runtime separat (self-contained win-x86).
  - Administrator doar pentru instalare + drivere USB/COM.
  - Simulator functioneaza fara hardware / fara drivere.
  - Spider8 pe adaptor USB–serial: driverele din pachet (FTDI/CH340/…).
  - Spider8 USB nativ HBM (USBHBM): driver oficial HBM (nu e redistribuit).
    Instalati pe PC-ul tinta «HBM USB IO Driver» din kit HBM/catman,
    apoi in UPET: Backend = HBM USB, Port = USBHBM…
  - Diagnostic: rulati Diagnose-Spider8Usb.cmd (ca Admin) pe PC-ul tinta.

LOGURI
  %ProgramData%\UPETAcqLab\setup.log
  %ProgramData%\UPETAcqLab\driver-install.log
  %ProgramData%\UPETAcqLab\diagnose-usb.log

Detalii: docs\INSTALL-RO.md (in sursa proiectului).
================================================================================
"@

$iscc = $null
$isccCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
    "C:\InnoSetup6\ISCC.exe",
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
)
foreach ($c in $isccCandidates) {
    if ($c -and (Test-Path -LiteralPath $c)) { $iscc = $c; break }
}

# Always stage ZIP + CMD kit (works without Inno)
Write-Host "== Staging ZIP + elevated setup =="
$stage = Join-Path $dist "payload"
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item -Path (Join-Path $publish '*') -Destination $stage -Recurse -Force
$stageInstaller = Join-Path $stage "installer"
New-Item -ItemType Directory -Force -Path $stageInstaller | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot "drivers") -Destination (Join-Path $stageInstaller "drivers") -Recurse -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Install-UsbSerialDrivers.ps1") -Destination $stageInstaller -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Setup-UPETAcqLab.ps1") -Destination $stageInstaller -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Uninstall-UPETAcqLab.ps1") -Destination $stageInstaller -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Uninstall-UPETAcqLab.cmd") -Destination $stageInstaller -Force
# Also at app root so Setup copies them into Program Files with the payload
Copy-Item -Path (Join-Path $PSScriptRoot "Uninstall-UPETAcqLab.ps1") -Destination $stage -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Uninstall-UPETAcqLab.cmd") -Destination $stage -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Setup-UPETAcqLab.ps1") -Destination $stage -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Install-UsbSerialDrivers.ps1") -Destination $stage -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Diagnose-Spider8Usb.ps1") -Destination $stage -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Diagnose-Spider8Usb.cmd") -Destination $stage -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Diagnose-Spider8Usb.ps1") -Destination $stageInstaller -Force
Copy-Item -Path (Join-Path $PSScriptRoot "Diagnose-Spider8Usb.cmd") -Destination $stageInstaller -Force
Copy-Item -Path (Join-Path $PSScriptRoot "CITESTE-MA-USB-SPIDER8.txt") -Destination $stage -Force
Copy-Item -Path (Join-Path $PSScriptRoot "CITESTE-MA-USB-SPIDER8.txt") -Destination $stageInstaller -Force
Set-Content -Path (Join-Path $dist "CITESTE-MA-INSTALARE.txt") -Value $readme -Encoding UTF8
Set-Content -Path (Join-Path $stage "CITESTE-MA-INSTALARE.txt") -Value $readme -Encoding UTF8

$zip = Join-Path $dist "UPETAcqLab-Portable.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force

Copy-Item -Path (Join-Path $PSScriptRoot "Install-UPETAcqLab.wrapper.ps1") -Destination (Join-Path $dist "Install-UPETAcqLab.ps1") -Force

$cmdPath = Join-Path $dist "UPETAcqLab-Setup.cmd"
$cmdContent = '@echo off' + [Environment]::NewLine +
    'title UPET AcqLab Setup' + [Environment]::NewLine +
    'cd /d "%~dp0"' + [Environment]::NewLine +
    'echo.' + [Environment]::NewLine +
    'echo  UPET AcqLab — instalare (necesita Administrator)' + [Environment]::NewLine +
    'echo.' + [Environment]::NewLine +
    'powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell -Verb RunAs -ArgumentList ''-NoProfile -ExecutionPolicy Bypass -File \"%~dp0Install-UPETAcqLab.ps1\"''"' + [Environment]::NewLine +
    'echo.' + [Environment]::NewLine +
    'echo  Daca a aparut UAC, confirmati. Setup-ul continua in fereastra elevata.' + [Environment]::NewLine +
    'pause' + [Environment]::NewLine
[System.IO.File]::WriteAllText($cmdPath, $cmdContent)

$setupExeName = "UPET-AcqLab-Setup-$appVersion.exe"
if ($iscc) {
    Write-Host "== Inno Setup =="
    Write-Host $iscc
    # Keep ISS version + output name in sync
    $iss = Join-Path $PSScriptRoot "UPETAcqLab.iss"
    $issText = Get-Content $iss -Raw
    $issText = $issText -replace '#define MyAppVersion "[^"]+"', "#define MyAppVersion `"$appVersion`""
    $issText = $issText -replace 'OutputBaseFilename=[^\r\n]+', "OutputBaseFilename=UPET-AcqLab-Setup-$appVersion"
    $utf8Bom = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText($iss, $issText, $utf8Bom)
    & $iscc $iss
    if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit $LASTEXITCODE" }
}

Write-Host "== Sample personal license =="
$gen = Join-Path $root "tools\LicenseGen\bin\Release\net8.0\UpetLicenseGen.exe"
if (-not (Test-Path $gen)) {
    & $dotnet build (Join-Path $root "tools\LicenseGen\LicenseGen.csproj") -c Release
}
$licOut = Join-Path $dist "LICENSE-PERSONAL.txt"
& $gen "Utilizator UPET AcqLab" | Tee-Object -FilePath $licOut
Add-Content -Path $licOut -Value ""
Add-Content -Path $licOut -Value "Titular exemplu: Utilizator UPET AcqLab"
Add-Content -Path $licOut -Value "Produs: UPET AcqLab"
Add-Content -Path $licOut -Value "Activare: la primul start introduceti Nume + Cheie"
Add-Content -Path $licOut -Value "Fisier local: %LocalAppData%\UPETAcqLab\license.dat"

if (-not $SkipDesktopCopy) {
    $desk = [Environment]::GetFolderPath("Desktop")
    # Prefer single Inno setup EXE on Desktop (full installer)
    $setupExeVersioned = Join-Path $dist $setupExeName
    $setupExeLegacy = Join-Path $dist "UPETAcqLab-Setup.exe"
    $desktopSetup = Join-Path $desk $setupExeName
    if (Test-Path -LiteralPath $setupExeVersioned) {
        Write-Host "== Copiere installer pe Desktop: $desktopSetup =="
        Copy-Item $setupExeVersioned $desktopSetup -Force
        Write-Host "Desktop installer OK: $desktopSetup"
    }
    elseif (Test-Path -LiteralPath $setupExeLegacy) {
        Write-Host "== Copiere installer pe Desktop: $desktopSetup =="
        Copy-Item $setupExeLegacy $desktopSetup -Force
        Write-Host "Desktop installer OK: $desktopSetup"
    }
    else {
        # Fallback: folder kit (no ISCC)
        $kit = Join-Path $desk ("UPET-AcqLab-Instalare-v" + $appVersion)
        Write-Host "== Copiere kit pe Desktop (fara Inno): $kit =="
        if (Test-Path -LiteralPath $kit) { Remove-Item -LiteralPath $kit -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $kit | Out-Null
        Copy-Item (Join-Path $dist "CITESTE-MA-INSTALARE.txt") $kit -Force
        Copy-Item (Join-Path $PSScriptRoot "CITESTE-MA-USB-SPIDER8.txt") $kit -Force
        Copy-Item (Join-Path $PSScriptRoot "Diagnose-Spider8Usb.cmd") $kit -Force
        Copy-Item (Join-Path $PSScriptRoot "Diagnose-Spider8Usb.ps1") $kit -Force
        Copy-Item (Join-Path $dist "UPETAcqLab-Setup.cmd") $kit -Force
        Copy-Item (Join-Path $dist "Install-UPETAcqLab.ps1") $kit -Force
        Copy-Item (Join-Path $dist "UPETAcqLab-Portable.zip") $kit -Force
        Copy-Item (Join-Path $dist "LICENSE-PERSONAL.txt") $kit -Force
        Copy-Item $stage (Join-Path $kit "payload") -Recurse -Force
        Write-Host "Kit Desktop OK: $kit"
    }
}

Write-Host "Done. v$appVersion"
Write-Host $dist
Get-ChildItem $dist | Format-Table Name, @{N='MB';E={[math]::Round($_.Length/1MB,1)}}, LastWriteTime
