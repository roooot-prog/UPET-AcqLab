# Hartă funcții catman Easy → UPET AcqLab

**Produs:** UPET AcqLab (Universitatea din Petroșani)  
**Referință studiată (documentație + captură):** catman Easy V5.0.2 Evaluation — doar ca **listă de fluxuri DAQ**, nu ca specificație de clonă UI.  
**Data:** 2026-07-30

---

## Decizie de produs (obligatorie)

**UPET AcqLab = produs original UPET**, cu similaritate funcțională țintă pe fluxurile de laborator (canale, joburi, vizualizare, data viewer, bibliotecă senzori), în română.

**NU** este și **nu va fi**:

- clonă 99% / trade-dress pixel-perfect a catman Easy;
- redistribuire de iconițe, ribbon, branding HBM, formate proprietare (ex. `.SDB` Sensordatabase HBM) sau binare extrase din `setup.exe` / instalarea catman;
- crack sau ocolire a licenței catman.

Ce **poate** fi oglindit ca workflow: enumerare dispozitiv, grilă canale (citire / rată·filtru / senzor·funcție / zero), zero balance, start măsurare, joburi, ploturi, viewer înregistrări, bibliotecă senzori **proprie** (JSON UPET).

Ce **rămâne IP HBM**: driver `usbhbm.sys`, API Spider32.dll, baza `.SDB`, asset-uri UI, protocoale nedocumentate reverse-engineered din binare.

---

## Situație USB pe acest PC (evidență)

