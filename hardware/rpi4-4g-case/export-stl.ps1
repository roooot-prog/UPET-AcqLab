# Export STL from rpi4_4g_hat_case.scad
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here

$openscad = $null
foreach ($c in @(
    "openscad",
    "$env:ProgramFiles\OpenSCAD\openscad.exe",
    "${env:ProgramFiles(x86)}\OpenSCAD\openscad.exe"
)) {
    if (Get-Command $c -ErrorAction SilentlyContinue) { $openscad = (Get-Command $c).Source; break }
    if (Test-Path $c) { $openscad = $c; break }
}

if (-not $openscad) {
    Write-Host "OpenSCAD nu este instalat. Descarca de la https://openscad.org/ si re-ruleaza."
    exit 1
}

New-Item -ItemType Directory -Force -Path "$here\stl" | Out-Null
$src = Join-Path $here "rpi4_4g_hat_case.scad"

foreach ($item in @("base", "lid", "print_set")) {
    $out = Join-Path $here "stl\rpi4_4g_case_$item.stl"
    Write-Host "Export $item -> $out"
    & $openscad -D "part=`"$item`"" -o $out $src
}

Write-Host "Gata."
