# Gap analysis: UPET AcqLab vs catmanEasy (overnight final → 2026-08-05)

**Honest scope:** full overnight parity with HBM catmanEasy/AP 5.0.2 is **not** possible. Target = Spider8 lab DAQ, WPF .NET 8, UI RO.

## P0 — Done / Hardened
| Item | Status |
|---|---|
| Channel Rec/Punte/Exc/Rsh/Scale/Offset + Rec all/none | **Done** |
| PeakInterval; ACT/P15 | **Done / Verified** |
| USBHBM Connect | **Hardened** Intfac 3×+Flush+DEST; UI soft-retry USB+Serial; watchdog PnP/catman |
| Simulator / Serial | **Hardened** |

## P1 — Done
Hold/Freeze · Copy/Export live · Alarm log · `# MARK` + Analysis import · Soft Zero clear-all · Filtru Rate/10 · Preflight Intfac handles · PDF MARK/EST · EXC/ICR/ASF · Shunt multi-fallback · Poly2/Crest/Dual Y(t)

## P2/P3 — Remaining
Camera live, rainflow, BIN write, EasyScript, `.SDB`, QuantumX — out of scope.

## Build
**v2.9.3** → `publish-v2\UPETAcqLab.exe`  
Program Files: still **2.3.0** (ACL) — Admin: `tools\Deploy-To-ProgramFiles.ps1`
