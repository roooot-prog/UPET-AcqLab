# UPET AcqLab
## Manual de utilizare — procedură de laborator

**Universitatea din Petroșani**  
Versiune software: **2.1**  
Titular licență: **dr.ing. Vilceanu**

---

## Cuprins

1. Pregătire PC / ce este necesar  
2. Instalare  
3. Prima pornire și activare licență  
4. Orientare în interfață  
5. Configurare proiect (operator, sample, comentariu)  
6. Alegere backend (Simulator / Serial / Spider32.dll)  
7. Connect — verificare stare  
8. Canale — activare, unități, senzori, Apply  
9. Tare / Preflight / Shunt  
10. Setări înregistrare (Record)  
11. Start streaming — verificare valori live  
12. Record → Stop rec — fișier CSV  
13. Oprire streaming / Disconnect  
14. Analysis  
15. Salvare proiect `.s8proj`  
16. Export raport (HTML / PDF / Excel)  
17. Finalizare totală — checklist închidere sesiune  
18. Depanare  

---

## 1. Pregătire PC / ce este necesar

### 1.1. Cerințe minime

| Element | Cerință |
|--------|---------|
| Sistem de operare | Windows 10 sau Windows 11 |
| Arhitectură pachet | win-x86 (self-contained); runtime .NET inclus |
| Drepturi Administrator | necesare pentru instalare cu setup și pentru instalarea driverelor USB/COM |
| Spațiu disc | minim ~200 MB pentru aplicație + folder `recordings` |
| Hardware (opțional) | Spider8 + adaptor USB–serial (FTDI, CH340, CP210x, PL2303 etc.) |

Fără aparat fizic se lucrează cu backend-ul **Simulator**.

### 1.2. Materiale înainte de start

