# UPET AcqLab Setup wrapper - ruleaza ca Administrator
#Requires -RunAsAdministrator
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$extract = $null
$payloadDir = Join-Path $here "payload"
$payloadZip = Join-Path $here "UPETAcqLab-Portable.zip"
if (Test-Path (Join-Path $payloadDir "UPETAcqLab.exe")) {
    $extract = $payloadDir
}
elseif (Test-Path $payloadZip) {
    $extract = Join-Path $env:TEMP ("UPETAcqLab-setup-" + [guid]::NewGuid().ToString("n"))
    New-Item -ItemType Directory -Force -Path $extract | Out-Null
    Expand-Archive -Path $payloadZip -DestinationPath $extract -Force
}
else {
    throw "Lipseste payload\ sau UPETAcqLab-Portable.zip langa acest script."
}
$setup = Get-ChildItem $extract -Recurse -Filter Setup-UPETAcqLab.ps1 | Select-Object -First 1
if (-not $setup) { throw "Setup-UPETAcqLab.ps1 negasit in pachet." }
$exe = Get-ChildItem $extract -Recurse -Filter UPETAcqLab.exe | Select-Object -First 1
if (-not $exe) { throw "UPETAcqLab.exe negasit in pachet." }
$payloadApp = Split-Path $exe.FullName
# Drivers default ON inside Setup (no -InstallDrivers bool - PS -File binding issue)
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $setup.FullName -PayloadDir $payloadApp
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }