---
title: "Cablare conector DA-15 și verificarea cu șunt intern (ASS)"
subtitle: "Notă de laborator — Half-bridge + dummy T° pe HBM Spider8-30"
lang: ro
---

# Cablare conector DA-15 și verificarea cu șunt intern (ASS)

**Notă de laborator** — punte Half + timbru activ + dummy de temperatură, același canal

| | |
|---|---|
| **Instituție** | Universitatea din Petroșani |
| **Laborator** | Achiziție de date / tensometrie |
| **Autor soft** | drd. ing. Iucal Ilie |
| **Expert rapoarte** | Șef lucr. dr. ing. Vîlceanu Florin |
| **Hardware** | HBM Spider8-30, adaptor USB USBHBM2186 |
| **Software** | UPET AcqLab **3.3.81** |
| **Document** | v1.0 · 2 septembrie 2026 |
| **Fișier** | `UPET-AcqLab-Cablare-DA15-si-Shunt-Half-dummy.pdf` |

**Clasificare:** notă internă de laborator (procedură + fizică de măsurare). Nu înlocuiește manualul HBM b0405, dar pinout-ul DA-15 de mai jos este cel oficial HBM (nu VGA).

---

## Rezumat executiv (citiți întâi)

1. **Cablarea făcută este corectă** pentru Half + activ (grindă) + dummy T° (placă), 120 Ω, GF = 2.12, același canal. Firele din ștecher au fost confirmate. Citire live ~0,4 µm/m, **Semnal OK**.
2. **Nu se leagă pinul 120 (nici 350 / 700)** și **nu se leagă pinul 15**. Completarea internă nu face parte din punte. Scutul se pune **pe carcasă (housing)**, nu pe pinul 4.
3. **Șuntul intern Spider8 (ASS n,43 / ASS n,42)** este montat **pe rezistența internă de completare** (pin 120/350/700), **nu** pe timbrul de pe grindă. În topologia voastră ASS **nu este în puntea de măsură**.
4. Treapta măsurată **~301 µm/m (≈ 0,16 mV/V)** față de formula quarter **~1893 µm/m** **nu este FAIL de cablaj**. Este un **reziduu** de ordinul sutelor de µm/m, tipic când ASS e în afara punții. În AcqLab **3.3.81** verdictul corect este **PASS (Half+dummy)** — **nu** se aplică Scale din shunt.
5. **Verificarea reală a lanțului:** apăsați ușor pe timbrul **activ** (Citirea trebuie să se miște); dummy-ul rămâne pe martor. Zero CH pe liber înainte de încercare. Păstrați Half+dummy, fără pin 120.

---

## 1. Identificare și scop

Această notă fixează, pentru laboratorul UPET:

- **ce s-a cablat efectiv** pe conectorul DA-15 al canalului (CH1 în sesiunea documentată);
- **de ce** harta pinilor nu este cea de VGA HD15;
- **ce este fizic un șunt (ASS)** într-o punte Wheatstone;
- **când** verificarea cu șunt intern are sens (quarter + pin 120);
- **când nu are sens ca etalon** (Half + două timbre externe — cazul vostru);
- **ce înseamnă** valoarea ~301 µm/m și de ce ecranul a arătat FAIL 301 vs 1893 **înainte de 3.3.81**.

Publicul: operator de laborator, cadru didactic, doctorand. Ton: procedură + explicație, nu marketing.

### 1.1. Configurația sesiunii documentate

| Element | Valoare |
|---------|---------|
| Canal | CH1 (celelalte canale în general Off; CH2 pregătit Full / U2B 5 kN, inactiv) |
| Punte (soft) | **Half** |
| Senzor | **Timbru activ + pas** (activ pe grindă + dummy T° pe placă) |
| Timbre | 2 × 2 fire, 120 Ω, GF = 2,12, același canal |
| Excitație | 2,5 V |
| Rsh kΩ (coloană) | 29,9 — **tabel firmware pentru pin 120; pinul 120 NU este cablat** |
| Scale | \|Scale\| ≈ **1886,79** = 4000 / 2,12 (în captură apărea și **−1886,79** = inversare polaritate) |
| Hz / filtru | 50 Hz / 5 Hz |
| Citire live | ~**0,408 µm/m** (afişaj mare ~0,4085 µm/m) |
| Zero | ~0,16 |
| Semnal | **OK** |
| Șunt măsurat | **301,13 µm/m** ≈ **0,16 mV/V** |
| Diferență permisă | 0,5 % (bandă Catman 0,5–5 %) |

Jurnalul a mai văzut, în sesiuni anterioare, ~0,45 mV/V apoi ~0,16 mV/V, iar versiuni cu bug-uri de conversie au afișat 55 sau 847 µm/m. Toate aceste ordine de mărime sunt **reziduuri**, nu treapta quarter de ~1 mV/V / 1893 µm/m — vezi cap. 11.

---

## 2. Hardware și software