1. Arhiva de distribuție: `UPET-AcqLab-Portable.zip` (sau pachetul `dist\` din laborator).  
2. Cheia de licență (vezi cap. 3).  
3. Dacă se măsoară pe hardware: cablu USB–serial, Spider8 alimentat, port COM vizibil în Device Manager.  
4. Opțional, pentru backend DLL: fișierul `Spider32.dll` din pachetul oficial HBM (nu este redistribuit de UPET AcqLab).

### 1.3. Verificări pe PC

1. Deconectați temporar alte programe care pot bloca portul COM (terminale seriale, alte DAQ).  
2. Notați calea unde veți păstra proiectele `.s8proj` și unde vor apărea CSV-urile (folderul `recordings` lângă executabil).  
3. Pentru instalare cu setup: cont cu drepturi de Administrator.

---

## 2. Instalare

Există două variante: **instalare cu Setup** (recomandată pe stațiile de laborator) și **rulare portabilă** din folderul extras.

### 2.1. Varianta A — din ZIP pe Desktop (recomandat)

1. Copiați `UPET-AcqLab-Portable.zip` pe Desktop.  
2. Clic dreapta pe arhivă → **Extract All…** (sau Extrage) într-un folder, de exemplu:  
   `C:\Users\<utilizator>\Desktop\UPET-AcqLab`  
3. Deschideți folderul extras. Verificați prezența fișierelor:  
   - `UPETAcqLab.exe`  
   - `Setup.cmd` (sau, în pachetul `dist\`, `UPETAcqLab-Setup.cmd`)  
4. Alegeți una din cele două căi:

#### 2.1.1. Instalare cu Setup (Administrator)

1. Clic dreapta pe `Setup.cmd` / `UPETAcqLab-Setup.cmd` → **Run as administrator** (Rulează ca administrator).  
2. Confirmați UAC dacă apare.  
3. Așteptați copierea fișierelor și, dacă scriptul include drivere, instalarea pachetelor USB/COM din distribuție.  
4. La final, aplicația este instalată tipic în:  
   `C:\Program Files (x86)\UPET AcqLab\`  
5. Pe Desktop (și în meniul Start) apare scurtătura **UPET AcqLab** → `UPETAcqLab.exe`.  
6. Jurnale de instalare (dacă setup-ul le scrie):  
   `%ProgramData%\UPETAcqLab\` (`setup.log`, `driver-install.log`).

#### 2.1.2. Rulare directă (fără instalare în Program Files)

1. Din folderul extras, dublați clic pe `UPETAcqLab.exe`.  
2. Alternativ, rulați `Setup.cmd` din același folder dacă acesta doar lansează executabilul (comportament pachet portabil).  
3. În această variantă, aplicația rulează din folderul extras; înregistrările CSV se creează în subfolderul `recordings` al acelui folder.

### 2.2. Varianta B — din folderul `dist\` al proiectului

1. Deschideți `dist\`.  
2. Rulați ca Administrator `UPETAcqLab-Setup.cmd`.  
3. Locație instalare: `C:\Program Files (x86)\UPET AcqLab\`.  
4. Pornire: scurtătura Desktop **UPET AcqLab**.

### 2.3. Spider32.dll (opțional)

Dacă folosiți backend-ul **Spider32.dll**:

1. Obțineți `Spider32.dll` din pachetul oficial HBM.  
2. Copiați-l în:  
   `C:\Program Files (x86)\UPET AcqLab\vendor\Spider32.dll`  
   (sau în `vendor\` din folderul portabil).  
3. Instalatorul UPET AcqLab **nu** redistribuie DLL-uri proprietare HBM.

### 2.4. După instalare — verificare rapidă

1. Există scurtătura **UPET AcqLab** pe Desktop **sau** `UPETAcqLab.exe` în folderul de lucru.  
2. Nu este necesară instalarea separată a .NET Desktop Runtime (pachet self-contained).

---

## 3. Prima pornire și activare licență

### 3.1. Lansare

1. Porniți aplicația din scurtătura Desktop **UPET AcqLab** sau din `UPETAcqLab.exe`.  
2. Titlul ferestrei principale (după activare): *UPET AcqLab — Universitatea din Petroșani*.

### 3.2. Fereastra de activare (primul start)

Dacă licența nu este încă salvată local, apare fereastra **Activare licență — UPET AcqLab**.

1. În câmpul **Nume titular** introduceți **exact**:  
   `dr.ing. Vilceanu`  
2. În câmpul **Cheie licență** introduceți **exact**:  
   `UPET-ACQLAB-XH3T-2AAA-QKN2-NGST`  
3. Apăsați **Activează**.  
4. Dacă datele sunt corecte, fereastra se închide și se deschide interfața principală.

**Atenție:** ortografia numelui și formatul cheii trebuie să coincidă caracter cu caracter (fără spații în plus, fără variante de scriere).

### 3.3. Unde se salvează licența

| Element | Valoare |
|--------|---------|
| Fișier | `%LocalAppData%\UPETAcqLab\license.dat` |
| Tip | licență personală, fără dată de expirare |
| Titular | dr.ing. Vilceanu |

### 3.4. Dacă licența este deja activată

1. La următoarele porniri, dacă `license.dat` este prezent și valid, **nu** se mai afișează fereastra de activare.  
2. Aplicația deschide direct fereastra principală.  
3. Dacă fișierul lipsește sau este corupt, fereastra de activare reapare — reintroduceți titularul și cheia ca la 3.2.

### 3.5. Dacă activarea eșuează

1. Verificați că numele este exact `dr.ing. Vilceanu`.  
2. Verificați cheia: `UPET-ACQLAB-XH3T-2AAA-QKN2-NGST`.  
3. Nu apăsați **Ieșire** dacă doriți să continuați — fără licență validă aplicația nu pornește.

---

## 4. Orientare în interfață

Lucrați cu aplicația deschisă. Identificați zonele de mai jos înainte de măsurătoare.

### 4.1. Bara de sus (ribbon / antet)

În partea de sus a ferestrei:

- logo UPET, text **UNIVERSITATEA DIN PETROȘANI**, titlu **UPET AcqLab**, numele proiectului curent;  
- butoane: **Info**, casetă **Panel**, **Salvează**, **Încarcă**, **Excel**, **TXT**, **MAT**, **DIAdem**, **HTML**, **PDF**, **Replay**.

### 4.2. Banda de comandă dispozitiv

Sub antet, pe fond alb:

| Control | Rol |
|--------|-----|
| Combo **Dispozitiv** (backend) | Simulator / Serial / Spider32.dll |
| Combo port COM | selectare COMx (relevant pentru Serial) |
| **Porturi** | reîmprospătare listă COM |
| Câmp rată Hz | rată de eșantionare |
| **Connect** | conectare |
| **Disconnect** | deconectare |
| **Start** | pornire streaming |
| **Stop** | oprire streaming |
| **Tare** | tare pe toate canalele |
| **Record** | pornire înregistrare CSV |
| **Stop rec** | oprire înregistrare |
| **Macro** | rulează macro din tab-ul Macros |
| Combo **Experiment** | preset tip experiment |

### 4.3. Tab-uri principale

| Tab | Conținut |
|-----|----------|
| **Măsurare** | Meta, canale, senzori, math, afișaj, setări record, grafice live |
| **Bibliotecă senzori** | editare / import / export senzori, Apply pe canal |
| **Job măsurătoare** | checklist ghidat, tare+record job, raport |
| **Analysis** | încărcare CSV, cursoare, prelucrări, export |
| **Devices** | dispozitive multiple / COM |
| **Macros** | pași automatizați |
| **Calib / Wiring** | calibrare, schema cablare |
| **DataViewer** | browser înregistrări |
| **Alarms / Trigger / Compute** | alarme, trigger avansat, compute |

### 4.4. Bara de stare (jos)

În banda galbenă de jos:

- text de ajutor (**Help** / Info contextual);  
- linia **Status** (stare conectare, streaming, înregistrare, erori);  
- text cursor (valori pe grafic, când e cazul).

Urmăriți permanent linia **Status** după fiecare acțiune (Connect, Start, Record etc.).

### 4.5. Scurtături tastatură

| Tastă | Acțiune |
|-------|---------|
| F5 | Start |
| F6 | Stop |
| F7 | Record |
| F8 | Stop rec |
| F9 | Tare |
| F11 | Panel |

---

## 5. Configurare proiect (operator, sample, comentariu)

1. Deschideți tab-ul **Măsurare**.  
2. În panoul stâng, secțiunea **Meta**:  
   - primul câmp (tooltip **Operator**): numele operatorului;  
   - al doilea câmp (tooltip **Sample**): identificator probă / măsurătoare;  
   - câmpul numeric + eticheta **s max**: durată maximă record (`MaxSeconds`), dacă o veți folosi;  
   - câmpul de jos (tooltip **Comment**): comentariu liber (condiții, încărcare, observații).  
3. Completați cel puțin **Operator** și **Sample** înainte de Record (facilită denumirea automată a CSV și rapoartele).  
4. Alternativ, aceleași câmpuri pot fi completate în tab-ul **Job măsurătoare** (Operator, Sample, Comment, MaxSeconds).

Exemplu tipic:

- Operator: `Vilceanu`  
- Sample: `proba_01`  
- Comment: `încărcare statică, 20 °C`

---

## 6. Alegere backend: Simulator / Serial / Spider32.dll

În banda **Dispozitiv**, selectați backend-ul înainte de **Connect**.

### 6.1. Simulator (test / exercițiu fără aparat)

1. Selectați **Simulator** în combo-ul de backend.  
2. Portul COM nu este necesar.  
3. Setați rata de eșantionare (Hz) dacă procedura o cere.  
4. Continuați cu cap. 7 (**Connect**).

### 6.2. Serial (COM — Spider8 pe USB–serial)

1. Conectați adaptorul USB–serial și alimentați Spider8.  
2. Apăsați **Porturi** pentru a reîmprospăta lista.  
3. Selectați backend **Serial**.  
4. Alegeți portul corect (ex. `COM3`).  
5. Parametri tipici în aplicație: **9600 baud, 8E1**.  
6. Continuați cu **Connect**.

### 6.3. Spider32.dll

1. Verificați că există `vendor\Spider32.dll` în folderul de instalare.  
2. Selectați **Spider32.dll**.  
3. Continuați cu **Connect**.

---

## 7. Connect — verificare stare

1. Apăsați **Connect**.  
2. Urmăriți linia **Status** din partea de jos: trebuie să indice stare de conectat (fără mesaj de eroare).  
3. Dacă Connect eșuează:  
   - Serial: port greșit, driver lipsă, aparat oprit, COM ocupat de alt program;  
   - Spider32.dll: DLL lipsă sau bitness greșit (necesar x86);  
   - Simulator: reîncercați; dacă persistă, reporniți aplicația.  
4. Nu treceți la **Start** până când Status confirmă conexiunea.

---

## 8. Canale: enable, unități, senzori din bibliotecă, Apply

Lucrați în tab-ul **Măsurare**, panoul **Channels (bridge/range/filter/alarm)**.

### 8.1. Activare și parametri canal

1. În tabelul de canale, bifați **On** pentru canalele folosite.  
2. Completați după nevoie:  
   - **Name** — etichetă canal;  
   - **Unit** — unitate de măsură (ex. `N`, `mm`, `mV/V`);  
   - **Bridge**, **Range**, **Filt**, **Scale**;  
   - opțional alarme: **Alm**, **Lo**, **Hi**.

### 8.2. Aplicare senzor din bibliotecă (în Măsurare)

1. În panoul de senzori (arbore pe categorii), alegeți categoria sau folosiți căutarea (cod / nume).  
2. Selectați senzorul (ex. cod tip `FOR-0001`).  
3. În câmpul **CH#** introduceți numărul canalului (1-based).  
4. Apăsați **Apply → CH**.  
5. Pentru toate canalele activate: **Apply all On**.

### 8.3. Tab Bibliotecă senzori (opțional)

1. Deschideți **Bibliotecă senzori**.  
2. Folosiți **Reload** / **Save** / **Import** / **Export** / **Add custom** după nevoie.  
3. Selectați senzorul → **Apply → CH** sau **Apply all On**.

### 8.4. Verificare

1. Verificați că **Name**, **Unit** și **Scale** reflectă senzorul aplicat.  
2. Canalele nefolosite: debifați **On**.

---

## 9. Tare / Preflight / Shunt — ce și când

Toate aceste acțiuni necesită dispozitiv **conectat**. Pentru tare/shunt pe date live, streaming-ul trebuie să fie pornit sau pe cale să fie pornit, după procedura laboratorului.

| Acțiune | Unde | Când |
|--------|------|------|
| **Tare** (ribbon) | banda de sus | zeroing pe toate canalele, înainte de Record |
| **Tare CH** | panoul Channels | tare doar pe canalul selectat (CH#) |
| **Shunt CH** / **Shunt all** | panoul Channels | verificare shunt / semnal de referință pe canal(e) |
| **Preflight** | Channels sau Job | succesiune de verificare (tare + shunt pe canalele relevante); rezultatele apar în lista Preflight |

### Procedură tipică înainte de înregistrare

1. **Connect** → **Start** (streaming).  
2. Așteptați stabilizarea valorilor pe grafic.  
3. Apăsați **Tare** (sau **Tare CH** pe canalul de interes).  
4. Opțional: **Preflight** și citiți liniile din listă.  
5. Opțional: **Shunt CH** / **Shunt all** dacă procedura de laborator cere verificarea shunt.  
6. Continuați cu setările de Record (cap. 10).

---

## 10. Setări record (Manual / Duration / Samples / Trigger, MaxSeconds)

În tab-ul **Măsurare**, secțiunea **Oprire înregistrare** (sub **Afișaj / tip grafic**):

1. Selectați modul din combo:  
   - **Manual** — oprire doar cu **Stop rec**;  
   - **Duration** — se oprește după `MaxSeconds`;  
   - **Samples** — se oprește după `MaxSamples`;  
   - **Trigger** — înregistrare armată pe trigger de nivel.  
2. Completați câmpurile:  
   - **MaxSeconds** (și/sau câmpul **s max** din Meta);  
   - **MaxSamples** (pentru modul Samples).  
3. Opțiuni utile:  
   - **Stats journal** + interval (secunde) — jurnal statistici `*_stats.csv`;  
   - **Stop on alarm**;  
   - **Level trigger**, canal, prag, **Rising**, **PreTrig**;  
   - **Append**, **Auto name** (denumire automată CSV pe baza Sample).  
4. Pentru măsurători standard de laborator, începeți cu **Manual** sau **Duration** + `MaxSeconds` cunoscut.

---

## 11. Start streaming — verificare valori live / plot

1. Asigurați-vă că sunteți pe tab-ul **Măsurare**.  
2. Apăsați **Start** (sau F5).  
3. Verificați Status: streaming activ.  
4. Pe graficele din dreapta urmăriți curbele canalelor activate.  
5. Verificați că valorile au sens (unități, ordin de mărime, fără saturări evidente).  
6. Ajustați tipul de afișaj din **Afișaj / tip grafic** dacă e nevoie (Y(t), Dual Y(t), Y(X), Numeric, Bar, FFT).  
7. Dacă valorile sunt greșite: opriți (**Stop**), reveniți la canale/senzori (cap. 8), tare (cap. 9), apoi **Start** din nou.

**Important:** **Record** nu pornește dacă streaming-ul nu este activ. Mesaj tipic: *Start streaming before recording.*

---

## 12. Record → Stop record → unde este CSV

### 12.1. Pornire înregistrare

1. Cu streaming activ, apăsați **Record** (sau F7).  
2. Dacă **Auto name** este bifat, fișierul se creează automat în folderul `recordings`.  
3. Dacă **Auto name** este debifat, alegeți calea în dialogul de salvare.  
4. Status afișează modul și calea, de exemplu:  
   `Înregistrare (stop manual): …\recordings\proba_01_20260725_103015.csv`

### 12.2. Oprire înregistrare

1. În modul **Manual**: apăsați **Stop rec** (sau F8) când măsurătoarea s-a încheiat.  
2. În **Duration** / **Samples** / **Trigger**: înregistrarea se poate opri automat; verificați Status.  
3. Notați calea fișierului din Status sau din proprietatea ultimei înregistrări.

### 12.3. Unde se află CSV-ul

Folderul implicit:

`<folderul_aplicației>\recordings\`

Exemple:

- Instalare tipică: `C:\Program Files (x86)\UPET AcqLab\recordings\`  
- Rulare portabilă: `…\Desktop\UPET-AcqLab\recordings\`

Nume tipic (Auto name): `<Sample>_yyyyMMdd_HHmmss.csv`  
Opțional, lângă înregistrare: jurnal statistici `*_stats.csv` dacă **Stats journal** a fost activ.

### 12.4. Verificare rapidă

1. Deschideți tab-ul **DataViewer** → **Refresh**.  
2. Selectați fișierul → **Open in Analysis** (sau continuați la cap. 14 cu **Load CSV**).

---

## 13. Oprire streaming / Disconnect

Ordinea corectă după ce ați terminat achiziția:

1. Dacă încă înregistrați: **Stop rec**.  
2. **Stop** (oprire streaming) sau F6.  
3. **Disconnect**.  
4. Verificați Status: deconectat / idle, fără erori.

Nu închideți fereastra aplicației în timp ce Record este activ — opriți mai întâi înregistrarea.

---

## 14. Analysis: Load CSV, cursoare, smooth / fit / FFT, export

1. Deschideți tab-ul **Analysis**.  
2. Apăsați **Load CSV** și selectați înregistrarea din `recordings` (sau folosiți **DataViewer** → **Open in Analysis**).  
3. Selectați canalul (**CH**).  
4. Pe graficul Analysis folosiți zoom / pan; urmăriți textul cursorului din bara de jos.  
5. Prelucrări, după caz:  
   - **Smooth** (fereastră);  
   - **Cut** (interval);  
   - **Peaks**;  
   - **∫ Integral**, **d/dt**;  
   - **Outliers**, **LPF**;  
   - **Scale** / **Off** → **Apply**;  
   - **Fit Y(X)**;  
   - **FFT regiune**, **Export spectru**;  
   - adnotări: **Add @**, **Clear**.  
6. Consultați tabelul de statistici (Min, Max, Mean, σ, RMS, P2P).  
7. Export din Analysis, după nevoie:  
   - **Save CSV**, **Export region**;  
   - **TXT**, **MAT**, **Excel**.

---

## 15. Salvare proiect `.s8proj`

Proiectul păstrează configurația (backend, canale, meta, setări), nu înlocuiește CSV-ul brut.

1. În antet, apăsați **Salvează**.  
2. Alegeți calea și numele fișierului (extensie `.s8proj`).  
3. Pentru reluare ulterioară: **Încarcă** și selectați același `.s8proj`.  
4. Opțional, din **Job măsurătoare**: **Încarcă exemplu** pentru un proiect demo.

Salvați proiectul după ce configurația de canale/senzori este stabilă și după măsurătoare, înainte de închiderea sesiunii.

---

## 16. Export raport HTML / PDF / Excel

Din antet (sau din **Job măsurătoare** / Tools, după date disponibile):

| Buton | Rezultat |
|-------|----------|
| **HTML** | raport HTML pe măsurătoare |
| **PDF** | raport PDF |
| **Excel** | export `.xlsx` |
| **TXT** / **MAT** / **DIAdem** | exporturi alternative |

### Procedură

1. Asigurați-vă că există o înregistrare recentă (sau sesiune încărcată în Analysis).  
2. Completați Meta (Operator, Sample, Comment) — apar în raport.  
3. Apăsați **HTML** și/sau **PDF** și alegeți locația de salvare.  
4. Pentru tabele de lucru: **Excel**.  
5. Verificați că fișierele s-au deschis / există pe disc.

Din **Job măsurătoare**: **Raport HTML**, **Export PDF**.

---

## 17. Finalizare totală — checklist închidere sesiune

Parcurgeți lista **în ordine**, cu aplicația încă deschisă:

| # | Acțiune | Verificare |
|---|---------|------------|
| 1 | **Stop rec** dacă Record era activ | Status nu mai indică înregistrare |
| 2 | **Stop** streaming | Status: streaming oprit |
| 3 | **Disconnect** | Status: deconectat |
| 4 | **Salvează** proiect `.s8proj` | fișier pe disc |
| 5 | Notați / copiați CSV din `recordings\` | calea cunoscută |
| 6 | Export **HTML** / **PDF** / **Excel** dacă laboratorul cere raport | fișiere pe disc |
| 7 | (Opțional) Analysis pe CSV — smooth/fit/FFT și export rezultate | rezultate salvate |
| 8 | Închideți fereastra aplicației (X) | sesiune închisă |

### Unde sunt fișierele (recapitulare)

| Tip | Locație tipică |
|-----|----------------|
| Aplicație instalată | `C:\Program Files (x86)\UPET AcqLab\` |
| Scurtătură | Desktop → **UPET AcqLab** |
| Licență | `%LocalAppData%\UPETAcqLab\license.dat` |
| CSV măsurători | `<instalare_sau_folder_portabil>\recordings\` |
| Proiect | calea aleasă la **Salvează** (`.s8proj`) |
| Rapoarte | calea aleasă la export HTML/PDF/Excel |
| Log setup | `%ProgramData%\UPETAcqLab\` |

Sesiunea este considerată **finalizată** doar după oprirea Record/Stream, Disconnect, salvare proiect (dacă e cazul) și confirmarea locației fișierelor.

---

## 18. Depanare

| Simptom | Ce faceți |
|---------|-----------|
| Aplicația cere cheia la fiecare start | Verificați `%LocalAppData%\UPETAcqLab\license.dat`. Reintroduceți titularul și cheia exact. |
| Aplicația **nu** cere cheia | Licența este deja activă — comportament normal. Continuați cu măsurătoarea. |
| Licență respinsă | Titular exact: `dr.ing. Vilceanu`. Cheie: `UPET-ACQLAB-XH3T-2AAA-QKN2-NGST`. |
| Nu apare port COM | Driver USB–serial; cablu; Device Manager; buton **Porturi**. |
| Connect eșuează (Serial) | Port greșit; aparat oprit; alt program ține COM-ul; baud/protocol. |
| Connect eșuează (Spider32.dll) | Lipsește `vendor\Spider32.dll`; verificați instalarea x86. |
| Record inactiv / mesaj streaming | Faceți **Start**, apoi **Record**. |
| CSV negăsit | Căutați în `recordings` lângă `UPETAcqLab.exe`; verificați Status la Record. |
| Setup eșuează | Rulați ca Administrator; citiți `%ProgramData%\UPETAcqLab\setup.log`. |
| Valorile live nu au sens | Canale On, Scale/Unit, senzor Apply, Tare, backend corect. |
| Shortcut Desktop lipsește | Recreați din `UPETAcqLab.exe` (Send to → Desktop) sau reluați Setup ca Admin. |

---

## Anexă A — date licență (referință)

```
Produs   : UPET AcqLab
Titular  : dr.ing. Vilceanu
Cheie    : UPET-ACQLAB-XH3T-2AAA-QKN2-NGST
Expiră   : perpetuă
Fișier   : %LocalAppData%\UPETAcqLab\license.dat
```

## Anexă B — flux scurt (ordine operațională)

```
Pregătire PC
  → Instalare / lansare EXE
  → Activare licență (dacă e nevoie)
  → Meta (Operator, Sample, Comment)
  → Backend → Connect
  → Canale + senzori Apply
  → Start → Tare / Preflight / Shunt
  → Setări Record
  → Record → Stop rec
  → Stop → Disconnect
  → Analysis (opțional)
  → Salvează .s8proj
  → Export HTML/PDF/Excel
  → Checklist finalizare → închidere aplicație
```

---

*Universitatea din Petroșani — UPET AcqLab, manual de utilizare (procedură de laborator), versiunea 2.1*  
*Responsabil: dr.ing. Vilceanu*
