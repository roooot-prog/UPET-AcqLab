# UPET AcqLab — status overnight (RO)

**Țintă funcțională:** 2026-07-31 (sesiune „rulează până e funcțional”)  
**Ultima actualizare:** 2026-07-31 ~08:47 (București)  
**Build instalat:** `C:\Program Files (x86)\UPET AcqLab\` — exe **08:46:00**  
**Mod:** auto, fără commit git

---

## Checklist Functional (verde = PASS)

| # | Criteriu | Stare | Dovezi |
|---|----------|-------|--------|
| 1 | App pornește fără dialog crash | **PASS** | Fereastră `UPET AcqLab — Universitatea din Petroșani` |
| 2 | Bibliotecă senzori count > 0 | **PASS** | **299** senzori (AppData `vendor\sensors.json`); tree UIA ~313 noduri |
| 3 | Grilă canale citibilă (fără overlap) | **PASS** | DataGrid 8 rânduri; card canale lărgit (560px) + scroll orizontal |
| 4 | Toolbar cards pe un rând; mutabile/redimensionabile pe Măsurare | **PASS** | Carduri ribbon DISPOZITIV/MĂSURARE/VIZUALIZARE; **Reset layout** OK; layout salvat în `%LocalAppData%\UPETAcqLab\ui-layout.json` |
| 5a | Simulator Connect+Start | **PASS** | UIA Conectare→Start; headless `tools/SmokeSim` |
| 5b | USBHBM / Spider32 | **BEST-EFFORT FAIL (documentat)** | `S8_InitAll(PORT_USB)=-1` pe port liber; DirectNT **1275** (blocat); catman `HBM_Scan` = rețea, nu USB |
| 6 | Record/export pe Simulator | **PASS** | UI: `meas_20260731_084701.csv` **31943** bytes; headless ~49 samples CSV |
| 7 | Latest build în Program Files | **PASS** | Deploy 08:46 |

---

## Cum folosești acum

### Simulator (calea oficială de lab / demo)

1. Deschide `C:\Program Files (x86)\UPET AcqLab\UPETAcqLab.exe`
2. Card **DISPOZITIV** → backend **Simulator** (implicit; **nu** se mai forțează Spider32 când USB e băgat)
3. **Conectare** → **Start** (F5) → **Record** (F7)
4. CSV: `%LocalAppData%\UPETAcqLab\recordings\`
5. Export Excel/TXT/MAT: meniu **Export / Replay** (după înregistrare) sau tab **Vizualizare date**
6. **Stop înregistrare** F8 · **Stop măsurare** F6

### USB HBM (USBHBM2186)

- Driver **USBHBM** = RUNNING; dispozitiv vizibil în Device Manager.
- **UPET + Spider32.dll nu deschid USBHBM** (`InitAll = -1`). Catman Easy folosește **alt stack**.
- **DirectNT** instalat dar **blocat (1275)** pe Win11 — relevant doar LPT, nu USB.
- **HBM_Scan / Intfac32** din catmanEasy: fără interop USB curat pentru UPET (scan rețea / COM-GPIB).
- Pentru achiziție USB reală: **catman Easy**. Pentru UPET: **Simulator** sau **Serial RS232 pe COM**.

### Serial RS232

1. Adaptor pe port COM real (nu ținta `USB USBHBM…`)
2. Backend **Serial** → alegeți COMx → Conectare → Start

---

## Fixuri din această sesiune

- Simulator **nu** mai e deturnat automat pe Spider32 când USBHBM e prezent
- Recordings + examples → `%LocalAppData%\UPETAcqLab\` (scriere sub Program Files)
- CSV writer **AutoFlush** (fișiere goale la kill forțat)
- Card canale mai lat; layout default fără overlap; AutomationProperties pe Conectare/Start/Stop/Record
- Smoke headless: `tools/SmokeSim`
- Mesaje USB: recomandă Simulator / Serial, fără buclă de retry

---

## Servicii / hardware

| Item | Stare |
|------|--------|
| USBHBM (driver) | RUNNING |
| DirectNT | STOPPED, exit **1275** (driver blocked) |
| PnP | `USB\VID_10D1&PID_0101\USBHBM2186` OK |
| COM ports | niciun COM RS232 listat pe stație |

---

## Următorul pas (opțional, nu blochează „funcțional”)

- Achiziție USB nativă UPET ar necesita stack tip catman (licență/API HBM) — în afara scope-ului actual.
- Serial pe COM când există adaptor fizic.