### 2.1. Aparatul

**HBM Spider8-30** (SR30): amplificator tensometric cu 8 canale analogice, conector **DA-15** (D-sub 15 pini, **două rânduri**: 8 + 7), nu HD15 VGA (trei rânduri a câte 5).

Comunicare pe PC-ul de laborator: **USB USBHBM2186** (driver `usbhbm.sys` / DEST), backend **HBM USB** în UPET AcqLab. Comanda de șunt intern este ASCII HBM:

- `ASS n,43` — șunt **ON** (canal `n` **0-based**, același index ca ACT / ASA / OMB);
- `ASS n,42` — șunt **OFF**.

Pentru CH1: `ASS1,43` apoi `ASS1,42`. **Nu** se folosește `CHnSH1` (index 1-based, off-by-one). Firmware-ul **nu raportează ohmi**; ASS doar comută rezistența internă. Valoarea Rsh din tabel (29,9 / 87,35 / 175 kΩ) este din help-ul HBM ASS (2004), mapată în software pe rezistența timbrului.

### 2.2. Software

**UPET AcqLab 3.3.81** (Universitatea din Petroșani).

Comportament relevant după corecția Half+dummy:

- Quarter (un timbru + pin 120/350/700 în circuit): compară treapta cu  
  `ε ≈ 1e6 × Rg / (Rsh × GF)`  
  bandă **±0,5 % … ±5 %** (implicit 0,5 %, ca catman Easy A05566 §4.20). Scale din shunt **doar dacă PASS**.
- Half + activ + dummy T° (două timbre externe): **nu** se punctează față de 1893. Residual mic → **PASS (Half+dummy)**; **CanApplyScale = false**.

Texte din aplicație (3.3.81), citate fidel:

> *shunt intern NU e în punte (2 timbre externe; pin 120 nefolosit). Verificare: apăsare pe activ. Nu e FAIL de cablaj 1893.*

> *Shunt intern ASS stă pe completarea nefolosită (pin 120/350/700). Cu 2 timbre externe nu legați pinul de completare — ASS nu e în punte. Verificați apăsând pe timbrul activ. Nu e FAIL de cablaj 1893; nu Aplică Scale din shunt.*

Asistent Timbru, după Aplică pe canal (Half + dummy):

> *Shunt intern NU e în punte (nu pin 120); PASS (Half+dummy) la residual, nu FAIL 1893. Verificare: apăsare pe activ.*

---

## 3. Conectorul DA-15 — nu este VGA HD15

### 3.1. De ce se confundă

Ambele mufe arată „ca un trapez cu 15 pini”. **Nu sunt interschimbabile.**

| | **DA-15 (Spider8 / HBM)** | **HD15 VGA** |
|--|---------------------------|--------------|
| Rânduri | **2** (8 + 7) | **3** (5 + 5 + 5) |
| Densitate | pini normali D-sub | high-density, pini mai deși |
| Pin 1 (soclu HBM) | **la dreapta rândului lung** | altă geometrie |
| Scut | **carcasa** mufei | de obicei carcasa; mapare video diferită |
| Semnal | ±Ex, Sig±, Sense±, completare 120/350/700 | RGBHV + DDC |

**Nu forțați** un cablu VGA în Spider8 și nu citiți un ghid VGA pentru tensometrie.

### 3.2. Orientare HBM (b0405)

Privind **fața soclului** de pe aparat (găuri spre operator):

- rândul **lung** (8 găuri) este deasupra;
- **pinul 1 este la dreapta** rândului lung;
- rândul **scurt** (7 găuri) este dedesubt.

Numerotarea de pe **fața de lipit** a ștecherului mascul este **în oglindă**. Când lucrați în ștecher, verificați ștanța sau un tester de continuitate pin-la-pin, nu memoria VGA.

### 3.3. Hartă ASCII — față soclu DA-15 (HBM)

```
        vedere FAȚĂ SOCLU (găuri spre dvs.; pin 1 = dreapta sus)
        ┌──────────────────────────────────────────────┐
        │  8    7    6    5    4    3    2    1        │  rând lung (8)
        │ Sig+      +Ex  −Ex      120                 │
        │            │    │       NU                  │
        │            │    │                           │
        │    15   14   13   12   11   10    9         │  rând scurt (7)
        │   Sig−      S+   S−         350  700        │
        │    NU      jum  jum          NU   NU        │
        └──────────────────────────────────────────────┘
              CARCASA / HOUSING  =  SCUT  (nu pinul 4)
```

Legendă scurtă: **S+ / S−** = Sense+ / Sense−. **120 / 350 / 700** = completare internă **doar Quarter**.

---

## 4. Pinout oficial HBM b0405 (funcții)

Aliniat cu comentariile din proiect (completare internă, Sense, scut pe carcasă):

