#Requires -Version 5.1
<#
.SYNOPSIS
  Headless smoke: Simulator Connect -> Start -> Record -> CSV (no UI, no Spider32 USB loops).
#>
param(
    [string]$BinDir = "",
    [int]$RecordMs = 1500
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if ([string]::IsNullOrWhiteSpace($BinDir)) {
    $BinDir = Join-Path $root "publish-v2"
}
$core = Join-Path $BinDir "Spider8DAQ.Core.dll"
$hw = Join-Path $BinDir "Spider8DAQ.Hardware.dll"
if (-not (Test-Path $core) -or -not (Test-Path $hw)) {
    throw "Missing Core/Hardware DLL in $BinDir - publish first."
}

Add-Type -Path $core
Add-Type -Path $hw

$outDir = Join-Path $env:LOCALAPPDATA "UPETAcqLab\recordings"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$csv = Join-Path $outDir ("smoke_sim_{0:yyyyMMdd_HHmmss}.csv" -f (Get-Date))

$dev = [Spider8DAQ.Hardware.SimulatedSpider8]::new(8, 1)
$dev.SampleRateHz = 50
$engine = [Spider8DAQ.Core.Acquisition.AcquisitionEngine]::new()

Write-Host "CONNECT..."
$dev.ConnectAsync().GetAwaiter().GetResult() | Out-Null
if ($dev.State.ToString() -ne "Connected") { throw "Connect failed: $($dev.State)" }

$engine.Attach($dev)
$headers = 0..7 | ForEach-Object { "CH$($_+1)" }
Write-Host "START streaming..."
$dev.StartStreamingAsync().GetAwaiter().GetResult() | Out-Null
if ($dev.State.ToString() -ne "Streaming") { throw "Start failed: $($dev.State)" }

Write-Host "RECORD ${RecordMs}ms -> $csv"
$engine.ArmRecordingAsync($csv, [string[]]$headers).GetAwaiter().GetResult() | Out-Null
Start-Sleep -Milliseconds $RecordMs
$engine.StopRecordingAsync().GetAwaiter().GetResult() | Out-Null
$dev.StopStreamingAsync().GetAwaiter().GetResult() | Out-Null
$dev.DisconnectAsync().GetAwaiter().GetResult() | Out-Null
$engine.DisposeAsync().AsTask().GetAwaiter().GetResult() | Out-Null

if (-not (Test-Path $csv)) { throw "CSV missing: $csv" }
$lines = @(Get-Content $csv)
$n = $lines.Count
if ($n -lt 3) { throw "CSV too short ($n lines): $csv" }
$len = (Get-Item $csv).Length
$samples = $n - 1
Write-Host "OK samples_lines=$samples bytes=$len path=$csv"
exit 0
