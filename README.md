# UPET AcqLab

Aplicație de achiziție și analiză pentru **HBM Spider8**, pentru laboratorul **Universității din Petroșani**.

Conectare, canale, grafice live, tare, trigger, înregistrare CSV, canale math, replay, export Excel/PDF/MAT.

## Instalare (Windows)

Vezi [docs/INSTALL-RO.md](docs/INSTALL-RO.md).

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1
.\dist\UPETAcqLab-Setup.cmd
```

Licență personală UPET AcqLab: format `UPET-ACQLAB-XXXX-XXXX-XXXX-XXXX`  
Exemplu titular / cheie: `dist\LICENSE-DISTRIBUTIE.txt`.

Chei aplicație / câte PC-uri s-au instalat / IP (API + panou **separat**, nu Canale DAQ): [docs/Admin-chei-aplicatie.md](docs/Admin-chei-aplicatie.md). GitHub Releases nu raportează IP-ul fiecărui PC.

## Build

```powershell
cd C:\Users\acer\Desktop\Spider8DAQ
dotnet build Spider8DAQ.sln -c Release
dotnet run --project Spider8DAQ.App -c Release
```

## Publish (self-contained x86)

```powershell
dotnet publish Spider8DAQ.App -c Release -r win-x86 --self-contained true -p:PublishSingleFile=false -o .\publish-v2
```

Rulează `.\publish-v2\UPETAcqLab.exe`.

Actualizări din **GitHub Releases public** (fără token pe PC-urile de lab): [github.com/roooot-prog/UPET-AcqLab](https://github.com/roooot-prog/UPET-AcqLab) — [docs/Actualizari-GitHub.md](docs/Actualizari-GitHub.md).

## Backend-uri

| Backend | Când |
|---|---|
| **Simulator** | Fără hardware |
| **Serial** | USB-serial / RS-232 (implicit 9600 8E1) |
| **Spider32.dll** | DLL HBM în `vendor\` |

## Flux tipic

1. Simulator → Connect → Start
2. Tab Sensors / Apply pe canale
3. Opțional: trigger, durată maximă
4. Record sau macro
5. Analysis: Load CSV → prelucrare → export
6. Save project (`.s8proj`)

## Structură

- `Spider8DAQ.Core` — motor achiziție, proiecte, CSV, math, trigger
- `Spider8DAQ.Hardware` — Simulator, Serial, Spider32.dll
- `Spider8DAQ.App` — UI WPF (UPET AcqLab)
- `UPETAcqLab.LicenseApi` / `UPETAcqLab.Admin` — chei aplicație și activări (panou separat)

## Note

- Target **x86** pentru încărcarea `Spider32.dll` 32-bit.
- Dialectul serial poate varia după firmware; pentru UI folosiți Simulator.
- Licențele comerciale HBM Spider8 / catman sunt separate; această aplicație nu le deblochează.
