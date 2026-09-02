# Deploy UPET AcqLab from publish-v2 → Program Files (run as Administrator)
$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "..\publish-v2"
$dst = "${env:ProgramFiles(x86)}\UPET AcqLab"
if (-not (Test-Path $src)) { throw "Missing $src - run dotnet publish first." }
Get-Process UPETAcqLab -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400
New-Item -ItemType Directory -Force -Path $dst | Out-Null
robocopy $src $dst /E /IS /IT /R:2 /W:1
$code = $LASTEXITCODE
if ($code -ge 8) { throw "robocopy failed with $code" }
$exe = Join-Path $dst "UPETAcqLab.exe"
$vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
Write-Host "Installed $($vi.FileVersion) -> $exe"
