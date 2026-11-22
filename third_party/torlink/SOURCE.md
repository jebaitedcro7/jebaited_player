# Torlink runtime: izvor i provjera

Torlink (https://github.com/baairon/torlink, licenca MIT, © 2026 bairon.dev) je terminalska aplikacija (Node.js, webtorrent) koju
`jebaited_player` u GitHub inačici pokreće kao pomoćni proces. **Nije dio instalacijskog paketa playera niti Microsoft Store (MSIX) izdanja.**
Skida se na zahtjev nakon što korisnik uključi Torlink u Postavke → Torlink.

Paket gradi `tools/build-torlink-runtime.ps1` (lokalno i u `.github/workflows/torlink-runtime.yml`):

- Node.js zakucane verzije s nodejs.org, SHA-256 provjeren prema službenom `SHASUMS256.txt`;
- izvorni kod Torlinka na točnoj oznaci, `npm ci --ignore-scripts` iz njihovog `package-lock.json` (nijedna install skripta se ne izvršava);
- `node-datachannel` (WebRTC) je uklonjen, WebRTC je trajno isključen (`TORLINK_NO_WEBRTC=1`, Node ≥ 22.15);
- prebuilds drugih platformi su uklonjeni; ostaju samo `win32-x64`;
- smoke test (`--version`, `serve` + `/health`), `MANIFEST.json` sa SHA-256 svake datoteke, zip + `SHA256SUMS.txt`.

| Stavka | Vrijednost |
|---|---|
| Status | **Lokalna provjerna izgradnja. Izdanje na GitHubu još NIJE objavljeno** (objava traži izričito odobrenje vlasnika) |
| Torlink | v1.11.1 (commit `5e89f6fd067c28f2c091c4beb9a82838e1b34cc5`) |
| Node.js | 22.23.5 win-x64 (`4a191082e9706866830379d577d2cc8a50c776fcd9436a8d061b063ed0d9a0e3`, prema SHASUMS256.txt) |
| Paket | `torlink-runtime-1.11.1-r1-win-x64.zip` (41,5 MB, 5878 datoteka) |
| Oznaka izdanja (planirana) | `torlink-runtime-1.11.1-r1` (pre-release, nije „Latest”) |
| URL (planiran) | https://github.com/jebaitedcro7/jebaited_player/releases/download/torlink-runtime-1.11.1-r1/torlink-runtime-1.11.1-r1-win-x64.zip |
| SHA256 arhive (lokalna izgradnja) | `b9f97b611b62275fe5a78cf1ebc57f435d441f2a2bad60d5b315d2c59ec965de` |
| Potpis | Nije digitalno potpisan; integritet se provjerava preko SHA-256 (zip prema `SHA256SUMS.txt`, svaka datoteka prema `MANIFEST.json`) |
| Licence | Torlink MIT; ovisnosti MIT, Apache-2.0, ISC, BSD, BlueOak i dr. (popis i tekstovi u paketu: `licenses\`); Node.js MIT |

Zip nije ponovljiv bit-za-bit (vrijeme izgradnje je u manifestu), pa se **nakon objave iz GitHub Actions** ovdje upisuje SHA-256 objavljenog zipa.

Nova verzija: pokrenuti radni tok `Torlink runtime` s novom oznakom (nakon pregleda `package-lock.json`, `npm audit` izvještaja i promjena u izdanju),
provjeriti rezultate testova (`tools/run-torlink-tests.ps1`) i tek tada objaviti.
