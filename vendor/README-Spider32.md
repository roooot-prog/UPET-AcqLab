# Spider32.dll - sursa oficiala HBM (Spider8 Setup)

## Verdict final USB (2026-07-31)

**`S8_InitAll(PORT_USB=100)` = `-1` pe port liber — STOP reincercari in bucla.**

| Componenta | Rezultat |
|------------|----------|
| Spider32.dll (vendor, v2.00) | **Nu deschide USBHBM modern** |
| DirectNT / NT_IODRV | Instalat, **blocat la load (1275)** pe Win11; util doar LPT |
| catman `HBM_Scan.dll` | Scan retea / adapter IP — **fara API USB Spider8** pentru UPET |
| catman `intfac32.dll` | Strat COM/GPIB/TCP — **nu inlocuieste** Spider32 USB |
| Calea functionala UPET | **Simulator** (demo) sau **Serial RS232 pe COM real** |

### Cum masurati acum

1. **Simulator (obligatoriu / lab demo):** backend `Simulator` → **Conectare** → **Start** → **Record** → CSV in `%LocalAppData%\UPETAcqLab\recordings\`
2. **USB HBM:** folositi **catman Easy** pentru achizitie USB; UPET nu are interop curat pe stack-ul catman.
3. **Serial:** adaptor RS232 pe port COM → backend `Serial` (nu USBHBM).

---

## Verdict (2026-07-30, dupa retest)

**`S8_InitAll(PORT_USB=100)` = `-1` (`ERR_PORT_OPEN_FAILED`) chiar cu portul USB liber.**

- Proces **UPETAcqLab** a fost oprit (taskkill) pentru test; **reporniti UPET AcqLab** dupa experiment.
- **catmanEASY** nu rula in sesiunea de test.
- Dispozitiv Windows: `Spider8 / MGCplus CP32 / K800 / K148` (USBHBM2186) status **OK**.
- Driverul **HBM USB IO** este instalat — **nu** il dezinstalati.
- DLL-ul din `sp32.zip` / SpiderCD + companioni Setup + Intfac32 2016 **nu** rezolva `-1`.

### Pasul urmator (definitiv)

1. **Hardware / legatura:** alimentati Spider8, verificati adaptorul USB HBM (cablu, LED-uri), deconectati/reconectati USB, apoi reincercati Connect **fara** UPET/catman deschise.
2. **Validare cu soft HBM:** deschideti scurt **catman Easy**, vedeti daca vede / deschide USBHBM2186. Daca nici catman nu deschide portul → problema e pe lantul hardware/driver/dispozitiv, nu pe DLL-ul din acest proiect.
3. **Setup Spider32 (optional, manual):** exista InstallShield `disk1\SETUP.EXE` („Spider32 Setup”) in arhiva. **Nu** rulati silent automat (vezi mai jos). Instalarea interactiva poate copia DLL-uri/help, dar pe Win10/11 risca fisiere System vechi; **nu** e necesara doar pentru a „activa USB” daca driverul USBHBM e deja OK.
4. Daca catman deschide USB iar aplicatia nu: comparati calea DLL / bitness (x86) si reincercati smoke-ul din `vendor\_hbm_download\smoke_init.ps1`.

---

## Ce s-a facut (automat)

1. Descarcat **sp32.zip** de pe pagina HBM legacy (Spider8 Setup).
2. Extras `SpiderCD_V228` + CAB InstallShield (`_extract_sp32\_ue_out`, `_hbm_download\sp32_extracted`).
3. `vendor\Spider32.dll` = varianta **Program_Files** (EN) din Setup:
   - 110592 octeti, timestamp PE **2003-05-09**
   - SHA256: `FEB23DE1919D896FCCC89511B5A6F6E8AAD13EC206F246A44F8F286C5AB3280C`
4. Backup s8exmpl 1999: `vendor\Spider32.dll.s8exmpl-bak` (115200 B).
5. Backup EN curent (dupa experiment DE): `vendor\Spider32.dll.en-sp32-bak` (acelasi hash ca vendor).

## Comparatie hash Spider32.dll

| Sursa | Size | SHA256 (prefix) | InitAll USB |
|-------|------|-----------------|-------------|
| `vendor\Spider32.dll` (= Program_Files EN) | 110592 | FEB23DE1919D896F… | **-1** |
| Programm_Dateien (DE) | 110592 | B3DE6FB99FEB97BB… | **-1** (reincercat) |
| s8exmpl 1999 | 115200 | FC5297AA59F01CFA… | (vechi; backup) |

**Nu exista un Spider32 „mai nou” pe SpiderCD_V228** fata de EN deja in `vendor\`. Varianta DE e diferita binary, FileVersion `1,0,0,2`, tot `-1`. Pastrati EN.

## Setup.exe — ce inseamna

| Cale | Rol |
|------|-----|
| `SpiderCD_V228\Setup.exe` | **DemoShield** launcher (`Setup.ini`: `CopyFiles=0`, porneste demo) — **nu** e installer-ul API |
| `SpiderCD_V228\disk1\SETUP.EXE` | **InstallShield „Spider32 Setup”** (`SETUP.INI` AppName=Spider32 Setup) |

Continut Setup relevant (extras UniExtract):

- `Program_Files` / `Programm_Dateien`: Spider32.dll + help
- `Sprachunabhaengig`: companioni (`Papo32`, `interlnk`, `acqbase`, …) + `SPIDER32.EXE`
- `Windows_System_*`: OCX/OLE/MFC **foarte vechi** (risc pe OS modern)
- `Usb_hbm\`: usbhbm.inf/sys (driver USB — deja instalat pe acest PC)

### Silent install?

- InstallShield clasic: `SETUP.EXE -s` necesita **`setup.iss`** (inregistrat cu `-r`). **Nu exista** `setup.iss` in arhiva.
- Silent **nu** a fost rulat: nu e clar non-destructiv (ar putea scrie in System32 fisiere 1990s).
- **Nu** rulati Setup-ul full catman; doar Spider32 Setup, si doar manual daca acceptati riscul.

Copierea companionilor Setup langa `vendor\` (fara installer) **nu** schimba rezultatul: tot `rc=-1`.

## Smoke USB (procedura)

```text
# 1) Inchideti UPET AcqLab + catman Easy
# 2) x86 PowerShell:
%WINDIR%\SysWOW64\WindowsPowerShell\v1.0\powershell.exe -ExecutionPolicy Bypass -File vendor\_hbm_download\smoke_init.ps1
```

Rezultat asteptat pe acest PC (port liber, USBHBM OK):  
`S8_InitAll(PORT_USB=100) rc=-1`

## Fisiere locale utile

| Cale | Rol |
|------|-----|
| `vendor\Spider32.dll` | DLL curent (EN sp32 / Program_Files) |
| `vendor\Spider32.dll.s8exmpl-bak` | Backup s8exmpl 1999 |
| `vendor\Spider32.dll.en-sp32-bak` | Copie EN (acelasi hash) |
| `vendor\Intfac32.dll` | 2016 / 5.2.0.34 (identic cu catmanEasy) |
| `vendor\_hbm_download\sp32.zip` | Arhiva oficiala |
| `vendor\_hbm_download\sp32_extracted\SpiderCD_V228\` | CD extras + Setup |
| `vendor\_hbm_download\smoke_init.ps1` | Smoke InitAll x86 |

## Nu faceti

- Nu copiați / nu redistribuiti **catman** Easy/AP.
- Nu dezinstalati **HBM USB IO Driver**.
- Nu rulati silent Setup fara `setup.iss` + fara backup System.
- Nu comiteti DLL-urile HBM in git daca politica proiectului interzice binare vendor.

## Smoke 2026-07-31 (post-reboot check)

- Procese catman/UPET: eliminate pentru test curat; USBHBM2186 PnP **OK**; driver `usbhbm.sys` **Running**.
- Serviciu **DirectNT**: **Stopped**, Win32 1275 (driver blocked from loading) - relevant LPT, nu stack-ul USBHBM.
- `S8_InitAll(PORT_USB=100)` cu `vendor\Spider32.dll` (v2.00, 110592) si variante s8exmpl/bak: **rc=-1** pe port liber.
- Concluzie neschimbata: mesajul „port ocupat” e insuficient; pe acest PC Spider32 nu deschide USBHBM modern chiar cand catman poate (stack diferit).