| Pin | Funcție | În cablarea voastră |
|-----|---------|---------------------|
| **5** | **−Ex** (excitație negativă) | **DA** — dummy (placă) |
| **6** | **+Ex** (excitație pozitivă) | **DA** — activ (grindă) |
| **8** | **Sig+** | **DA** — nod comun activ + dummy |
| **15** | **Sig−** | **NU** — doar Full; nefolosit pe Half |
| **3** | Completare **120 Ω** | **NU** — doar Quarter |
| **10** | Completare **350 Ω** | **NU** |
| **9** | Completare **700 Ω** | **NU** |
| **12** | **Sense −** | jumper **5 ↔ 12** |
| **13** | **Sense +** | jumper **6 ↔ 13** |
| **4** | *nu este scut* | **nu legați ecranul aici** |
| **Carcasă** | **Scut / ecran** | **DA** — housing / shell |
| 1, 2, 7, 11, 14 | rezervă / nefolosite în această topologie | lăsați libere |

**Jumperele Sense 6–13 și 5–12** închid bucla de sense **local**, lângă conector. Asta spune amplificatorului că tensiunea de excitație măsurată este cea de la ștecher (compensare cădere pe firele de excitație, pe lungimea scurtă din mufă). Nu înlocuiesc o cablare 6-fire până la timbru; timbrele voastre sunt **2 × 2 fire**.

---

## 5. Ce am făcut — cablarea reală a laboratorului

Documentat ca **ce s-a executat**, nu ca variantă teoretică. Operatorul a confirmat că **firele din ștecher sunt corecte**.

### 5.1. Topologie

**2 × 2 fire, Half + dummy T°, același canal, timbre 120 Ω, GF = 2,12.**

| Rol | Unde stă fizic | Fire (2 fire) | Pini DA-15 |
|-----|----------------|---------------|------------|
| **Activ** | pe **grindă** (piesă solicitată) | 2 | **6 (+Ex)** și **8 (Sig+)** |
| **Dummy / pasiv** | pe **placă** nesolicitată (martor T°) | 2 | **8 (Sig+)** și **5 (−Ex)** |
| Sense | jumpere în ștecher | — | **6–13** și **5–12** |
| Scut | ecran cablu | — | **carcasă**, nu pin 4 |

**Interzis în această topologie:**

- pin **15** (Sig−);
- pin **3 / 10 / 9** (completare 120 / 350 / 700);
- scut pe pin **4**.

### 5.2. De ce dummy-ul este pe placă, nu pe grindă

Cele două timbre văd **aceeași temperatură** (placa lângă piesă), dar **doar activul** vede deformația. Variația rezistivă de la T° se anulează în diferență (raport 1:1, același GF și același R). Dummy-ul **nu** se lipește pe zona solicitată — altfel încetați compensarea T° și măsurați o combinație deformație + T°.

Echivalent conceptual: **NI Quarter Bridge Type II** (un braț activ + un braț pasiv T°), realizat hardware ca **Half** pe Spider8, fără rezistențe interne de completare.

Text din Asistent cablare (aplicație):

> *Activ + timbru pasiv (compensare T°) — echivalent NI Quarter Bridge Type II — pe ACELAȘI canal.*  
> *Gactiv — pe grindă / piesă.*  
> *Gpasiv — pe placă nesolicitată lângă piesă.*  
> *Hardware: Half bridge pe Spider8. Scale BF=1 (4000/GF).*  
> *Nu legați pin 120/350/700. Șuntul intern ASS nu e în punte — residual mic ≠ FAIL 1893.*

### 5.3. Schiță de cablare (ce este în ștecher)

```
   +Ex pin 6 ──────── [ G activ, 120 Ω, grindă ] ──────── pin 8 Sig+
                                                              │
   jumper 6 ── 13 (Sense+)                                    │
                                                              │
   −Ex pin 5 ──────── [ G dummy, 120 Ω, placă  ] ──────── pin 8 Sig+
   jumper 5 ── 12 (Sense−)

   pin 15 Sig−     = NEFOLOSIT
   pin 3  (120 Ω)  = NEFOLOSIT
   pin 10 (350 Ω)  = NEFOLOSIT
   pin 9  (700 Ω)  = NEFOLOSIT
   scut            = CARCASĂ ștecher
```

Schița din aplicație (`WiringDiagrams.AsciiArt`, Half + dummy):

```
+Ex --[Gactiv pe grindă]--+--[Gpasiv compensare T° pe placă]-- -Ex
                          |
                         Sig   (1 canal DAQ)
```

---

## 6. Puntea Wheatstone — de ce arată așa

### 6.1. Puntea în patru brațe

O punte Wheatstone clasică are patru rezistențe, alimentate între **+Ex** și **−Ex**. Ieșirea este diferența dintre cele două mijloace (Sig+ și Sig−). Dezechilibrul (mV/V) este proporțional cu deformația, prin factorul de punte *k* (Gauge Factor) și factorul de punte *B*.

