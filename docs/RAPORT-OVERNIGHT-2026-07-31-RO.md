# Raport overnight — 2026-07-31 (RO)

**Produs:** UPET AcqLab v2.2.0  
**Stație:** Desktop Spider8DAQ / Program Files (x86)\UPET AcqLab

## Crash startup (dimineață)

- **Simptom:** dialog „Nu s-a putut deschide fereastra principală” / `StaticResourceExtension`.
- **Cauză reală:** `MainWindow.xaml` referă stilul `{StaticResource InfoToggleOn}`, dar stilul lipsea din `App.xaml` (build instalat vechi, 30 iulie 18:15). Logul pe disc mai conținea și un crash istoric (24 iulie) legat de `Assets\app.ico` (Content vs Resource) — deja rezolvat în sursă.
- **Fix:** definit `InfoToggleOn` în `App.xaml` (bazat pe `GhostButton` + trigger `InfoMode`).
- **Deploy:** publish win-x86 → `publish-v2` (31 iulie ~08:01), copiere elevată în Program Files (hash DLL aliniat).
- **Verificare:** `UPETAcqLab.exe` pornește; `MainWindowHandle` ≠ 0; fără dialog nou de crash.

## Stare livrare overnight

- UI Live / Configure / Review, ribbon, health badge, scurtături — livrate.
- Publish + refresh Program Files — **OK** (31 iulie dimineață, după fix InfoToggleOn).
- Connect USB Spider32 pe HW — încă zonă deschisă (nu blocant pentru UI).

## Cum pornești

`C:\Program Files (x86)\UPET AcqLab\UPETAcqLab.exe`
