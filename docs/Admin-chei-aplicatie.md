# Chei aplicație + Admin (separat de Canale DAQ)

Panoul de administrare **nu** este în UI-ul UPET AcqLab. GitHub Releases nu poate raporta IP-ul fiecărui PC: trebuie un API găzduit care vede adresa la heartbeat.

## Componente

| Proiect | Rol |
|---|---|
| `UPETAcqLab.LicenseApi` | ASP.NET Minimal API + SQLite (hash chei + activări) |
| `UPETAcqLab.Admin` | WPF separat: generează chei, listă instalări/IP |
| AcqLab (`license-server.json`) | Client subțire: dialog **Cheie aplicație**, heartbeat |

## Rulare locală (acest PC)

1. Setați parola admin **doar local** (nu pe GitHub, nu în chat):

   `%LocalAppData%\UPETAcqLab.LicenseApi\settings.json`

   Completați `"AdminPassword"` (fișierul se creează la prima pornire a API-ului, cu parolă goală).

2. API (implicit `http://127.0.0.1:5088`):

```powershell
cd C:\Users\acer\Desktop\Spider8DAQ
dotnet run --project UPETAcqLab.LicenseApi
```

Sau: `.\publish-admin\api\UPETAcqLab.LicenseApi.exe`

3. Admin — **Generează cheie** după **Conectează**:

```powershell
dotnet run --project UPETAcqLab.Admin
```

Sau: `.\publish-admin\admin\UPETAcqLab.Admin.exe`

Cheia se afișează **o dată**. În baza de date rămâne doar SHA256 + ultimele 4 caractere.

4. AcqLab pe același PC: în `license-server.json` (lângă `UPETAcqLab.exe`) puneți:

```json
{
  "LicenseServerUrl": "http://127.0.0.1:5088"
}
```

Dacă `LicenseServerUrl` e **gol**, poarta de cheie aplicație se **omite** (PC-ul autorului rămâne utilizabil). Licența HMAC personală existentă nu se schimbă.

## Ce vede operatorul AcqLab

- Fără cheie salvată: dialog **Cheie aplicație** + **Continuă**.
- Cheie invalidă: nu pornește DAQ.
- După o activare reușită: dacă serverul cade, laboratorul **poate măsura** (cache local). Prima activare **necesită** serverul.

Heartbeat: la pornire și ~la fiecare oră. Se trimit hash cheie, hash id-mașină, hostname. **IP-ul** e `HttpContext.Connection.RemoteIpAddress` pe server (nu un IP trimis de client).

## Limitare importantă (IP-uri remote)

Pe `localhost` veți vedea `127.0.0.1`. Pentru PC-urile de laborator din rețea/internet trebuie găzduit API-ul pe un **URL HTTPS public** (IIS, cloud). Apoi:

- `ListenUrl` pe server (ex. `http://0.0.0.0:5088` în spatele IIS HTTPS)
- `LicenseServerUrl` pe fiecare PC de lab = acel URL HTTPS
- În spatele unui reverse proxy, activați `X-Forwarded-For` (API-ul deja citește forwarded headers)

GitHub Releases **nu** înlocuiește acest API.

## GDPR

Adresa IP este înregistrată exclusiv pentru auditul licențelor pe PC-urile de laborator.

## Publicare

```powershell
dotnet publish UPETAcqLab.LicenseApi -c Release -r win-x64 --self-contained true -o .\publish-admin\api
dotnet publish UPETAcqLab.Admin -c Release -r win-x64 --self-contained true -o .\publish-admin\admin
```