Pe Spider8, **Half** folosește **un** nod de semnal (**Sig+**, pin 8). Al doilea mijloc este intern. **Full** folosește și **Sig−** (pin 15). De aceea **pin 15 rămâne neconectat** la Half.

### 6.2. Cazul vostru: jumătate de punte **închisă afară**

Cele două timbre **120 Ω** formează singure jumătatea de punte:

```
        +Ex (6)
          |
       [Activ 120]
          |
        Sig+ (8)  ---- amplificator (intrare Half)
          |
       [Dummy 120]
          |
        −Ex (5)
```

**Puntea de măsură este închisă în ștecher / pe piesă.** Completarea internă (pin 3 = 120 Ω) **nu participă**. Amplificatorul vede un Half echilibrat (activ ≈ dummy la T° constantă, fără sarcină). De aceea Citirea pe liber este aproape zero (~0,4 µm/m după Zero), **Semnal OK**.

---

## 7. Setări în UPET AcqLab 3.3.81 (canalul documentat)

| Câmp | Valoare | Comentariu |
|------|---------|------------|
| Punte | **Half** | nu Quarter, nu Full |
| Senzor / HalfConfig | **Timbru activ + pas** | etichetă internă: *Activ + timbru pasiv (compensare T°)* |
| GF | **2,12** | din fișa timbrului |
| R Ω | **120** | **nu** intră în formula Scale |
| Exc V | **2,5** | |
| Rsh kΩ | **29,9** | tabel intern Spider8-30 pentru **pin 120**; **informativ** — pinul nu e cablat |
| Scale | **±1886,79** | 4000 / GF; semnul minus = Inversare polaritate |
| Unitate | µm/m | |
| Diferență permisă % | 0,5 | relevantă la **Quarter**; la Half+dummy nu se mai compară cu 1893 |

Rsh 29,9 kΩ **nu înseamnă** „am cablat pinul 120”. Coloana se umple la Connect de pe tabela internă (120 Ω → 29,9 kΩ; 350 Ω → 87,35 kΩ; 700 Ω → 175 kΩ). Pe Half+dummy rămâne un **etichet firmware**, nu o confirmare de pin.

---

## 8. Scale Timbru = 4000 / (*k* · *B*) — R Ω nu intră

Afișajul în µm/m este:

**ε [µm/m] = (citire electrică [mV/V] − tare) × Scale**

Formula Timbru (AcqLab / aceeași fizică ca `StrainScale.FromGaugeFactor`):

**Scale = 4000 / (GF · *B_w*)**

unde *B_w* este factorul Wheatstone al topologiei:

| Topologie | *B_w* | Scale la GF = 2,12 |
|-----------|-------|---------------------|
| Quarter (un timbru activ) | 1 | 4000/2,12 ≈ **1886,8** |
| **Half activ + dummy T°** | **1** | **4000/2,12 ≈ 1886,8** |
| Half Simplu (2 timbre active egale) | 2 | 2000/2,12 ≈ 943,4 |
| Half Poisson | 2(1+ν) | 2000 / (GF·(1+ν)) |
| Half încovoiere / Full | 4 | 1000/2,12 ≈ 471,7 |

**R Ω nu intră în Scale.** Rezistența timbrului contează la **impedanța punții** și la **formula de șunt** `Rg/(Rsh·GF)`, nu la conversia mV/V → µm/m.

Pentru voi: Half+dummy T° este **BF = 1** (un braț „de deformație”, dummy-ul doar compensează T°) → aceeași Scale ca un Quarter. De aceea vedeți ~1887, nu ~943.

Domeniul Autorange (2000 / 5000 / 10 000 / 20 000 µm/m) **nu se scrie în Scale**. Un Scale de tip 91885 sau 10 000 pe canal Timbru este **invalid** — se reaplică Timbru.

---

## 9. Capitolul șunt (ASS) — nucleul notei

### 9.1. Ce este un șunt, fizic

Un **șunt de calibrare** (shunt cal, ASS = Automatic Shunt Switch la HBM) este o **rezistență cunoscută Rsh**, de obicei zeci de kΩ, care se pune **în paralel pe un braț** al punții.

Efect:

1. Brațul Rg || Rsh scade puțin față de Rg.
2. Apare un **ΔR cunoscut**.
3. Puntea se dezechilibrează cu un **Δ(mV/V) cunoscut**.
4. Softul traduce asta în **deformație echivalentă** ε_așteptat.
5. Comparați treapta măsurată cu ε_așteptat → **channel check** (lanț: cablu + amplificator + Scale).

Nu este o „greutate etalon” pe grindă. Este un **semnal electric cunoscut**, injectat în punte.

### 9.2. ΔR și deformația echivalentă

Pentru Rsh ≫ Rg, paralelul:

**R_par = Rg · Rsh / (Rg + Rsh)**

**ΔR / Rg ≈ − Rg / (Rg + Rsh) ≈ − Rg / Rsh**

Definiția Gauge Factor:

