# Actualizări GitHub (public) — UPET AcqLab

Pe PC-urile de laborator **nu** este nevoie de token GitHub. Aplicația verifică release-urile **publice** la pornire (după fereastra principală). Dacă există o versiune mai nouă, operatorul vede o frază și un singur buton **Actualizează**. X sau Esc omite actualizarea în sesiunea curentă.

Repo public: [github.com/roooot-prog/UPET-AcqLab](https://github.com/roooot-prog/UPET-AcqLab). Owner/Repo sunt deja în `GitHubPublicRepo.cs`.

## Pentru autori (versiuni noi)

1. Setați `<Version>` în `Spider8DAQ.App.csproj` (FileVersion trebuie să coincidă cu tag-ul).
2. `git tag v3.3.97` și `git push origin v3.3.97` (înlocuiți cu versiunea curentă).
3. Actions (`.github/workflows/release.yml`) publică `UPETAcqLab-update.zip` pe release-ul public.

Studenții nu completează nimic: la următoarea pornire, **Este disponibilă versiunea x.y.z.** → **Actualizează**.

## Ce face Actualizează

1. `GET https://api.github.com/repos/{owner}/{repo}/releases/latest` (fără Bearer; `User-Agent: UPETAcqLab`).
2. Compară `tag_name` / `name` (ex. `v3.3.91`) cu **FileVersion** al exe-ului.
3. Descarcă `UPETAcqLab-update.zip` din `browser_download_url`.
4. Helper-ul `apply-update.cmd` așteaptă închiderea procesului, copiază fișierele **lângă exe** și repornește. **Nu** se scrie în Program Files.

Prima instalare rămâne manuală (`publish-v2` sau setup). Instalările din **Program Files** nu se actualizează automat — folosiți copia portabilă (`publish-v2` sau `%LocalAppData%\UPETAcqLab\app`).

## Limitări

- **SmartScreen / Windows Defender** pot avertiza la exe/zip nesemnat.
- Limita GitHub fără token: **60 cereri / oră / IP** — suficient pentru un laborator.
- Repo-ul trebuie să fie **public** și să aibă un release `latest` cu zip-ul. Un repo privat fără token răspunde 404; verificarea se omite sau eșuează silențios la pornire.
