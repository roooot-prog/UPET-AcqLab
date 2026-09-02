# Drivere USB-Serial pentru UPET AcqLab / Spider8 (lab)

Acest folder este parcurs recursiv de instalator (`pnputil /add-driver ... /install`).

## Pachete așteptate (toate ON by default)
| Folder | Dispozitiv | Notă |
|---|---|---|
| `ftdi\` | FTDI VCP | Cabluri USB-serial tipice laborator |
| `ch340\` | WCH CH340/CH341 | Cabluri ieftine USB-TTL/COM |
| `cp210x\` | Silicon Labs CP210x | USB-UART lab |
| `pl2303\` | Prolific PL2303 | Compat / redistribuibil dacă e disponibil |
| `vcredist\` | VC++ x86 | Runtime nativ opțional |
| `hbm-usb-io\` | HBM USB IO | **Doar documentație** — nu conține `.sys`/`.inf` HBM |

Rulați `Fetch-Drivers.ps1` pentru a descărca ZIP-uri oficiale redistribuibile.
Driverele proprietare HBM Spider32 / USBHBM **nu** se redistribuie aici — vedeți `hbm-usb-io\README.md` și `vendor\README.md`.

Pe mașina de lab, după instalare: `C:\Program Files\HBM\HBM USB IO Driver\` (vezi `hbm-usb-io\README.md`).