**GF = (ΔR / R) / ε    ⇒    ε = (ΔR / R) / GF**

deci, în µm/m (factor 10⁶):

**ε_µm/m ≈ 10⁶ × Rg / (Rsh_Ω × GF)**

(semnul depinde pe care braț se pune șuntul; se lucrează în modul).

Aceasta este formula **când Rsh este într-adevăr în paralel cu un braț al punții de măsură**.

### 9.3. Formula **quarter + completare internă în circuit**

Pe Spider8, quarter-ul tipic este: **un timbru extern** între +Ex și Sig+, plus **rezistența internă de completare** (pin 3 = 120 Ω, sau 10 = 350, sau 9 = 700) între Sig+ și −Ex. Cablare **3 fire** recomandată.

Șuntul intern ASS este **paralel pe completarea internă**. Completarea **este** în punte → formula de mai sus se aplică, cu *B_factor* = 1:

**ε ≈ 10⁶ × Rg / (Rsh × GF)**

**Exemplu de laborator (pin 120):**

- Rg = **120 Ω**
- Rsh = **29,9 kΩ** = 29 900 Ω  (tabel HBM: 120 Ω pin 3 → 29,9 kΩ ±0,1 %)
- GF = **2,12**

**ε = 1e6 × 120 / (29 900 × 2,12) = 1e6 × 120 / 63 388 ≈ 1893 µm/m**

HBM documentează treapta internă ca **~1 mV/V ±1 %**. Verificare: 1 mV/V × Scale 1886,8 ≈ 1887 µm/m, în acord cu 1893 (diferențe de rotunjire Rsh / GF).

**Bandă de acceptare** (stil catman Easy): **±0,5 % … ±5 %** din valoarea așteptată. Implicit AcqLab: **0,5 %**.  
La 1893 µm/m și 0,5 %: fereastra ≈ **1883,5 … 1902,5 µm/m**.

**Scale din shunt** se aplică **doar dacă PASS**. Pe FAIL nu se „corectează” Scale-ul — se repară cablajul / pinul / Rsh.

Pin 350: Rsh ≈ 87,35 kΩ → ε ≈ 10⁶ × 350 / (87 350 × GF). Pin 700: 175 kΩ. **Rsh 29,9 = pin 120**; dacă ați cablat pin 350 dar Rsh e 29,9, treapta nu se potrivește (~25 % jos e un simptom clasic de pin greșit sau 2 fire pe quarter).

### 9.4. Unde stă fizic ASS pe Spider8-30

**Important:** șuntul intern **nu este lipit pe timbrul de pe grindă**. El stă **în aparat**, **în paralel cu rezistența internă de completare** (pin 3 / 10 / 9).

Consecință directă:

| Situație | ASS este în puntea de măsură? | Treaptă așteptată |
|----------|-------------------------------|-------------------|
| Quarter, un timbru, **pin 120 (sau 350/700) cablat** | **DA** | ~1893 (sau formula corespunzătoare) |
| Half, **două timbre externe**, pin 120 **nefolosit** | **NU** | **nu 1893** — doar reziduu |
| Full, 4 timbre | **NU** (completarea internă nu e în punte) | reziduu mic |

Comanda `ASS n,43` **comută oricum** rezistența internă. Dacă acea rezistență **nu e în circuitul de măsură**, vedeți un **cuplaj rezidual** (scurgeri, capacități, cale internă incomplet izolată), nu treapta de 1 mV/V.

Din cod (`ShuntCheck` remarks):

> *Half activ+pasiv T° (two external gauges, BF=1): Scale 4000/GF, but internal ASS sits on unused completion (pin 3/10/9). Do not wire pin 120/350/700 — shunt is NOT in the measuring bridge.*

Prag intern 3.3.81: residual Half+dummy dacă |mV/V| < **0,6** (sau |ε| < 35 % din 1893) → tratat ca reziduu, nu ca treaptă quarter.

### 9.5. Când șuntul **poate** verifica lanțul (într-adevăr)

Condiții **simultan**:

1. Punte **Quarter** în software.
2. **Un singur** timbru extern (activ).
3. Cablare **3 fire**.
4. **Pinul de completare potrivit R**: 120 Ω → **pin 3**; 350 → pin 10; 700 → pin 9.
5. Fără al doilea timbru pe același canal.

Atunci ASS **este** în punte → treapta măsurată ≈ 1893 (exemplul 120 Ω / 29,9 kΩ / GF 2,12) → **PASS/FAIL este semnificativ**. Scale din shunt **doar la PASS**.

**Preț:** pierdeți compensarea T° cu dummy. Quarter-ul clasic nu are al doilea timbru pe martor.

### 9.6. Când șuntul **nu** poate fi folosit ca etalon de canal — **cazul vostru**

**Half + activ + dummy (două timbre 120 Ω externe, același canal).**

