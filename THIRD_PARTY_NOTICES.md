# Komponente drugih autora / Third-party components

jebaited_player je objavljen pod licencom **GPL-3.0** (vidi [LICENSE](LICENSE)). Instalacijski paket sadrži i sljedeće komponente; svaka zadržava svoju licencu.

| Komponenta | Uloga | Licenca | Izvor |
|---|---|---|---|
| **mpv / libmpv** (`libmpv-2.dll`) | reprodukcija videa i zvuka | GPL-2.0-or-later (ovaj build uključuje GPL dijelove) | https://github.com/mpv-player/mpv (commit `bcde6b93cc`) |
| FFmpeg, libass, libplacebo i ostale biblioteke ugrađene u `libmpv-2.dll` | dekodiranje, titlovi, iscrtavanje | GPL / LGPL / ISC (prema pojedinoj biblioteci) | build skripte: https://github.com/shinchiro/mpv-winbuild-cmake (izdanje `20261111`) |
| .NET runtime i WPF | izvođenje aplikacije | MIT | https://github.com/dotnet/runtime, https://github.com/dotnet/wpf |
| Velopack | instalacija i nadogradnje | MIT | https://github.com/velopack/velopack |
| Microsoft.Data.Sqlite | lokalna baza (povijest gledanja) | MIT | https://github.com/dotnet/efcore |
| SQLitePCLRaw | veza prema SQLiteu | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw |
| SQLite | baza podataka | javno dobro (public domain) | https://sqlite.org |
| WebRtcVadSharp | detekcija govora (sinkronizacija titlova) | MIT | https://github.com/ladenedge/WebRtcVadSharp |
| WebRTC VAD | detektor govora unutar WebRtcVadSharp | BSD-3-Clause | https://webrtc.googlesource.com/src |
| Microsoft Edge WebView2 SDK (`Microsoft.Web.WebView2`) | ugrađeni preglednik za službeni YouTube player (sam preglednik je dio Windowsa) | BSD-3-Clause | https://www.nuget.org/packages/Microsoft.Web.WebView2 |
| NetCord | bot za Discord: prijava, glasovni kanal | MIT | https://github.com/NetCordDev/NetCord |
| Concentus | Opus kodiranje zvuka za Discord (C#) | BSD-3-Clause (licenca Opusa) | https://github.com/lostromb/concentus |
| libdave (`libdave.dll`) | Discordova end-to-end enkripcija glasa (DAVE) | MIT | https://github.com/discord/libdave (oznaka `v1.2.2/cpp`) |
| OpenSSL 3 (statički u `libdave.dll`) | kriptografija za DAVE | Apache-2.0 | https://www.openssl.org |
| mlspp (statički u `libdave.dll`) | MLS protokol za DAVE | BSD-2-Clause | https://github.com/cisco/mlspp |
| nlohmann/json (statički u `libdave.dll`) | JSON unutar libdave | MIT | https://github.com/nlohmann/json |

Puni tekstovi licenci za NetCord, Concentus, libdave, OpenSSL, mlspp i nlohmann/json nalaze se uz program u mapi `licenses` (i u repozitoriju u `third_party/licenses`).

## Izvorni kod za libdave

`libdave.dll` je izgrađen iz izvornog koda (Discordova oznaka `v1.2.2/cpp`) s OpenSSL 3 umjesto BoringSSL-a, na GitHub Actions ovog repozitorija (`.github/workflows/libdave.yml`). Točne verzije, SHA256 i poveznica na izdanje su u [third_party/libdave/SOURCE.md](third_party/libdave/SOURCE.md).

## Izvorni kod za libmpv

`libmpv-2.dll` nije mijenjan. Točan paket, SHA256 i poveznice su u [third_party/libmpv/SOURCE.md](third_party/libmpv/SOURCE.md). Izvorni kod mpv-a i svih biblioteka u tom buildu dostupan je na gornjim poveznicama (izdanje `20261111` sadrži popis verzija svih ugrađenih biblioteka). Ako trebaš kopiju izvornog koda točno tog builda, otvori issue u ovom repozitoriju.

## Titlovi s interneta

Player se spaja na OpenSubtitles.com i SubDL.com (s tvojim ključem i računom) i, ako ga uključiš, Titlovi.com (s tvojim računom). Titlove i njihove uvjete korištenja određuju te stranice; jebaited_player nije s njima povezan.
