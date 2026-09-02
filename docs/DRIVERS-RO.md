# Drivere Spider8 / HBM / UPET — raport instalare

**Data:** 2026-07-30 18:10  
**Reboot automat:** NU  
**Reboot necesar (manual de către utilizator):** **DA**

## Listă instalată / verificată

1. **NT_IODRV / DirectNT** (Spider8 parallel I/O)
   - Sursă: `vendor\_extract_sp32\_ue_out\Windows_System_Non_Self-Register\NT_IODRV.EXE`
   - Copiat în: `vendor\nt_iodrv\`
   - Rulat elevat (RunAs); ExitCode installer: **33**
   - Driver: `C:\Windows\SysWOW64\Drivers\DirectNT.sys` (prezent: True)
   - Serviciu: **DirectNT** creat, START_TYPE=AUTO, stare curentă **STOPPED** (pornește după restart)

2. **HBM USB IO Driver**
   - Setup: `vendor\_extract\app\DriverSetups\HBM USB IO Driver Setup.exe`
   - Acțiune: **NU s-a re-rulat** — deja instalat
   - Path: `C:\Program Files\HBM\HBM USB IO Driver\` (prezent: True)
   - Kernel: `usbhbm.sys` (prezent: True), serviciu **USBHBM** = **RUNNING**

3. **Companioni Spider8 Setup (CD)**
   - Driver companion necesar = **NT_IODRV** (mai sus)
   - `SpiderCD_V228\Setup.exe` (instalator produs complet) **nu** a fost rulat (nu e setup doar de driver; evitat GUI lung)

## Verificare scurtă

### DirectNT
```
[SC] QueryServiceConfig SUCCESS

SERVICE_NAME: DirectNT
        TYPE               : 1  KERNEL_DRIVER 
        START_TYPE         : 2   AUTO_START
        ERROR_CONTROL      : 1   NORMAL
        BINARY_PATH_NAME   : \SystemRoot\SysWOW64\Drivers\DirectNT.sys
        LOAD_ORDER_GROUP   : Extended Base
        TAG                : 0
        DISPLAY_NAME       : DirectNT
        DEPENDENCIES       : 
        SERVICE_START_NAME :
```

### USBHBM
```
SERVICE_NAME: USBHBM 
        TYPE               : 1  KERNEL_DRIVER  
        STATE              : 4  RUNNING 
                                (STOPPABLE, NOT_PAUSABLE, IGNORES_SHUTDOWN)
        WIN32_EXIT_CODE    : 0  (0x0)
        SERVICE_EXIT_CODE  : 0  (0x0)
        CHECKPOINT         : 0x0
        WAIT_HINT          : 0x0
```

## Pași pentru utilizator

1. **Reporniți Windows** (obligatoriu după NT_IODRV / DirectNT).
2. După login, deschideți **UPET** / AcqLab.
3. Conectați Spider8 / hardware-ul HBM și verificați comunicarea.

## Note

- DirectNT este driver vechi (era 32-bit / parallel port). Pe Windows 64-bit modern poate necesita compatibilitate suplimentară; totuși fișierele și serviciul sunt înregistrate conform procedurii HBM.
- Detalii locale: `vendor\nt_iodrv\README.md` și `vendor\nt_iodrv\install-log.txt`.
