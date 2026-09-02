# HBM USB IO (usbhbm) — detectare, nu redistribuire

## Ce este

Spider8 pe USB apare în Windows ca:

- **Class:** `HBM_USB_DEVICES`
- **Service / driver:** `USBHBM` → `usbhbm.sys`
- **Exemplu InstanceId:** `USB\VID_10D1&PID_0101\USBHBM2186`
- **Interfață DEST:** `{1B447280-1A9B-11D3-ADCA-444553540000}`
- **Pipe-uri:** `...\PIPE01` (OUT), `...\PIPE00` (IN)

## Cum conectează UPET

1. Backend **HBM USB** (sau **Spider32.dll** + țintă USBHBM…) → `HbmUsbSpider8Adapter`
2. `CreateFile` pe DEST + pipe-uri bulk (nu `S8_InitAll(PORT_USB)` — acel drum e rupt pe USBHBM modern)
3. Comenzi ASCII cu terminator `\n` (dialect catman), ex. `EST?`, `IDN?`, `MSV`/`OMB?`

**Spider32.dll** rămâne pentru COM/RS232. Simulator neschimbat.

## Instalare tipică pe mașina de lab

`C:\Program Files\HBM\HBM USB IO Driver\` — INF/SYS oficial. UPET **nu** redistribuie binarele driverului.

## Verificare rapidă

```powershell
Get-PnpDevice | Where-Object { $_.InstanceId -match 'VID_10D1|USBHBM' }
```

În UPET: **Porturi** → `USBHBM…` → backend **HBM USB** → **Connect** (catman Easy închis).
