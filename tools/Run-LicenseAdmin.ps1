# UPET AcqLab — pornește API-ul de licențe și panoul Admin (nu Canale DAQ).
# Parola se pune în %LocalAppData%\UPETAcqLab.LicenseApi\settings.json (nu pe GitHub).

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $root "Spider8DAQ.sln"))) {
    $root = $PSScriptRoot
    if (-not (Test-Path (Join-Path $root "Spider8DAQ.sln"))) {
        $root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
    }
}

Write-Host "API:  http://127.0.0.1:5088"
Write-Host "Setări parolă: $env:LOCALAPPDATA\UPETAcqLab.LicenseApi\settings.json"
Write-Host ""

$apiProj = Join-Path $root "UPETAcqLab.LicenseApi\UPETAcqLab.LicenseApi.csproj"
$adminProj = Join-Path $root "UPETAcqLab.Admin\UPETAcqLab.Admin.csproj"

Start-Process -FilePath "dotnet" -ArgumentList @("run", "--project", $apiProj, "-c", "Release") -WorkingDirectory $root
Start-Sleep -Seconds 2
Start-Process -FilePath "dotnet" -ArgumentList @("run", "--project", $adminProj, "-c", "Release") -WorkingDirectory $root

Write-Host "API și Admin au fost pornite. În Admin: Conectează → Generează cheie."
