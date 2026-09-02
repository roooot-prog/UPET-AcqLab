<#
.SYNOPSIS
  Descarcă pachete USB-serial redistribuibile în installer\drivers\
#>
param(
    [string]$OutRoot = ""
)

$ErrorActionPreference = "Continue"
if ([string]::IsNullOrWhiteSpace($OutRoot)) {
    $OutRoot = Join-Path $PSScriptRoot "drivers"
}

New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null
$tmp = Join-Path $env:TEMP ("upet-drv-" + [guid]::NewGuid().ToString("n"))
New-Item -ItemType Directory -Force -Path $tmp | Out-Null

$ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36"

function Expand-ZipSafe([string]$zip, [string]$dest) {
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Expand-Archive -Path $zip -DestinationPath $dest -Force
}

function Get-Url([string]$url, [string]$outFile) {
    Write-Host "GET $url"
    try {
        Invoke-WebRequest -Uri $url -OutFile $outFile -UseBasicParsing -MaximumRedirection 8 -Headers @{ "User-Agent" = $ua }
        return (Test-Path $outFile) -and ((Get-Item $outFile).Length -gt 1000)
    } catch {
        Write-Host "  FAIL: $($_.Exception.Message)"
        return $false
    }
}

# --- FTDI VCP ---
$ftdiDir = Join-Path $OutRoot "ftdi"
New-Item -ItemType Directory -Force -Path $ftdiDir | Out-Null
$ftdiZip = Join-Path $tmp "ftdi_cdm.zip"
$ftdiUrls = @(
    "https://ftdichip.com/wp-content/uploads/2025/02/CDM-v2.12.36.4-WHQL-Certified.zip",
    "https://ftdichip.com/wp-content/uploads/2024/10/CDM-v2.12.36.4-WHQL-Certified.zip",
    "https://github.com/juliagoda/FTDI_Win_Driver/archive/refs/heads/master.zip"
)
$gotFtdi = $false
foreach ($u in $ftdiUrls) {
    if (Get-Url $u $ftdiZip) {
        try { Expand-ZipSafe $ftdiZip $ftdiDir; $gotFtdi = $true; break } catch { Write-Host "  expand fail" }
    }
}
if (-not $gotFtdi) {
    Set-Content -Path (Join-Path $ftdiDir "DOWNLOAD.txt") -Encoding UTF8 -Value @"
Descarcati manual FTDI VCP WHQL: https://ftdichip.com/drivers/vcp-drivers/
Extrati INF/CAT/SYS in acest folder (ftdi\). Instalatorul UPET le va instala cu pnputil.
"@
}

# --- Silicon Labs CP210x ---
$cpDir = Join-Path $OutRoot "cp210x"
New-Item -ItemType Directory -Force -Path $cpDir | Out-Null
if (-not (Get-ChildItem $cpDir -Filter *.inf -Recurse -ErrorAction SilentlyContinue)) {
    $cpZip = Join-Path $tmp "cp210x.zip"
    $cpUrls = @(
        "https://www.silabs.com/documents/public/software/CP210x_Windows_Drivers.zip",
        "https://www.silabs.com/documents/public/software/CP210x_Universal_Windows_Driver.zip"
    )
    foreach ($u in $cpUrls) {
        if (Get-Url $u $cpZip) { Expand-ZipSafe $cpZip $cpDir; break }
    }
}

# --- WCH CH340 ---
$chDir = Join-Path $OutRoot "ch340"
New-Item -ItemType Directory -Force -Path $chDir | Out-Null
$chZip = Join-Path $tmp "ch340.zip"
$chUrls = @(
    "https://github.com/WCHSoftGroup/ch34xser_windows/archive/refs/heads/main.zip",
    "https://github.com/WCHSoftGroup/ch343ser_windows/archive/refs/heads/main.zip",
    "https://cdn.jsdelivr.net/gh/nicolaielectronics/ch340-driver@master/CH341SER.ZIP"
)
$gotCh = $false
foreach ($u in $chUrls) {
    if (Get-Url $u $chZip) {
        try { Expand-ZipSafe $chZip $chDir; $gotCh = $true; break } catch {}
    }
}
if (-not $gotCh) {
    Set-Content -Path (Join-Path $chDir "DOWNLOAD.txt") -Encoding UTF8 -Value @"
Descarcati CH340SER de pe https://www.wch-ic.com/downloads/CH341SER_EXE.html
Extrati INF in ch340\.
"@
}

# --- Prolific PL2303 ---
$plDir = Join-Path $OutRoot "pl2303"
New-Item -ItemType Directory -Force -Path $plDir | Out-Null
$plZip = Join-Path $tmp "pl2303.zip"
# Community mirror of PL2303 legacy INF (compatible check) — optional
$plUrls = @(
    "https://github.com/zhujisheng/PL2303/archive/refs/heads/master.zip"
)
$gotPl = $false
foreach ($u in $plUrls) {
    if (Get-Url $u $plZip) {
        try { Expand-ZipSafe $plZip $plDir; $gotPl = $true; break } catch {}
    }
}
if (-not $gotPl) {
    Set-Content -Path (Join-Path $plDir "DOWNLOAD.txt") -Encoding UTF8 -Value @"
Prolific PL2303: descarcati driverul oficial de pe https://www.prolific.com.tw/
Extrati INF/CAT/SYS aici. Instalatorul UPET ruleaza pnputil pe orice INF gasit.
"@
}

# --- VC++ x86 ---
$vcDir = Join-Path $OutRoot "vcredist"
New-Item -ItemType Directory -Force -Path $vcDir | Out-Null
$vcExe = Join-Path $vcDir "VC_redist.x86.exe"
if (-not (Test-Path $vcExe) -or (Get-Item $vcExe).Length -lt 100000) {
    Get-Url "https://aka.ms/vs/17/release/vc_redist.x86.exe" $vcExe | Out-Null
}

Write-Host ""
Write-Host "Rezumat INF dupa fetch:"
Get-ChildItem $OutRoot -Filter *.inf -Recurse -ErrorAction SilentlyContinue | ForEach-Object { Write-Host "  $($_.FullName)" }
Write-Host "VC++: $(Test-Path $vcExe) size=$($(if (Test-Path $vcExe){(Get-Item $vcExe).Length} else {0}))"
Write-Host "Done. OutRoot=$OutRoot"
