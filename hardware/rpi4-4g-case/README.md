# Carcasă Raspberry Pi 4 + HAT 4G (2 antene tip router)

Carcasă 3D-printabilă, din două piese (bază + capac), gândită ca un router compact: **Raspberry Pi 4 Model B** stivuit cu un **HAT 4G** (Waveshare SIM7600G-H / SIM7600E-H sau echivalent) și **două antene paddle SMA** pe capac.

Sursa parametrică: [`rpi4_4g_hat_case.scad`](rpi4_4g_hat_case.scad) (OpenSCAD 2021.01+).

## Ce încapă

| Componentă | Dimensiune luată în calcul | Montaj |
|---|---|---|
| Raspberry Pi 4 Model B | PCB 85 × 56 mm, găuri M2.5 la (3.5, 3.5) / (61.5, 52.5) | 4 stâlpi în bază, piulițe M2.5 sub podea |
| HAT 4G (SIM7600x) | 56.21 × 65.15 mm, pe header-ul GPIO | același șuruburi M2.5 prin HAT + Pi |
| 2 antene LTE paddle „tip router” | SMA tată articulat, ~160–200 mm | bulkhead SMA femelă în capac, găuri D Ø6.55 / flat 6.05, distanță 50 mm |
| Antenă GNSS (opțional) | SMA | knockout Ø6.5 mm (membrană 0.45 mm) lângă MAIN |

Înălțimea internă lasă loc pentru header GPIO, modulul SIM7600, cablul USB scurt HAT↔Pi și pigtail-urile U.FL→SMA care vin de obicei cu HAT-ul.

## Dimensiuni de gabarit

Valori cu parametrii default din `.scad`:

- **Corp (fără antene):** ~108 × 79 × 36 mm
- **Cavitate internă:** ~104 × 74 × 31 mm
- **Distanță antene MAIN–AUX:** 50 mm
- **Înălțime cu antene paddle verticale:** ~200 mm

Dimensiunile exacte apar în consola OpenSCAD la `part = "preview"`.

Porturile Pi sunt scoase în pereți:

- USB-C (alimentare), 2× micro-HDMI, jack 3.5 mm
- 2× USB 3.0, 2× USB 2.0, Ethernet
- microSD
- fante de ventilație pe două laturi + grilaj în capac
- 4 șuruburi M3 de capac (doar pe laturile SD și GPIO — pe USB/HDMI nu încape stâlp)

## BOM

| Cant. | Piesă | Notă |
|---|---|---|
| 1 | Raspberry Pi 4 Model B | — |
| 1 | HAT 4G cu 2 conectori antenă (MAIN + AUX) | Waveshare SIM7600G-H recomandat |
| 2 | Antene LTE paddle SMA tată, articulate | cele de router/CPE, 698–2700 MHz |
| 2 | Pigtail U.FL (IPEX) → SMA bulkhead femelă | de obicei incluse cu HAT-ul |
| 4 | Șurub M2.5 × 12–16 + piuliță M2.5 | prindere Pi + HAT |
| 4 | Șurub M3 × 12 | capac, autofiletant în PETG |
| 4 | Picioare cauciuc Ø10–11 mm | locașuri în bază |
| 1 | Cartela nano-SIM 4G | capacul se scoate pentru inserare |
| 1 | Sursă USB-C 5 V / 3 A | oficială Pi 4 |

## Print 3D

**Material:** PETG (Pi 4 + modemul se încing; PLA se poate moale).

| Setare | Valoare |
|---|---|
| Înălțime strat | 0.20 mm |
| Pereti | 3 |
| Umplutură | 25–30 % gyroid |
| Temperatură pat | 75–80 °C (PETG) |
| Suporturi | nu, dacă printezi cum scrie mai jos |

Orientare:

1. **Baza** — podeaua pe pat.
2. **Capacul** — buza (lip) pe pat, fața exterioară în sus. Textele MAIN / AUX / 4G rămân citibile pe exterior.

Export STL din OpenSCAD:

```powershell
cd hardware\rpi4-4g-case
openscad -D "part=`\"base`\""      -o rpi4_4g_case_base.stl      rpi4_4g_hat_case.scad
openscad -D "part=`\"lid`\""       -o rpi4_4g_case_lid.stl       rpi4_4g_hat_case.scad
openscad -D "part=`\"print_set`\"" -o rpi4_4g_case_print_set.stl rpi4_4g_hat_case.scad
```

sau rulează `.\export-stl.ps1`.

Dacă capacul intră greu, crește `fit_clear` de la `0.30` la `0.40` și re-exportă doar capacul.

## Asamblare

1. Montează Pi-ul pe stâlpii din bază cu 4× M2.5. Piulițele stau în hexagoanele de sub podea.
2. Pune HAT-ul pe GPIO. Dacă HAT-ul are găurile de 58 mm, treci aceleași M2.5 prin HAT + Pi (distanțiere de alamă 11 mm între plăci, opțional).
3. Conectează cablul USB scurt HAT → un port USB al Pi-ului; ține-l pe lângă peretele Ethernet.
4. Înfiletează cele două SMA bulkhead în găurile D din capac: **MAIN** și **AUX**. Flat-ul D blochează rotirea.
5. Leagă pigtail-urile U.FL: MAIN și AUX pe HAT, GPS pe knockout doar dacă folosești GNSS (sparge membrana cu un burghiu Ø6.5).
6. Înșurubează antenele paddle pe SMA, verticale, ca la un router. Distanța de 50 mm e suficientă pentru diversitate LTE.
7. Închide capacul cu 4× M3. Cartela SIM: scoți capacul.

## Parametri OpenSCAD de ajustat

| Parametru | Default | Când îl schimbi |
|---|---|---|
| `hat_stack` | 26 | HAT mai înalt (condensator mare, radiator) → 30 |
| `sma_spacing` | 50 | antene paddle late (>22 mm) → 55–60 |
| `lid_t` | 2.2 | nu trece de ~2.4 mm: filetul SMA de kit nu prinde piulița |
| `fit_clear` | 0.30 | imprimantă care umflă piesele |
| `gps_knockout` | true | `false` dacă nu vrei gaura GPS |
| `label_text` | `"4G"` | text pe capac |
| `wall_mount` | true | urechi de agățat pe perete, pe latura GPIO |

## Note RF și termice

- Nu pune carcasa pe un suport metalic sub antene. Paddle-urile SMA sunt de obicei ground-plane independent, dar metalul deasupra lor strică diagrama.
- Ține antenele verticale, la 90°, nu lipite una de alta.
- Pi 4 + modem LTE disipă ~6–8 W. Grilajul din capac nu e opțional. Dacă vezi throttling, crește `hat_stack` și adaugă un radiator pe SoC înainte de a pune HAT-ul (dacă încape), sau un GPIO riser de 16 mm.

## Layout intern (privit de sus)

```
        GPIO / antene (spate, ca la router)
   ┌─────────────────────────────────────┐
   │  [M3]     SMA MAIN    SMA AUX  [M3] │
   │  [M3]     (knockout GPS)            │
   │     ┌────────── HAT 65 mm ───┐      │
   │ SD  │  SIM7600    GPIO header │ USB │
   │     │           Raspberry Pi 4│ ETH │
   │     └─────────────────────────┘     │
   │  [M3]   USB-C  HDMI  HDMI  jack     │
   └─────────────────────────────────────┘
        alimentare / video (față)
```
