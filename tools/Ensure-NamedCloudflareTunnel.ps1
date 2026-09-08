# Tunel Cloudflare cu nume fix (același HTTPS luni de zile).
# Necesită: cont Cloudflare + un domeniu cu nameserverele la Cloudflare.
# Exemplu:  powershell -File tools\Ensure-NamedCloudflareTunnel.ps1 -Hostname licente.exemplu.ro
param(
    [Parameter(Mandatory = $true)]
    [string]$Hostname
)

$ErrorActionPreference = "Stop"
$cf = "${env:ProgramFiles(x86)}\cloudflared\cloudflared.exe"
if (-not (Test-Path $cf)) { $cf = "$env:ProgramFiles\cloudflared\cloudflared.exe" }
if (-not (Test-Path $cf)) { throw "Lipsește cloudflared.exe. Instalați Cloudflare Tunnel." }

$hostClean = $Hostname.Trim().ToLowerInvariant() -replace '^https://', '' -replace '/$', ''
if ($hostClean -notmatch '\.') { throw "Hostname invalid. Folosiți un domeniu (ex. licente.firma.ro)." }

$lanDir = Join-Path $env:LOCALAPPDATA "UPETAcqLab.LicenseApi"
New-Item -ItemType Directory -Force -Path $lanDir | Out-Null
$cert = Join-Path $env:USERPROFILE ".cloudflared\cert.pem"
if (-not (Test-Path $cert)) {
    Write-Host "Se deschide autentificarea Cloudflare. Alegeți domeniul în browser, apoi reluați scriptul."
    & $cf tunnel login
    if (-not (Test-Path $cert)) { throw "Autentificare Cloudflare neterminată (lipsește cert.pem)." }
}

$tunnelName = "upet-acqlab-license"
$list = & $cf tunnel list --output json 2>$null
$tid = $null
if ($list) {
    try {
        $arr = $list | ConvertFrom-Json
        $hit = @($arr) | Where-Object { $_.name -eq $tunnelName } | Select-Object -First 1
        if ($hit) { $tid = [string]$hit.id }
    } catch {}
}
if (-not $tid) {
    Write-Host "Creez tunelul $tunnelName …"
    $created = & $cf tunnel create $tunnelName
    $tid = ([regex]::Match(($created | Out-String), '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}')).Value
    if (-not $tid) { throw "Nu am putut citi ID-ul tunelului. Ieșire: $created" }
}

$cred = Join-Path $env:USERPROFILE ".cloudflared\$tid.json"
if (-not (Test-Path $cred)) {
    $alt = Get-ChildItem (Join-Path $env:USERPROFILE ".cloudflared") -Filter "$tid.json" -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($alt) { $cred = $alt.FullName }
}
if (-not (Test-Path $cred)) { throw "Lipsește fișierul de credențiale $cred" }

Write-Host "DNS $hostClean → tunel $tid"
& $cf tunnel route dns --overwrite-dns $tunnelName $hostClean

$ymlPath = Join-Path $lanDir "cloudflared-config.yml"
$credYaml = $cred.Replace('\', '/')
$yml = @"
tunnel: $tid
credentials-file: $credYaml
protocol: quic
ingress:
  - hostname: $hostClean
    service: http://127.0.0.1:5088
  - service: http_status:404
"@
[IO.File]::WriteAllText($ymlPath, $yml)

$meta = @{
    Name = $tunnelName
    Hostname = $hostClean
    Id = $tid
} | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $lanDir "named-tunnel.json"), $meta)
$pub = "https://$hostClean"
[IO.File]::WriteAllText((Join-Path $lanDir "public-url.txt"), $pub + [Environment]::NewLine)

Write-Host "Gata. Hostname stabil: $pub"
Write-Host "Reporniți scurtătura UPET AcqLab Admin. Apoi publicați un release cu tools/license-server.release.json."