- Puntea e **închisă afară**.
- Pinul de completare **nu e în circuit**.
- ASS tot se comută pe 120 Ω intern **nefolosit**.
- Rezultat: **reziduu**, nu 1893.

**Nu este FAIL de cablaj** dacă firele 5 / 6 / 8 / 12 / 13 / carcasă sunt corecte (și sunt — confirmate în ștecher, Semnal OK, Citire ~0).

În 3.3.81: **PASS (Half+dummy)**; **nu** Aplică Scale din shunt.

---

## 10. De ce **nu** se leagă pinul 120 „ca să meargă șuntul”

Tentația: „dacă ASS e pe pin 120, hai să-l legăm și pe el, ca să iasă 1893.”

**Nu faceți asta** peste o punte deja închisă cu două timbre.

Dacă puneți pinul 3 (120 Ω intern) pe nodul Sig+ (pin 8):

- brațul dummy (sau nodul greșit) devine **120 Ω || 120 Ω ≈ 60 Ω**;
- brațul activ rămâne **120 Ω**;
- dezechilibru **uriaș** (puntea nu mai e 1:1);
- **se pierde compensarea T°** (dummy-ul nu mai e egal cu activul);
- formula 1893 **nu mai e validă** (nu mai e quarter curat, nici Half+dummy);
- riscați saturarea domeniului și semnal „nu OK”.

Pinul 120 este **sau** completare pentru **un** timbru quarter, **sau** nefolosit. Nu este un al treilea timbru „bonus”.

---

## 11. Măsurarea voastră: ~301 µm/m, Δ = 84 % față de 1893

### 11.1. Conversie electrică

Scale ≈ 1886,79 µm/m per mV/V.

**301,13 / 1886,79 ≈ 0,160 mV/V ≈ 0,16 mV/V.**

Treapta quarter ar fi ~**1 mV/V** (~1893 µm/m). Ați măsurat ~**16 %** din acea treaptă, adică **Δ ≈ 84 %** dacă (greșit) comparați cu 1893.

**Interpretare corectă:** 0,16 mV/V este **în banda reziduală** documentată (câțiva zeci–sute de µm/m; pragul soft este 0,6 mV/V). **Nu** este cablu rupt, **nu** este pin 350 în loc de 120, **nu** este 2-wire greșit pe quarter.

### 11.2. Jurnal: ~0,45 mV/V apoi ~0,16 mV/V; 55; 847

Reziduurile **variază** (scurgeri pe pinul intern nefolosit, temperatura aparatului, momentul eșantionării OMB față de ASS).

| Observație | Electric (ord. mărime) | × Scale 1887 | Comentariu |
|------------|------------------------|--------------|------------|
| Sesiune curentă | **0,16 mV/V** | **~301 µm/m** | reziduu Half+dummy |
| Jurnal anterior | **~0,45 mV/V** | **~849 µm/m** | tot reziduu (< 0,6 mV/V); ~847 din bug-uri vechi e aceeași ordine |
| Afișaj vechi **55** | conversie greșită | — | bug de unitate / Scale, nu fizica punții |
| Quarter adevărat | **~1 mV/V** | **~1893 µm/m** | **nu** e cazul vostru |

**301 este ordinul de mărime așteptat** pentru două timbre Half cu ASS **în afara** punții, nu un verdict de cablaj.

### 11.3. Ce arăta ecranul **înainte de 3.3.81** (FAIL greșit)

Log tipic (comparație **incorectă** quarter vs Half+dummy):

```
Shunt CH1: 301 µm/m (așteptat 1893 ±0.5%) FAIL — cablaj / Rsh / punte (Δ=84.1%)
Timbru Half+dummy T° GF=2.12 R=120 Rsh=29.9 kΩ
Sugestie: Δ=84.1% față de așteptat. Treapta e sub așteptat (~25% jos e tipic pin 350 sau 2 fire).
```

Sugestia „pin 120 vs 350 / 3 fire” este **corectă pentru Quarter**. Aplicată pe Half+dummy **induce în eroare**: cablajul vostru **nu trebuie** să aibă pin 120.

**În 3.3.81** același 301 µm/m trebuie să arate:

```
Shunt CH1: 301 µm/m — PASS (Half+dummy) — shunt intern NU e în punte
(2 timbre externe; pin 120 nefolosit). Verificare: apăsare pe activ.
Nu e FAIL de cablaj 1893.
```

**FAIL 301 vs 1893 a fost o comparație greșită de software, nu un conector greșit.**

---

## 12. Șuntul „aproximativ” dacă lucrați cu **două timbre**

Întrebarea de laborator: *ce treaptă să aștept dacă dau șuntul cu 2 timbre?*

Răspuns scurt: **nu 1893 și nu 2×1893**. ASS rămâne pe completarea internă nefolosită. Treapta este un **reziduu**, util doar ca **autotest** („ASS s-a comutat, canalul e viu”), nu ca etalon Catman.

### 12.1. Activ + dummy (pasiv T°) — **setup-ul vostru**

