# Spider32.dll (HBM proprietary)

Place these files from the official **HBM Spider8** packages into this folder:

| File | Role |
|---|---|
| `Spider32.dll` | API driver (required) — **not** shipped inside catman Easy media/install |
| `Intfac32.dll` | Dependency (can copy from catmanEasy install if newer) |
| `Papo32.dll` | Dependency |
| `Interlnk.dll` | Dependency |

## De ce lipsește din `HBM-catman-502` / catman Easy?

Kit-ul de pe Desktop este **installer** catman (`Setup\setup.exe`), nu un SDK Spider8. După instalare, `Program Files (x86)\HBM\catmanEasy` **tot nu** conține `Spider32.dll` — catman vorbește `USBHBM` pe stack propriu. UPET are nevoie de API-ul clasic Spider8.

## Unde le luați (legal, local lab)

1. Pagina HBM legacy:  
   https://www.hbm.com/2464/software-and-firmware-downloads-for-legacy-products/
2. **Spider8 Examples** → `s8exmpl.zip` (conține `example\Spider32.dll` + companion-e)  
3. **Spider8 Setup (32bit)** → `sp32.zip` (Setup V2.28; DLL mai nouă în installer, cu suport USB IO / usbhbm)

UPET **nu** redistribuie binarele HBM în git (vezi `.gitignore`).

## Serial vs USB

- **Serial (COM):** adaptor USB–serial VCP → backend **Serial**, port `COMx`.
- **USB nativ (USBHBM…):** driver `usbhbm.sys`, **fără** COM → backend **Spider32.dll**, țintă `USBHBM…`.  
  DLL-ul din `s8exmpl` (1999) e orientat COM/LPT; pentru USBHBM preferați DLL din **Spider8 Setup**.

Firmware Spider8 ≥ P20 recomandat (note LabVIEW HBM).
