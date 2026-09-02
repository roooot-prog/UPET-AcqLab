# NT_IODRV / DirectNT (driver I/O port legacy HBM)

## Stare instalare (2026-07-31)

| Componentă | Stare |
|---|---|
| **NT_IODRV.EXE** | Rulat elevat (ExitCode=33 ≈ „repornește”) |
| **DirectNT** (serviciu) | **STOPPED** — Windows blochează încărcarea: eroare **1275** (*This driver has been blocked from loading*) |
| **USBHBM** | **RUNNING** (driver USB HBM — necesar pentru Spider USB) |
| Secure Boot | False |

**Concluzie pentru Spider8DAQ / Connect USB:** aplicația pe `PORT_USB` folosește **USBHBM**, nu DirectNT. DirectNT e driverul legacy pentru **port paralel / ISA** (catman vechi). Connect USB poate funcționa cu USBHBM Running chiar dacă DirectNT rămâne STOPPED.

## Fișiere locale (sursă SpiderCD / exemple HBM)

- `vendor\nt_iodrv\NT_IODRV.EXE`
- `vendor\nt_iodrv\DIRECTNT.SYS`
- Surse: `vendor\_extract_sp32\_ue_out\Windows_System_Non_Self-Register\`
- SHA256 NT_IODRV.EXE: 6156D9776D13771D98A67C61313B8377E338A6C21B045E5A4C0EDE1AEC238BAE

Driver pe disc:
- `C:\Windows\System32\drivers\DIRECTNT.SYS`
- `C:\Windows\SysWOW64\Drivers\DirectNT.sys`

## De ce DirectNT nu e Running

Windows 10/11 blochează drivere kernel **nesemnate** (cod 1275). Nu am activat test signing. Chiar cu Secure Boot=False, politica de semnare poate bloca DirectNT.

## Relație USBHBM / S8_InitAll(PORT_USB)

- `S8_InitAll(PORT_USB)=-1` e mai probabil lipsă USBHBM, cablu sau DLL — **nu** DirectNT.
- USBHBM este deja **RUNNING** pe acest PC.