| | |
|--|--|
| ASS în punte | **Nu** |
| Pas tipic | **câteva sute µm/m** — exemplu **~301 µm/m ≈ 0,16 mV/V** la Scale ~1887 |
| AcqLab 3.3.81 | **PASS (Half+dummy)** |
| Scale din shunt | **Nu** |
| Verificare reală | apăsare pe **activ**; dummy stă; Citirea se mișcă |

### 12.2. Două timbre **active** (ambele pe piesă — Poisson sau încovoiere)

Electrically **același fapt** privind ASS: completarea internă tot nu e în punte.

| | |
|--|--|
| Pas așteptat de șunt intern | **aceeași ordine** (sute µm/m / ~0,1–0,5 mV/V), **nu** 2×1893 |
| Nu este | verificare Catman de quarter |
| Magnitudinea | poate varia cu scurgerile pe pinul nefolosit; **nu** e valoare de calibrare |
| Scale | pe Poisson / încovoiere **B_w ≠ 1** (2000/GF sau 1000/GF) — altă conversie µm/m, **aceeași** fizică „ASS off-bridge” |

Nu tratați reziduul ca pe o treaptă de 1 mV/V „împărțită la 2”. Nu e.

---

## 13. Tabel de comparație — topologii vs șunt intern

| Topologie | Pin 120 (sau 350/700) | ASS în punte? | Pas așteptat | Rol Shunt în AcqLab 3.3.81 |
|-----------|----------------------|---------------|--------------|------------------------------|
| **Quarter + pin 120** | **DA** | **DA** | **~1893 µm/m** (Rg=120, Rsh=29,9 kΩ, GF=2,12) | Check **±0,5–5 %**; **Scale dacă PASS** |
| **Half activ+dummy** | **NU** | **NU** | rest **~301 µm/m** (ex. laborator) | **PASS (Half+dummy)**; **NU Scale** |
| **2 timbre active (half)** | **NU** | **NU** | rest **similar**, nu 1893 | la fel: autotest, nu etalon |
| **Full 4 timbre** | **NU** | **NU** | rest **mic** | nu e check pe cele 4 timbre |

---

## 14. Procedură recomandată — laboratorul vostru

Păstrați **Half + dummy**, **fără pin 120**. Șuntul rămâne **autotest**, nu calibrare.

### 14.1. Înainte de măsurătoare

1. Verificare vizuală ștecher: **6–8 activ**, **8–5 dummy**, jumpere **6–13** și **5–12**, scut pe **carcasă**. Fără 15, fără 3/9/10.
2. Dummy pe **placă martor**, activ pe **grindă**, **fără sarcină**.
3. Soft: Punte **Half**, senzor **Timbru activ + pas**, GF **2,12**, R **120**, Exc **2,5 V**. Aplică pe canal.
4. Scale ≈ **±1886,8** (4000/GF). Dacă e 91885 / 10 000 / 1 → re-Aplică Timbru. Semnul minus e OK dacă ați inversat polaritatea.
5. Connect (HBM USB) → Start (**nu** Stop; Rec e alt buton) → așteptați Citirea live.
6. **Zero CH** (F9) pe liber.
7. **Verificare mecanică:** apăsare ușoară pe timbrul **activ** → Citirea **trebuie să se miște**. Apăsare pe dummy → aproape nimic (doar T° / paraziți).
8. **Shunt CH** (opțional): așteptați **PASS (Half+dummy)** la ~sute µm/m. **Nu** Aplică Scale din shunt. Dacă încă vedeți FAIL 301 vs 1893, versiunea nu e 3.3.81 sau puntea e setată Quarter din greșeală.

### 14.2. Ce nu faceți

- Nu „reparați” 301 legând pinul 120.
- Nu comparați 301 cu 1893 ca pe un cablu rupt.
- Nu aplicați Scale din shunt pe Half+dummy.
- Nu folosiți cablu VGA / pinout HD15.
- Nu puneți dummy-ul pe zona solicitată.
- Nu confundați Rsh 29,9 kΩ din coloană cu „pin 120 cablat”.

### 14.3. Dacă treceți temporar pe Quarter (un timbru)

Atunci **da**: 3 fire, **pin 3 (120)**, fără dummy pe același canal, Rsh 29,9, așteptați ~1893 ±0,5–5 %, Scale din shunt doar la PASS. Compensarea T° cu dummy **se pierde**. Reveniți la Half+dummy pentru încercarea de grindă.

---

## 15. Checklist rapid (o pagină)

