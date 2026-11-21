# libdave – izvor i provjera

`libdave.dll` je Discordova biblioteka za DAVE (end-to-end enkripcija glasa), potrebna botu za Discord. Gotovi Discordov DLL uključuje BoringSSL pod starom OpenSSL/SSLeay licencom, koja nije spojiva s GPL-3.0. Zato se libdave gradi iz izvornog koda s OpenSSL 3 (Apache-2.0) u radnom toku `.github/workflows/libdave.yml`.

**Verzija mora odgovarati C API-ju koji zove NetCord** (broj parametara, ne samo imena funkcija). NetCord 1.0.0-beta.30 odgovara libdave **v1.2.2**. libdave 1.3.0 je promijenio `daveSessionProcessProposals` i `daveSessionProcessWelcome` (popis sudionika više nije parametar), pa je DAVE na pravom serveru padao („Welcome message lists unrecognized user ID”). Unit testovi uspoređuju potpise s `include/dave/dave.h` i provjeravaju da DLL nije iz 1.3 API-ja.

| Stavka | Vrijednost |
|---|---|
| Paket | `libdave-1.2.2-openssl-3.6.5-win-x64.zip` |
| Izdanje | jebaitedcro7/jebaited_player, pre-release `libdave-1.2.2-openssl-3.6.5` (objavljeno 21. 11. 2026.; nije „Latest”, Velopack ga ne vidi) |
| Izgrađeno | GitHub Actions, radni tok `libdave.yml`, run 44895686156 (javni commit `bc48fed`), Visual Studio 18.12.0 |
| libdave | `v1.2.2/cpp`, commit `6396c90aedbb9a6ee117ea9ce1ce9832af6a3375` (github.com/discord/libdave, 23. 10. 2026.) |
| Ovisnosti | OpenSSL 3.6.5, mlspp, nlohmann-json; vcpkg 2026.10.16 (commit `9eec002df1cd109f8f104cd966a763daf9751489`), manifest `third_party/libdave/vcpkg.json` |
| Način izgradnje | triplet `x64-windows-static` (statički CRT, bez dodatnih DLL-ova), test `capi_test` prošao |
| URL | https://github.com/jebaitedcro7/jebaited_player/releases/download/libdave-1.2.2-openssl-3.6.5/libdave-1.2.2-openssl-3.6.5-win-x64.zip |
| SHA256 arhive | `1252eab2d1018076ae464fb9757f2a82c15a6304ee550a348b95425c6ead44f6` (podudara se sa SHA256SUMS.txt i GitHub digestom) |
| SHA256 `libdave.dll` | `85170d8daaacefdaa4bab13ce85fa214c72f7438d2539af5717fe8ad57458bc0` |
| Veličina `libdave.dll` | 5 266 944 B (~5 MB) |
| Izvozi | 45 funkcija (bez `daveSessionAddRecognizedUser`, koji je tek u 1.3). NetCord 1.0.0-beta.30 zove 30 i svi postoje s jednakim brojem parametara |
| C header | `third_party/libdave/include/dave/dave.h` (v1.2.2, isti sadržaj kao `include/dave/dave.h` u zipu; zip ima CRLF) |
| Potpis | Nije digitalno potpisan; integritet se provjerava preko SHA256 |
| Licence | libdave MIT, mlspp BSD-2-Clause, nlohmann-json MIT, OpenSSL Apache-2.0 (tekstovi su u zipu, mapa `licenses/`) |
| Preuzeto | 21. 11. 2026. |

`libdave.dll` nije u gitu (vidi `.gitignore`). Preuzima ga `tools/get-libdave.ps1`, koji provjerava SHA256 arhive i DLL-a.

Nova verzija: u `libdave.yml` i `vcpkg.json` promijeniti oznaku libdave-a (i po potrebi vcpkg i OpenSSL), pokrenuti radni tok, ovdje upisati novi paket, URL i oba SHA256, zamijeniti `include/dave/dave.h` headerom iz zipa i pokrenuti unit testove (usporedba potpisa s NetCordom). Prethodni pre-release 1.3.0 je obrisan (C API nije odgovarao NetCordu).