| Observație | Detaliu |
|---|---|
| Dispozitiv PnP | `USB\VID_10D1&PID_0101\USBHBM2186` |
| Class | `HBM_USB_DEVICES` |
| Service | `USBHBM` (`usbhbm.sys` 3.3.3.2) |
| Port COM | **Lipsă** — nu e VCP |
| catman | Enumeră `Spider8 [USB USBHBM2186]` |
| UPET (înainte P0) | Căuta COM → nu vedea ținta; backend Serial inutil pe acest path |
| UPET (după P0) | Detectează `USBHBM2186` în listă porturi; cale corectă = **Spider32.dll** + DLL oficial în `vendor\` |
| UPET (Faza 2) | DLL din `s8exmpl.zip` + companion-e (preferat din catmanEasy instalat: Intfac32/Papo32); interop S8_*; auto-backend Spider32; timeout Connect 15s. **catman Easy NU livrează Spider32.dll** |

---

## A. FILE (Fișier / proiect)

| Funcție | Ce face în catman | Status UPET | Prioritate | Note |
|---|---|---|---|---|
| Proiect nou / salvare / încărcare | Persistență setup | **Există** (.s8proj) | P1 | Format original UPET |
| Export CSV / Excel / raport | Date + rapoarte | **Există** (CSV, XLSX, TXT, MAT, DIAdem, HTML, PDF) | P1 | |
| Import BIN catman | Citește înregistrări HBM | **Parțial** (inspector BIN) | P2 | Doar citire defensivă; nu clonă format scriere |
| Licențiere HBM | Activare comercială | **N/A** | — | UPET are licență proprie; nu deblochează catman |

---

## B. DAQ CHANNELS (Canale DAQ) — focus P0

| Funcție | Ce face în catman | Status UPET | Prioritate | Note |
|---|---|---|---|---|
| Etichetă dispozitiv `Spider8 [USB …]` | Arată nume + path USB | **Parțial → îmbunătățit P0** | **P0** | Label + scan USBHBM |
| Grilă CH: Reading | Valoare live | **Parțial → îmbunătățit P0** | **P0** | Coloană „Citire” pe grilă |
| Sample rate · Filter | Hz / BE | **Parțial → îmbunătățit P0** | **P0** | Coloană combinată + editabile |
| Sensor · Function (bridge) | Senzor + tip punte | **Parțial → îmbunătățit P0** | **P0** | Bibliotecă UPET, nu .SDB |
| Zero value | Offset zero balance | **Parțial → îmbunătățit P0** | **P0** | Tare / Zero |
| Zero balance Execute | Tare hardware/soft | **Există** (Tare) | **P0** | Etichete RO „Zero / Tare” |
| Start measurement | Streaming | **Există** | **P0** | „Start măsurare” |
| TEDS | Citire EEPROM senzor | **Parțial** (auto-map bibliotecă) | P2 | Spider8 clasic fără TEDS fizic |
| Adaptation / mV/V | Scalare | **Există** (Scale, Range mV/V) | P1 | |
| Computation channels | Canale calculate | **Există** (Math + Online compute) | P1 | |
| Filtre pe canal | BE / LP | **Există** (FilterHz) | P1 | |
| Digital input bitmask (CH8) | DI | **Parțial Faza 2** | P2 | Doar dacă API raportează `IDS_S8DigIO` → rând **CH DI**; fără inventare |
| Cascade multi-device | Mai multe Spider8 | **Există** (Devices / cascade) | P1 | |

---

## C. DAQ JOBS (Joburi măsurătoare)

| Funcție | Ce face în catman | Status UPET | Prioritate | Note |
|---|---|---|---|---|
| Wizard / checklist job | Flux ghidat | **Livrat Faza 3** (tab Job: meta → rată/filtru → senzori → Start → tare → Record → viz/raport) | P1 | |
| Trigger / durată / samples | Oprire înregistrare | **Livrat Faza 3** (RecordStopMode în Job + Measure) | P1 | |
| Macro / sequencer | Pași automatizați | **Livrat Faza 3** (pași noi + WaitOperator + save/load) | P2 | Nu e clona sequencer catman AP |
| Alarme la job | Stop on alarm | **Există** | P2 | |

---

## D. VISUALIZATION (Vizualizare)

| Funcție | Ce face în catman | Status UPET | Prioritate | Note |
|---|---|---|---|---|
| Y(t), multi-trace | Plot live | **Există** (ScottPlot) | P1 | UI original UPET |
| Y(X), FFT, bar, numeric | Tipuri afișaj | **Există** + preset Experiment + Job „Aplică vizualizare” | P1 | |
| Template display | Layout-uri salvate | **Livrat Faza 3** (built-in + salvare/încărcare `.s8tpl.json`) | P2 | |
| Video / cam | Cameră | **Parțial Faza 4** | P3 | Tab Integrări: atașare imagini, Camera Windows, watch folder — fără UI HBM |

---

## E. DATAVIEWER

| Funcție | Ce face în catman | Status UPET | Prioritate | Note |
|---|---|---|---|---|
| Browser înregistrări | Listă fișiere | **Există → Faza 4** (grilă meta + căutare) | P1 | |
| Analysis offline | Cursoare, stats, export | **Există** (Analysis) + adnotări pe plot | P1 | |
| Compare recordings | Două curbe | **Există** (Faza 3 + Faza 4 pe plot DataViewer) | P2 | CH A/B + Δmean |
| Playback / review | Redare cursor | **Livrat Faza 4** | P1 | Play/Pause/Stop + viteză + scrubber |
| Export din viewer | CSV / Excel | **Livrat Faza 4** | P1 | |
| DB măsurători | Index / search | **Există** (Health / DB) | P2 | |

---

## F. SENSOR DATABASE

| Funcție | Ce face în catman | Status UPET | Prioritate | Note legale |
|---|---|---|---|---|
| SENSORDATABASE.SDB | Bază HBM | **Lipsă** (intenționat) | — | **Format proprietar HBM — nu se clonează** |
| My sensors | Senzori user | **Există → Faza 4** (JSON + UI RO, catalog v4 + minerit UPET) | P1 | Bibliotecă UPET |
| Import/export senzori | Schimb cataloage | **Există** (JSON) | P1 | Nu import .SDB |
| Edit / duplicate | CRUD bibliotecă | **Există → Faza 4** | P1 | Update / Duplicate / Delete |
| Aplicare pe canal | Mapare | **Există** | P0 | |

---

## Roadmap pe faze

### Faza 1 (P0) — livrat
- Hartă funcții + decizie legală/produs (acest document).
- Detectare `USBHBM*` + etichetă dispozitiv tip `Spider8 [USB …]`.
- Grilă canale: Citire / Rată·Filtru / Senzor·Funcție / Zero.
- Zero / Start măsurare vizibile în RO; live update pe grilă.
- Note instalator `installer\drivers\hbm-usb-io\` (fără redistribuire driver).

### Faza 2 (P1) — în curs / livrat acum
- [x] Localizare / obținere `Spider32.dll` oficial (`s8exmpl.zip` HBM) + companion-e în `vendor\` (nu în git).
- [x] Clarificat: media `HBM-catman-502` / catman Easy instalat **nu** conțin `Spider32.dll` loose (catman vorbește USBHBM pe stack propriu).
- [x] Interop corect: `S8_InitAll(port,mode)`, `S8_MeasOneVal`, `S8_GetChanSettings` (nu exporturi inventate).
- [x] Auto-select backend Spider32 la USBHBM (RefreshPorts + SelectedPort + Connect); erori RO (DLL lipsă / port busy / INVALID_PORT / timeout 15s).
- [x] Deploy `vendor\Spider32.dll` (+ companions) lângă exe via csproj `CopyToOutputDirectory` (exclude `_hbm_download`).
- [x] CH DI doar dacă tip canal = `IDS_S8DigIO` (nu inventat din DigIO citibil).
- [x] Docs: **Serial(COM)** vs **Spider32(USB/USBHBM)** în `INSTALL-RO.md`.
- [ ] Connect USB stabil pe hardware — DLL din `s8exmpl` (1999) = COM/LPT; pentru USBHBM e nevoie de DLL din **Spider8 Setup** (`sp32.zip`). Dacă InitAll → ERR_INVALID_PORT pe PORT_USB, înlocuiți DLL-ul după Setup.
- [x] Polish job + vizualizare → **livrat în Faza 3**.

### De ce nu e Spider32 în `HBM-catman-502`?

Folderul de pe Desktop este **media de instalare** catman Easy 5.0.2 Evaluation:

- fișiere loose: `Start.exe`, `layout.xml`, `version.txt`, `catmanEasy.pdf`, `Setup\`
- `Setup\setup.exe` (~156 MB InstallShield) + Firmware / Prerequisites / Tech Notes
- **0× `.dll` loose**, **nicio** `Spider32.dll`, **niciun** `sp32.zip` / DriverSetups separat

După instalare, `C:\Program Files (x86)\HBM\catmanEasy\` are zeci de DLL-uri proprii (ex. `HBM_Scan.dll`, `acqkrn32.dll`) plus `Intfac32.dll` / `Papo32.dll` / `interlnk.dll`, dar **tot fără `Spider32.dll`**. catman Easy **nu importă** `Spider32` — enumeratează `USBHBM2186` pe driverul `usbhbm.sys` + stack-ul său. Pentru UPET, `Spider32.dll` vine din pachetele legacy Spider8 (`s8exmpl.zip` / `sp32.zip`), nu din kit-ul catman.

### Faza 3 (P2) — livrat
- [x] Analysis/DataViewer avansat: comparare canale A/B + sumar stats (Δmean); adnotări pe plot Analysis; template-uri built-in + salvare/încărcare `.s8tpl.json`.
- [x] Job măsurătoare: checklist 7 pași (include rată/filtru), Aplică rată/filtru, RecordStopMode, Aplică vizualizare, sync checklist.
- [x] Macros mai bogate: `SetSampleRate`, `ApplyFilters`, `WaitOperator` (+ Continuă macro), `Beep`; preset „Job lab”; save/load `.s8macro.json`.
- [x] Health/watchdog: mesaje RO; self-test + verificare PnP USBHBM; watchdog verifică absența USB înainte de reconnect.
- [x] TEDS: rămâne auto-map bibliotecă (Spider8 clasic fără EEPROM) — nu forțat pe hardware modern.
- Viz live: preset Experiment + Job „Aplică vizualizare” + template-uri (YT / Dual / Numeric / Bars / Y(X) / FFT).

### Faza 4 (P3) — livrat / parțial
- [x] **DataViewer** UPET: browser meta (N, CH, durată, mărime), redare Play/Pause/Stop + scrubber, comparare A/B pe plot DataViewer+Analysis, export CSV/Excel, folder/ștergere.
- [x] **Bibliotecă senzori** catalog **v4**: categorie „Minerit / geotehnică UPET” + edit/duplicate/delete în UI RO (JSON propriu — **fără** `.SDB` HBM).
- [x] **Integrări opționale**: tab **Integrări** — cameră laborator (atașare / Camera Windows / watch pe Record → `camera/`) + export 3rd-party (folder outbound + webhook JSON, `data/integrations.json`).
- [ ] Cameră live DirectShow/OpenCV (preview video) — neinclus (opțional viitor; fără deps native fragile pe win-x86).
- [ ] Sync temporal fine / overlay >2 fișiere — opțional ulterior.
- [ ] Connect USB stabil pe hardware real — rest Faza 2 (DLL din Spider8 Setup dacă e nevoie).
- Fără copiere tech-notes UI HBM.

### Notă UX grafic (workflow tip laborator, UI original UPET)

Îmbunătățiri de uzabilitate pe vizualizare / măsurare (inspirate de fluxul catman Easy / FlexLogger / Dewesoft, **fără** clonă de chrome/iconițe HBM/NI):

- **Spațiu de lucru**: moduri **Live** · **Configure** · **Review** (Ctrl+1/2/3) — tab Măsurare / Job / DataViewer.
- **Badge sănătate**: SIM / USB / OK / LIVE / REC + detalii Connect RO (DLL lipsă, timeout, USBHBM≠COM).
- **Scurtături F1** documentate în panoul ribbon.
- **Ribbon grupat**: DISPOZITIV · MĂSURARE (Start / Stop / Zero / Record) · VIZUALIZARE.
- **Plot Y(t) inginerească**: axe cu unități, grilă, legendă cu nume+unit, panou „Canale pe grafic”.
- **Grilă canale**: On · Graf · Citire · Semnal · Hz · Filtru · Senzor · Alm Lo/Hi · Zero; click dreapta Zero/Shunt/Alarmă/Offset/Apply.
- **Defaults**: template / experiment implicit **Y(t)**; limbă UI **română**; branding UPET.

### Faza 5 (P0 industrial overnight 2026-07-30/31) — livrat

- [x] Live / Configure / Review + navigare tab.
- [x] Health badge + SessionBadge + erori Connect RO.
- [x] Panou scurtături F1 + Ctrl+S/O/E/H.
- [x] Coloane editabile Hz / Filtru / Alm pe grila canale.
- [x] Publish `publish-v2` + Setup Program Files (v2.2.0).
- [ ] Connect USB stabil pe hardware — rest Faza 2 (DLL din Spider8 Setup dacă e nevoie).

---

## Surse consultate (docs only)

- Captură utilizator: ribbon FILE / DAQ CHANNELS / DAQ JOBS / VISUALIZATION / DATAVIEWER / SENSOR DATABASE.
- Fluxuri documentate FlexLogger / DIAdem / Dewesoft / catman Easy (doar ca listă de workflow, nu UI clone).
- `HBM-catman-502\layout.xml`, `version.txt` (5.0.2), `catmanEasy.pdf`, `ReleaseNotes.pdf`, Tech Notes (inventar tematic din titluri; fără reverse-engineering binare).
- Cod sursă UPET AcqLab din `Spider8DAQ`.
- PnP Windows: `USBHBM2186` + `usbhbm.sys` deja instalat pe stație.