- [ ] DA-15, **nu** HD15 VGA; pin 1 = **dreapta** rândului lung (față soclu)
- [ ] Activ grindă: pini **6** și **8**
- [ ] Dummy placă: pini **8** și **5**
- [ ] Jumpere Sense **6–13**, **5–12**
- [ ] Scut pe **carcasă**, nu pin 4
- [ ] Fără pin **15**, fără **3 / 9 / 10**
- [ ] Soft: Half + Timbru activ+pas, GF 2,12, R 120, Exc 2,5 V, Scale ~1887
- [ ] Rsh 29,9 = tabel intern, **nu** dovada pinului 120
- [ ] Start live, Semnal OK, Citire ~0 pe liber, Zero CH
- [ ] Apăsare pe activ = Citirea se mișcă
- [ ] Shunt: PASS (Half+dummy) la ~301 µm/m; **nu** Scale din shunt
- [ ] FAIL 301 vs 1893 = comparație quarter greșită (pre-3.3.81), nu cablaj

---

## 16. Referințe (proiect și HBM)

| Sursă | Ce confirmă |
|-------|-------------|
| HBM **b0405** / conector DA-15 | Pin 5 −Ex, 6 +Ex, 8 Sig+, 15 Sig−, 3/10/9 completare, 12/13 Sense, scut = housing |
| HBM **ASS.htm** | `ASS n,43` on / `ASS n,42` off; n = index 0-based ca ACT/ASA; ~1 mV/V ±1 % pe completare |
| `Spider8InternalShunt.cs` | 120 Ω → 29,9 kΩ; 350 → 87,35 kΩ; 700 → 175 kΩ |
| `ShuntCheck.cs` | formulă ε; bandă 0,5–5 %; **PASS (Half+dummy)**; prag 0,6 mV/V; test **301 µm/m** |
| `StrainScale.cs` | Scale = 4000/(GF·B); Half+dummy BF=1 |
| `CalibrationWizard.cs` / `WiringDiagrams` | text cablare Half+dummy; „nu pin 120”; ASCII Gactiv/Gpasiv |
| `LabAdvisor.cs` | regulă *shunt-half-dummy*; Quarter rămâne pe Rsh ±0,5–5 % |
| catman Easy A05566 §4.20 | diferență permisă tipică 0,5 % (min 0,5, max 5) |

---

## Anexă A — Hartă pini DA-15 (tabel de banc)

*Față soclu aparat, pin 1 dreapta sus. Marcați cu pix pe o copie printată.*

| Pin | Rând | Funcție | Voastră (CH1 Half+dummy) |
|-----|------|---------|--------------------------|
| 1 | lung, dreapta | — | liber |
| 2 | lung | — | liber |
| 3 | lung | Completare 120 Ω | **liber** |
| 4 | lung | *nu scut* | **liber** (nu ecran) |
| 5 | lung | **−Ex** | **dummy** |
| 6 | lung | **+Ex** | **activ** |
| 7 | lung | — | liber |
| 8 | lung, stânga | **Sig+** | **nod comun** |
| 9 | scurt | Completare 700 Ω | **liber** |
| 10 | scurt | Completare 350 Ω | **liber** |
| 11 | scurt | — | liber |
| 12 | scurt | Sense − | **jumper la 5** |
| 13 | scurt | Sense + | **jumper la 6** |
| 14 | scurt | — | liber |
| 15 | scurt, stânga | Sig− (Full) | **liber** |
| Housing | — | Scut | **ecran** |

---

## Anexă B — Calcule numerice (sesiunea documentată)

**Scale**

4000 / 2,12 = **1886,79245283…** (afișaj 1886,7924528301885)

**Șunt quarter (referință, NU se aplică la voi ca etalon)**

1e6 × 120 / (29 900 × 2,12) = 120 000 000 / 63 388 ≈ **1893,1 µm/m**

**Reziduu măsurat**

301,13 / 1886,79245 ≈ **0,1596 mV/V** ≈ **0,16 mV/V**

Δ față de 1893 (comparație **nevalidă**): |301 − 1893| / 1893 ≈ **84,1 %**

**Prag residual 3.3.81:** |0,16| < 0,6 mV/V → **PASS (Half+dummy)**

---

## Anexă C — Mesaje AcqLab 3.3.81 (Half+dummy)

| Situație | Verdict |
|----------|---------|
| Residual ~301 µm/m / 0,16 mV/V | `PASS (Half+dummy)` + text „shunt intern NU e în punte…” |
| Residual mai mare, dar nu gunoi | `Info (Half+dummy) — SKIP verificare quarter` |
| Citire absurdă (> 1e5 µm/m) | FAIL cablaj (gunoi, nu 1893) |
| Punte setată **Quarter** din greșeală, același 301 | **FAIL** 301 vs 1893 — **verificați tipul de punte**, nu ștecherul |
| Simulator | `simulator — nu e shunt real` (fără PASS vs Rsh) |

---

*Universitatea din Petroșani · UPET AcqLab 3.3.81 · HBM Spider8-30 / USBHBM2186*  
*Autor soft: drd. ing. Iucal Ilie · Expert rapoarte: Șef lucr. dr. ing. Vîlceanu Florin*  
*Document v1.0 — 2 septembrie 2026 — notă de laborator internă*
