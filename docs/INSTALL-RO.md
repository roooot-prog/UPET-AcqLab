# Instalare — UPET AcqLab

Universitatea din Petroșani  
Aplicație de laborator pentru HBM Spider8 (USB-serial). Nu este catman și nu folosește licențe HBM.

## 1. Instalare

### Varianta A — setup (recomandat)

1. Deschideți `dist\`
2. Rulați `UPETAcqLab-Setup.cmd` ca Administrator
3. Așteptați copierea aplicației și instalarea driverelor USB/COM din pachet

### Varianta B — PowerShell elevat

```powershell
cd C:\Users\acer\Desktop\Spider8DAQ\dist
powershell -ExecutionPolicy Bypass -File .\Install-UPETAcqLab.ps1
```

### Inno Setup

Dacă există `UPETAcqLab-Setup.exe`, rulați-l și lăsați bifat „Instalează toate driverele USB/COM”.

**Locație:** `C:\Program Files (x86)\UPET AcqLab\`  
**Desktop / Start:** scurtătura **UPET AcqLab** → `UPETAcqLab.exe`

Pachetul e **self-contained win-x86** — nu trebuie .NET Desktop Runtime separat.

## 2. Drivere USB/COM

Instalatorul rulează `pnputil` pe fișierele `.inf` din `installer\drivers\`:

| Pachet | Rol | În pachet |
|---|---|---|
| FTDI VCP | USB–serial FTDI | `ftdi\` (INF oficial) |
| CH340/CH341 | WCH USB–TTL | `ch340\` |
| CP210x | Silicon Labs | inclus |
| PL2303 | Prolific | `pl2303\` |
| VC++ x86 | redistributable | inclus |

Log-uri: `%ProgramData%\UPETAcqLab\` (`setup.log`, `driver-install.log`).

Completare pachete: `installer\Fetch-Drivers.ps1`.

### HBM USB IO (usbhbm) — nu e în pachet

Spider8 pe USB nativ apare ca `USB\VID_10D1&…\USBHBM…` (clasă `HBM_USB_DEVICES`), **fără** port COM.  
Driverul `usbhbm.sys` vine din instalarea oficială HBM (ex. Spider8 Setup / catman) — **UPET nu îl redistribuie**.

| Cale | Backend în UPET | Țintă |
|---|---|---|
| Adaptor USB–serial (FTDI/CH340/…) | **Serial** | `COMx` |
| USB nativ HBM (`USBHBM…`) | **HBM USB** / Spider32.dll (best-effort) | serialul `USBHBM…` din listă Porturi |
| Demo fără hardware | **Simulator** | (ignoră portul) |

**Important overnight (2026-08):**  
- USBHBM e **exclusiv**: închideți **catman Easy** înainte de Connect în UPET.  
- Backend **HBM USB** încearcă Intfac32 (3× + FlushIn) apoi DEST SoftSetup (ACT/ASA/EXC/MSV).  
- Watchdog: nu reconectează dacă PnP USBHBM lipsește sau catman rulează.  
- Spider32 pe USB modern rămâne fragile; demo lab = **Simulator** sau **Serial pe COM**.  
- Nu e clonă catman — vezi `docs/GAP-catmanEasy.md`.

**Rulează fără Admin (overnight):**  
`C:\Users\acer\Desktop\Spider8DAQ\publish-v2\UPETAcqLab.exe`  
sau oglindă: `%LocalAppData%\UPETAcqLab\app\UPETAcqLab.exe`  
Program Files necesită `tools\Deploy-To-ProgramFiles.ps1` ca Administrator.

În aplicație: **Porturi** listează și `USBHBM…`. Pentru demo: **Simulator → Connect → Start**.  
Detalii: `installer\drivers\hbm-usb-io\README.md` și `docs\catman-feature-map-upet.md`.

### Spider32.dll (opțional / legacy COM)

**Nu** căutați `Spider32.dll` în folderul Desktop `HBM-catman-502` sau în instalarea catman Easy — acolo **nu** există ca fișier liber. Kit-ul catman e un installer (`Setup\setup.exe`); după install, catman folosește stack propriu USB (nu Spider32).

1. Descărcați de pe pagina HBM legacy:  
   https://www.hbm.com/2464/software-and-firmware-downloads-for-legacy-products/
2. **Spider8 Examples** (`s8exmpl.zip`) — DLL + companion-e  
   sau **Spider8 Setup 32bit** (`sp32.zip`) — rulați Setup, apoi copiați `Spider32.dll` din instalare (preferat pentru USB).
3. Companion-e (`Intfac32.dll`, `Papo32.dll`, `Interlnk.dll`) pot veni din `s8exmpl` **sau** din `C:\Program Files (x86)\HBM\catmanEasy\` (versiuni mai noi).
4. Copiați în:
   - dezvoltare: `Spider8DAQ\vendor\`
   - instalat: `C:\Program Files (x86)\UPET AcqLab\vendor\`

Instalatorul UPET **nu** redistribuie DLL / driver proprietar HBM.

Dacă descărcarea e blocată (firewall / redirect hbkworld): deschideți URL-ul de mai sus în browser, secțiunea **Spider8**, fișierele `s8exmpl.zip` / `sp32.zip`.

## 3. Activare

La primul start:

1. Numele titularului (exact ca pe cheie)
2. Cheia `UPET-ACQLAB-XXXX-XXXX-XXXX-XXXX`
3. Activează

Licența se salvează în `%LocalAppData%\UPETAcqLab\license.dat`.

Titular / cheie de distribuție: `dist\LICENSE-DISTRIBUTIE.txt`.

Generare chei personale (HMAC, offline):

```powershell
dotnet run --project tools\LicenseGen -- "Nume Prenume"
dotnet run --project tools\LicenseGen -- "Nume Prenume" --expires 2027-12-31
```

`LicenseServerUrl` e în `license-server.json` lângă `UPETAcqLab.exe` (gol = fără poartă).

## 4. Verificare

1. Porniți **UPET AcqLab** de pe Desktop  
2. Activați licența dacă e cerută  
3. Backend Simulator → Connect → Start  
4. Hardware:
   - USB–serial VCP → backend **Serial** → `COMx` → Connect → Start  
   - USB nativ `USBHBM…` → backend **Spider32.dll** (auto la selectare port) → Connect → Start  
     (necesită `vendor\Spider32.dll` lângă exe; mesaj RO clar dacă lipsește)  
     Dacă InitAll eșuează cu ERR_INVALID_PORT pe USB: înlocuiți DLL-ul cu cel din **Spider8 Setup** (`sp32.zip`), nu doar `s8exmpl`.

## Rebuild

```powershell
cd C:\Users\acer\Desktop\Spider8DAQ
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1
```
