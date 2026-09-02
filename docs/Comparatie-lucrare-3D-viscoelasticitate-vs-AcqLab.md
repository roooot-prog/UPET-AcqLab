# Comparație: caracterizare 3D LVE (Graziani et al., 2019) vs UPET AcqLab

**Verdict scurt:** **Nu.** Lucrarea PDF **nu** este aplicația UPET AcqLab / Spider8DAQ și **nu** descrie același experiment. Se suprapun doar familia DAQ (HBM Spider8), geometria cilindrică și măsurarea deformației transversale la mijlocul înălțimii. Restul (material, încărcare, mărimi, model constitutiv, scop) e alt domeniu.

**PDF sursă (neschimbat):** `C:\Users\acer\Desktop\Experimental_characterization_of_the_3D_linear_vis.pdf`  
**Aplicație:** UPET AcqLab (repo `Spider8DAQ`) — WPF, UI română, laborator UPET Petroșani.

---

## 1. Traducere-rezumat a lucrării

### Identificare

| Câmp | Valoare |
|------|---------|
| **Titlu original** | Experimental characterization of the 3D linear viscoelastic behavior of cold recycled bitumen emulsion mixtures |
| **Titlu RO** | Caracterizarea experimentală a comportării viscoelastice liniare 3D a amestecurilor reciclate la rece cu emulsie de bitum |
| **Autori** | Andrea Graziani (autor corespondent), Carlotta Godenzoni, Francesco Canestrari |
| **Afiliere** | Dipartimento di Ingegneria Civile, Edile e Architettura, Università Politecnica delle Marche, Ancona 60131, Italia |
| **Revistă** | *Journal of Traffic and Transportation Engineering (English Edition)* |
| **An / volum / pagini** | 2019, **6**(4), 324–336 |
| **DOI** | [10.1016/j.jtte.2019.03.001](https://doi.org/10.1016/j.jtte.2019.03.001) |
| **Primit / revizuit / acceptat** | 5 dec. 2018 / 15 mar. 2019 / 23 mar. 2019; online 19 iun. 2019 |
| **Licență** | CC BY-NC-ND 4.0 (open access, Periodical Offices of Chang'an University / Elsevier) |
| **Cuvinte-cheie** | emulsie de bitum, reciclare la rece, modul complex, coeficient Poisson complex, model Huet–Sayegh |

Numele de fișier `Experimental_characterization_of_the_3D_linear_vis.pdf` este titlul trunchiat; lucrarea **nu** este despre sare, rocă, oțel sau despre software-ul AcqLab.

### Scop

Să caracterizeze **comportarea viscoelastică liniară tridimensională (3D LVE)** a unui material tratat ciment–bitum (**CBTM**, *cement-bitumen treated material*) produs la rece cu emulsie de bitum și ciment, prin măsurarea simultană a **modulului lui Young complex** \(E^*\) și a **coeficientului Poisson complex** \(\nu^*\) (în lucrare notat \(v^*\)). Pentru comparație: un **asfalt turnat la cald (HMA)** cu 25% RAP și liant polimer-modificat (PMB). În 3D izotrop sunt necesare **două** funcții de răspuns independente; de obicei \(\nu\) e luat constant (\(\nu = 0{,}35\)) — aici \(\nu^*\) e măsurat explicit.

### Probe și materiale

- **CBTM (laborator):** 50% agregat RAP + 50% agregat reciclat (strat tratat cu ciment frezat); emulsie cationică **C 60 B 10** (EN 13808), dozaj 3,0% (≈ 1,8% bitum proaspăt); ciment pozzolanic **IV/A (P) 42,5R**, dozaj 2,0%; apă totală 5%. Compactare gyratory Ø 150 mm (600 kPa, 30 rpm, 1,25°). Maturare **14 zile la 40 °C**.
- **HMA (uzină):** 75% agregat virgin calcaros + 25% RAP; bitum total 4,8% (PMB cu 3,8% SBS; penetrație 71 × 0,1 mm; inel–bilă 68 °C). Compactare gyratory imediat după prelevare.
- **Pregătire mecanică:** carotare la Ø **100 mm**, tăiere capete, capare cu rășină bicomponentă, lustruire. Înălțimi ≈ 148–150 mm (HMA) și 129–132 mm (CBTM). Goluri: HMA ≈ 3,8–4,2%; CBTM ≈ 12,4%.
- **Nu** sunt probe de sare, granit, oțel sau beton de laborator UPET.

### Montaj experimental (ce se măsoară)

- Presă **servo-hidraulică** cu cameră termostatată.
- Încărcare **haversine** axială, **control pe tensiune**, amplitudine de deformație axială foarte mică: **30 × 10⁻⁶** (CBTM) și **50 × 10⁻⁶** (HMA) — domeniul LVE, nu rupere UCS.
- Unsoare (vaselină) pe platouri **ca să reducă frecarea** și să nu altereze starea uniaxială (opusul filozofiei de bombare).
- **Mărci tensometrice** TML P60 (60 mm, 120 Ω), două perechi lipite diametral la mijlocul înălțimii: axial + transversal. Semipunte Wheatstone; epruvetă dummy pentru compensare termică.
- **DAQ:** unitate portabilă **HBM Spider 8**. Frecvența de eșantionare \(f_s = 100\, f_t\) (100 puncte/ciclu).
- **Program:** baleiaj de frecvență **12, 4, 1, 0,25, 0,10 Hz** la **0, 10, 20, 30, 40, 50 °C**; 20 de cicluri/frecvență; pauză ≥ 300 s între frecvențe.

### Prelucrare semnal

Semnalul de tensiune = componentă de fluaj (compresiune medie) + sinusoidă + armonici de la bucla de control. Se separă media mobilă pe o perioadă (\(N = 100\)), apoi aproximare Fourier de ordin 3; **doar prima armonică** (\(k = 1\)) intră în \(E^*\) și \(\nu^*\).

### Model constitutiv (ecuații; simbolurile din lucrare)

Semnale (fazori; \(j^2 = -1\), \(\omega = 2\pi f\)):

\[
\sigma_1(t) = \sigma_{1,0}\sin(\omega t + \delta_1)
\quad\to\quad
\sigma_1^*(\omega) = \sigma_{1,0}\exp\bigl[j(\omega t + \delta_1)\bigr]
\tag{1}
\]

\[
\varepsilon_1(t) = \varepsilon_{1,0}\sin(\omega t)
\quad\to\quad
\varepsilon_1^*(\omega) = \varepsilon_{1,0}\exp\bigl[j(\omega t)\bigr]
\tag{2}
\]

\[
\varepsilon_3(t) = \varepsilon_2(t) = \varepsilon_{2,0}\sin(\omega t - \delta_2)
\quad\to\quad
\varepsilon_2^*(\omega) = \varepsilon_{2,0}\exp\bigl[j(\omega t - \delta_2)\bigr]
\tag{3}
\]

**Modulul lui Young complex** și **Poisson complex**:

\[
E^*(\omega) = \frac{\sigma_1^*}{\varepsilon_1^*} = \frac{\sigma_{1,0}}{\varepsilon_{1,0}}\exp(j\delta_1) = E_0\exp(j\delta_E)
\tag{4}
\]

\[
\nu^*(\omega) = -\frac{\varepsilon_2^*}{\varepsilon_1^*} = -\frac{\varepsilon_{2,0}}{\varepsilon_{1,0}}\exp(-j\delta_2) = \nu_0\exp(-j\delta_\nu)
\tag{5}
\]

cu \(E_0 = \sigma_{1,0}/\varepsilon_{1,0}\), \(\nu_0 = \varepsilon_{2,0}/\varepsilon_{1,0}\), \(\delta_E = \delta_1\), \(\delta_\nu = \delta_2 - \pi\).

**Model 2S2P1D** (HMA; două arcuri, două elemente fracționare, un amortizor):

\[
E^*(\omega) = E_e + \frac{E_g - E_e}{1 + \delta(j\omega\tau)^{-k} + (j\omega\tau)^{-h} + (j\omega\tau\beta)^{-1}}
\tag{11}
\]

Când vâscozitatea amortizorului \(\eta\to\infty\), 2S2P1D se reduce la **Huet–Sayegh (HS)** — folosit pentru CBTM (lipsa curgerii vâscoase la temperaturi înalte, datorită cimentului).

**Principiul de superpoziție timp–temperatură (TTSP)** și **WLF**:

\[
\tau(T) = a_{T_\mathrm{ref}}(T)\,\tau_\mathrm{ref}
\tag{13}
\]

\[
\log a_{T_\mathrm{ref}}(T) = -\frac{C_1(T - T_\mathrm{ref})}{C_2 + (T - T_\mathrm{ref})}
\tag{14}
\]

\(\nu^*(\omega)\) pentru HMA se ajustează analog (ec. 15), cu aceiași factori de deplasare ca la \(E^*\). **Nu** se raportează serie Prony, \(G^*(\omega)\) sau \(K^*(\omega)\) ca mărimi primare (perechea măsurată este \(E^*\), \(\nu^*\)).

### Rezultate principale

- **HMA:** \(E_0\) de la ≈ 431 MPa (50 °C, 0,1 Hz) la ≈ 20 547 MPa (0 °C, 12 Hz); \(\delta_E\) de la ≈ 31,7° la ≈ 5,4°. TTSP valid pentru \(E^*\) **și** \(\nu^*\); 2S2P1D reproduce curbele master (\(T_\mathrm{ref} = 0\) °C). \(\nu_0\) variază cu \(f\) și \(T\) (ex. 0,239 … 0,425 pe HMA1); pe HMA2 \(\nu_0\) poate depăși 0,5 (cunoscut în literatură pentru HMA).
- **CBTM:** \(E_0\) doar ≈ 3151 … 10 519 MPa; \(\delta_E\) ≈ 3° … 12°. TTSP valid pentru \(E^*\); model **HS**. La **frecvențe reduse înalte** (temperaturi joase) HMA e mai rigid; la **frecvențe reduse joase** (temperaturi înalte) CBTM e mai rigid (legături cimentice). \(\tau_\mathrm{ref}\) CBTM e cu ≈ 5 ordine de mărime mai mare decât HMA (relaxare mai slabă).
- **\(\nu^*\) CBTM:** \(\nu_0\) aproape **constant ≈ 0,15** (0,130 … 0,169), ca la beton/materiale tratate cu ciment; TTSP pentru \(\nu^*\) **nu** poate fi confirmat. \(\delta_\nu\) e foarte mic; amplitudinea transversală e aproape de rezoluția sistemului (≈ 1 µε).

### Concluzii ale autorilor (restrânse)

HMA cu PMB și 25% RAP rămâne termo-reologic simplu în 3D (aceiași \(a_T\) pentru \(E^*\) și \(\nu^*\)), compatibil cu 2S2P1D. CBTM: \(E^*\) se poate modela cu HS + TTSP; \(\nu^*\) **nu** se comportă ca la HMA — rămâne ≈ 0,15, „cement-like”.

### Limitări (din lucrare)

- Doar două epruvete/amestec; \(\nu^*\) CBTM e zgomotos (deformație transversală ≈ 4,5 µε).
- Compresiune ciclică haversine (nu tensiune–compresiune simetrică ENTPE); componentă de fluaj filtrată.
- Ipoteză de izotropie (două mărci transversale, nu inel dens de senzori).
- Subiectul \(\nu^*\) pentru amestecuri reci e nou; puține date de comparație.
- Rezultatele sunt pentru **pavaje bituminoase**, nu pentru minerit / sare / oțel.

### Glosar EN → RO (termeni din lucrare)

| Engleză | Română | Notă |
|---------|--------|------|
| 3D linear viscoelasticity (LVE) | viscoelasticitate liniară 3D | două funcții independente dacă materialul e izotrop |
| Complex Young's modulus \(E^*\) | modulul lui Young complex | \(E_0\) = modul de rigiditate (stiffness / dynamic modulus) |
| Complex Poisson's ratio \(\nu^*\) (\(v^*\)) | coeficient Poisson complex | nu \(\nu\) elastic static |
| Phase angle \(\delta_E\), \(\delta_\nu\) | unghi de fază (pierdere) | \(\delta_\nu\) tipic &lt; 10° |
| Master curve | curbă master | după deplasare \(a_T\) |
| Time–temperature superposition (TTSP) | superpoziție timp–temperatură | „termo-reologic simplu” |
| Shift factor \(a_T\), WLF | factor de deplasare, lege WLF | \(C_1\), \(C_2\) |
| 2S2P1D | 2 arcuri + 2 elemente parabolice + 1 amortizor | Olard & Di Benedetto |
| Huet–Sayegh (HS) | model Huet–Sayegh | 2S2P1D fără amortizorul newtonian |
| Cole–Cole / Black diagram | diagrame Cole–Cole / Black | \(E_2\) vs \(E_1\); \(E_0\) vs \(\delta_E\) |
| Strain gauge (SG) | marcă tensometrică | TML P60 |
| Haversine cyclic compression | compresiune ciclică haversine | sinusoidă pe o precompresiune |
| RAP / HMA / CBTM / CBE / CRM | agregat reciclat / asfalt cald / material ciment–bitum / emulsie la rece / amestec reciclat la rece | materiale de drum, nu probe UPET |
| Stiffness modulus | modul de rigiditate | norma lui \(E^*\) |
| Reduced frequency | frecvență redusă | \(\omega\cdot a_T\) |

---

## 2. Tabel comparativ (lucrare vs AcqLab)

| Criteriu | Lucrarea Graziani et al. (2019) | UPET AcqLab (Spider8DAQ) | Aceeași? |
|----------|-------------------------------|---------------------------|----------|
| **Ce este** | Articol științific de inginerie rutieră (JTTE) | Aplicație WPF de achiziție de laborator (DAQ) | **Nu** |
| **Instituție** | Univ. Politecnica delle Marche (Ancona) | Universitatea din Petroșani (UPET) | **Nu** |
| **Scopul fizic** | Caracterizare **3D LVE**: \(E^*(\omega,T)\) și \(\nu^*(\omega,T)\) | Compresiune cilindru **Contur**: geometria umflării la **Fmax**; plus tensometrie, forță UCS, librărie probe | **Nu** |
| **Testul fizic** | Compresiune **ciclică haversine**, control pe tensiune, **30–50 µε**, 0,1–12 Hz, 0–50 °C | Compresiune **monotonă / ciclu presă** până la Fmax (sau cursă), deformații **mari** (mm), fără protocol de cameră termică | **Nu** |
| **Filozofia platourilor** | Vaselină ca **să evite** confinarea / bombarea | Bombarea **este obiectul** (frecare pe platouri, mijlocul se umflă) | **Opus** |
| **Probe** | CBTM (emulsie + ciment + RAP) și HMA (PMB + 25% RAP), Ø 100 mm | Catalog: **oțel, rocă (UCS), sare/fluaj, beton EN 12390**; cilindru/tub operator | **Nu** (nici asfalt în catalog) |
| **Senzori pe probă** | 2×2 **mărci tensometrice** (axial + transversal), mijloc | **Contur:** 4 sau **8 senzori de deplasare radială** \(u_i\) [mm] pe circumferință + cursă presă + F opțional. **Tensometrie:** tip de experiment **separat** | Parțial: familia tensometrie există, inelul de 8 LVDT **nu** e montajul lucrării |
| **DAQ** | **HBM Spider 8** portabil, \(f_s = 100 f_t\), semipunte Wheatstone | **HBM Spider8** (Serial / Spider32 / USBHBM) + **simulator** 8 canale | **Aceeași familie de aparat**, altă aplicație (nu catman, nu scriptul Graziani) |
| **Mărimi raportate** | \(E_0\), \(\delta_E\), \(\nu_0\), \(\delta_\nu\); curbe master; Cole–Cole, Black; \(E_g, E_e, k, h, \delta, \beta, \tau_\mathrm{ref}, C_1, C_2\) | \(u_i\), ovalitate, **index de bombare** \(u_\mathrm{med}/|\mathrm{cursă}|\), **D_capat**, **D_mijloc**, hartă \(u(z)\) model \(\sin^2\), \(\sigma=F/A_0\), \(\varepsilon=|\mathrm{cursă}|/L_0\); UCS \(= F_\max/A\) (pachet ISRM) | **Nu** |
| **\(G\), \(K\), Prony** | Nu (teoretic 3D LVE admite \(G^*\), \(K^*\); ei măsoară \(E^*\), \(\nu^*\)) | Nu există identificarea Prony / \(G\) / \(K\) | Niciuna nu e „acel” experiment Prony |
| **Poisson** | \(\nu^*(\omega)\) complex, fază, master curves | Layout Poisson: \(\nu_\mathrm{ap} = -d\varepsilon_t/d\varepsilon_l\) (regresie pe fereastră); **nu** \(\nu^*\) | Doar analogie superficială |
| **Model constitutiv** | 2S2P1D (HMA), Huet–Sayegh (CBTM), WLF | Geometrie de butoi + tensiune tehnică; pachete **SteelMetal / IsrmUcs / SaltCreep / En12390**; **fără** fit Norton, **fără** 2S2P1D | **Nu** |
| **Simulator** | — | Ciclu de compresiune cu **bombare** pe 8 senzori (nu sinusoidă LVE 30 µε) | Nu reproduce lucrarea |
| **Reproducere?** | Protocol: presă servo + cameră T + haversine + Fourier 1-armonică + TTSP | AcqLab poate **înregistra** mărci pe Spider8 (tensometrie) și \(\varepsilon_l,\varepsilon_t\); **nu** generează \(E^*\), curbe master, HS/2S2P1D, baleiaj T–f | **Nu** cu software-ul actual |

### Ce *este* înrudit (fără a confunda)

1. **Spider8** — același tip de DAQ HBM; lucrarea îl folosește pentru mărci, AcqLab pentru laboratorul UPET (inclusiv LVDT/forță).
2. **Cilindru comprimat axial**, măsură transversală **la mijlocul înălțimii**.
3. **Tensometrie** ca *mod separat* în AcqLab: același *gen* de traductor ca în lucrare, dar fără analiza LVE complexă.
4. **Sarea** din catalog are pachet „fluaj”, dar **nu** există identificare \(E^*(f,T)\) și **nu** e materialul CBTM/HMA.
5. **Poisson** în AcqLab e un \(\nu\) aparent static, nu \(\nu^*(\omega)\).

### Ce lipsește în AcqLab ca să reproducă experimentul din lucrare

- Control **haversine** în domeniul 30–50 µε și sincronizare \(f_s = 100 f_t\).
- Cameră termică 0–50 °C și protocol de **frequency sweep**.
- Extragere **prima armonică** (filtru medie mobilă + Fourier) → \(E^*\), \(\nu^*\).
- Diagrame Cole–Cole / Black, curbe master, WLF, fit **2S2P1D / Huet–Sayegh**.
- Montaj **2 perechi de mărci** (nu 8 LVDT de bombare); unsoare pe platouri, nu evidențierea frecării.
- Materiale **HMA/CBTM** și compactare gyratory — în afara scopului UPET.

Modului **Contur** i-ar lipsi și *sensul* testului: lucrarea **minimizează** bombarea; Contur o **măsoară**.

---

## 3. Concluzie: e la fel?

**Nu — nu este aceeași lucrare și nu este același experiment.** PDF-ul este articolul Graziani, Godenzoni & Canestrari (2019) despre **viscoelasticitatea liniară 3D a amestecurilor bituminoase reciclate la rece**, cu \(E^*\) și \(\nu^*\) din cicluri sinusoidale mici pe mărci tensometrice, prelucrate pe un **HBM Spider 8**. UPET AcqLab este un **program de laborator** pentru același *tip* de aparat, dar experimentul-fanion **Compresiune cilindru – contur** măsoară **umflarea radială** \(u_i\) cu 4/8 senzori, **bombare**, **D_capat / D_mijloc**, \(\sigma\)–\(\varepsilon\) și UCS-style pe **sare / rocă / oțel**, plus un simulator de bombare — deci geometrie de deformație mare, nu caracterizare reologică 3D. Singura suprapunere solidă este **hardware-ul Spider8** și faptul că un cilindru comprimat *poate* avea deformație transversală la mijloc; cantitățile, protocolul, materialele și modelele **nu coincid**. Nu copiați titlul, autorii sau ecuațiile 2S2P1D/HS ca și cum ar descrie AcqLab.

---

*Document intern de comparație. Lucrarea rămâne citabilă după DOI; PDF-ul de pe Desktop nu a fost modificat. Aplicația nu a fost publicată din acest fișier.*
