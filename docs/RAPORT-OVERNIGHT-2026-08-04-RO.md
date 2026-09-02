# UPET AcqLab — raport overnight FINAL 2026-08-04 → 05 (RO)

**Build final:** `publish-v2` → **v2.9.3** (stable, 24 tests + SmokeSim OK)  
**Stop:** ~07:02 UTC+3 · **Deadline user:** 08:00  

**Nu este și nu pretinde clonă 100% catmanEasy.**

---

## 1. Ce s-a analizat
- UPET AcqLab WPF (canale, job, analiză, export, senzori JSON, ACT/P15)
- catmanEasy 5.0.2 instalat (store flag, Peak storage, Excitation, shunt, SoftSetup)
- Gap: `docs/GAP-catmanEasy.md`

## 2. Ce s-a făcut overnight (v2.4 → v2.9.3)

### Canale / UX
- Grilă Rec · Punte · Exc · Rsh · Scale · Offset · Rec all/none
- PeakInterval · Dual Y(t) · Hold/Freeze (Pause)
- Copiază/Export live · Copiază stats · Export jurnal alarme
- `# MARK` mid-Record (Ctrl+Shift+M) + import la Analysis
- Soft Zero clear-all · Filtru BE = Rate/10 · Preflight F10 (+ Intfac handles)

### Hardware
- Hybrid USB: Intfac **3×** + FlushIn + DEST SoftSetup
- Connect soft-retry UI (USB + Serial) · Serial Open 2×
- Shunt multi-fallback · EXC SoftSetup · ICR/ASF Intfac
- Watchdog: nu reconectează dacă PnP lipsă sau catman rulează

### Export / calitate
- CSV `#` meta + `# MARK` · PDF cu MARK/EST/cal notes
- Poly2 · Crest · 24 unit tests · SmokeSim OK

## 3. Ce rămâne
1. USBHBM „ca Easy” = stack HBM licențiat (out of scope / no crack)
2. Shunt fără valoare pe unele dialecte Intfac
3. P2/P3: camera live, rainflow, BIN write, EasyScript, `.SDB`, QuantumX
4. **Program Files** încă **v2.3.0** (ACL) — necesită Admin:
   `tools\Deploy-To-ProgramFiles.ps1`

## 4. Unde rulezi (exe)
```
C:\Users\acer\Desktop\Spider8DAQ\publish-v2\UPETAcqLab.exe
```
Demo lab: **Simulator → Connect → Start (F5) → Record (F7)** · **Pause** Hold · **F10** Preflight
