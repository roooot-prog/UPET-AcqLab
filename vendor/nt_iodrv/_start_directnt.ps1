$ErrorActionPreference='Continue'
$log='C:\Users\acer\Desktop\Spider8DAQ\vendor\nt_iodrv\start-log.txt'
function L($m){ Add-Content $log ((Get-Date -Format o) + ' ' + $m) }
'' | Set-Content $log
L 'elevated start attempt'
# Ensure driver also in System32\drivers (native path preferred on 64-bit)
$src = 'C:\Users\acer\Desktop\Spider8DAQ\vendor\nt_iodrv\DIRECTNT.SYS'
$dst1 = Join-Path $env:SystemRoot 'System32\drivers\DIRECTNT.SYS'
$dst2 = Join-Path $env:SystemRoot 'SysWOW64\Drivers\DirectNT.sys'
Copy-Item -Force $src $dst1 -ErrorAction SilentlyContinue
Copy-Item -Force $src $dst2 -ErrorAction SilentlyContinue
L ("System32 drivers: " + (Test-Path $dst1))
L ("SysWOW64 Drivers: " + (Test-Path $dst2))
# Point service to System32\drivers as well
sc.exe config DirectNT binPath= '\SystemRoot\System32\drivers\DIRECTNT.SYS' start= auto 2>&1 | Out-String | ForEach-Object { L $_ }
# Re-run vendor installer
$p = Start-Process -FilePath 'C:\Users\acer\Desktop\Spider8DAQ\vendor\nt_iodrv\NT_IODRV.EXE' -WorkingDirectory 'C:\Users\acer\Desktop\Spider8DAQ\vendor\nt_iodrv' -Wait -PassThru
L ("NT_IODRV ExitCode=" + $p.ExitCode)
# Try start
sc.exe start DirectNT 2>&1 | Out-String | ForEach-Object { L $_ }
sc.exe query DirectNT 2>&1 | Out-String | ForEach-Object { L $_ }
sc.exe query USBHBM 2>&1 | Out-String | ForEach-Object { L $_ }
try { L ("SecureBoot=" + (Confirm-SecureBootUEFI)) } catch { L ("SecureBootErr=" + $_.Exception.Message) }
bcdedit /enum {current} 2>&1 | Out-String | ForEach-Object { L $_ }
L 'done'
