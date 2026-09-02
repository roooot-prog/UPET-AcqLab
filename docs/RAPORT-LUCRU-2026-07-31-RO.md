# Raport de lucru UPET AcqLab — până la 31.07.2026 08:00

**Produs:** UPET AcqLab (Universitatea din Petroșani)  
**Cerință utilizator:** aplicație industrială aprofundată până mâine 8:00  
**Data raport:** 2026-07-31 ~08:00 (București)

---

## 1. Până când s-a lucrat

| Interval | Ce s-a întâmplat |
|----------|------------------|
| **30.07 ~17:00–18:20** | Sprint intens documentat: Faze 1–5, UI aerisit, click-dreapta senzori, driveri, publish |
| **30.07 18:10** | Instalare **NT_IODRV / DirectNT**; verificare **HBM USB IO** |
| **30.07 18:15–18:16** | **Ultimul publish** pe `publish-v2` + `Program Files (x86)\UPET AcqLab` (v2.2.0) |
| **30.07 18:20 → 31.07 08:00** | Continuare agenți overnight (research industrial / polish); **exe-ul instalat pe disc** = build-ul de la **18:16** |

**Concluzie timp:** munca grea + livrarea pe disc s-a concentrat pe **30 iulie, până ~18:16**. Termenul 08:00 a fost ținta; raportul de dimineață confirmă starea la acea oră.

---

## 2. Ce este livrat (funcțional)

### Interfață
- Moduri **Live / Configure / Review** (Ctrl+1/2/3)
- Ribbon: Dispozitiv · Măsurare · Vizualizare
- Plot **Y(t)** inginieresc (axe, grilă, canale pe grafic)
- Grilă canale aerisită: Citire, Semnal, Hz, Filtru, Senzor, Alm, Zero
- **Click-dreapta pe senzor:** Aplică, Zero/Offset 0, Edit, Duplică, Șterge, Copiază, Detalii
- **Click-dreapta pe canal:** Zero, Shunt, Aplică senzor
- Badge sănătate SIM/USB/OK/LIVE/REC; scurtături F1

### Soft DAQ
- Connect: Simulator / Serial / **Spider32** (auto pe USBHBM)
- Job măsurătoare (checklist), macros, alarme
- DataViewer: redare, comparare A/B, export CSV/Excel
- Bibliotecă senzori **v4** (CRUD RO, fără `.SDB` HBM)
- Export: CSV, Excel, TXT, MAT, DIAdem, HTML, PDF
- Proiect `.s8proj` (Ctrl+S/O)
- Integrări: cameră (fișiere) + webhook (fără preview video live)

### Drivere (30.07 ~18:10)
- **HBM USB IO** — deja OK, serviciu USBHBM = RUNNING
- **NT_IODRV → DirectNT** — instalat; serviciu **STOPPED până la reboot Windows**

---

## 3. Ce nu e „absolut ideal” încă

| Limitare | Detaliu |
|----------|---------|
| Connect USB stabil | `S8_InitAll(PORT_USB)` a eșuat (−1) în teste; catman folosește stack propriu, nu Spider32 din kit-ul Easy |
| DirectNT | Necesită **restart Windows** (încă STOPPED la 08:00) |
| Clonă catman / NI FastView | **Interzis / imposibil** legal + timp — UPET = produs original, fluxuri inspirate |
| Cameră live | Doar atașare fișiere / Camera Windows, fără DirectShow preview |
| Publish după 18:16 | Dacă agenții au mai editat noaptea, e nevoie de **republicare** ca să apară pe Desktop |

---

## 4. Unde rulezi aplicația acum

```
C:\Program Files (x86)\UPET AcqLab\UPETAcqLab.exe
```
sau shortcut Desktop **UPET AcqLab**  
sau `C:\Users\acer\Desktop\Spider8DAQ\publish-v2\UPETAcqLab.exe`

**Timestamp exe (ultimele fișiere):** 30.07.2026 18:15:58

---

## 5. Pași imediat după 08:00

1. **Repornește Windows** (activează DirectNT).
2. Închide **catman Easy** dacă e deschis.
3. Deschide UPET → Porturi **USBHBM2186** → backend Spider32 → Connect → Start.
4. Dacă Connect tot eșuează: folosește **Simulator** pentru UI; pe hardware rămâne de investigat path-ul USB vs stack HBM.

---

## 6. Documente aferente

- `docs/OVERNIGHT-STATUS-RO.md` — status overnight + checkpoint 08:00  
- `docs/catman-feature-map-upet.md` — hartă funcții pe faze  
- `docs/DRIVERS-RO.md` — raport instalare driveri  
- `vendor/nt_iodrv/README.md` — NT_IODRV local  
