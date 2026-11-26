# Third-party components

jebaited_player is released under the **GPL-3.0** license (see [LICENSE](LICENSE)). The installation package also contains the following components; each keeps its own license.

| Component | Purpose | License | Source |
|---|---|---|---|
| **mpv / libmpv** (`libmpv-2.dll`) | video and audio playback | GPL-2.0-or-later (this build includes GPL parts) | https://github.com/mpv-player/mpv (commit `bcde6b93cc`) |
| FFmpeg, libass, libplacebo and other libraries built into `libmpv-2.dll` | decoding, subtitles, rendering | GPL / LGPL / ISC (depending on the library) | build scripts: https://github.com/shinchiro/mpv-winbuild-cmake (release `20261111`) |
| .NET runtime and WPF | running the application | MIT | https://github.com/dotnet/runtime, https://github.com/dotnet/wpf |
| Velopack | installation and updates | MIT | https://github.com/velopack/velopack |
| Microsoft.Data.Sqlite | local database (watch history) | MIT | https://github.com/dotnet/efcore |
| SQLitePCLRaw | binding to SQLite | Apache-2.0 | https://github.com/ericsink/SQLitePCL.raw |
| SQLite | database | public domain | https://sqlite.org |
| WebRtcVadSharp | speech detection (subtitle sync) | MIT | https://github.com/ladenedge/WebRtcVadSharp |
| WebRTC VAD | speech detector inside WebRtcVadSharp | BSD-3-Clause | https://webrtc.googlesource.com/src |
| Microsoft Edge WebView2 SDK (`Microsoft.Web.WebView2`) | embedded browser for the official YouTube player (the browser itself is part of Windows) | BSD-3-Clause | https://www.nuget.org/packages/Microsoft.Web.WebView2 |
| NetCord | Discord bot: login, voice channel | MIT | https://github.com/NetCordDev/NetCord |
| Concentus | Opus audio encoding for Discord (C#) | BSD-3-Clause (the Opus license) | https://github.com/lostromb/concentus |
| libdave (`libdave.dll`) | Discord's end-to-end voice encryption (DAVE) | MIT | https://github.com/discord/libdave (tag `v1.2.2/cpp`) |
| OpenSSL 3 (statically linked in `libdave.dll`) | cryptography for DAVE | Apache-2.0 | https://www.openssl.org |
| mlspp (statically linked in `libdave.dll`) | MLS protocol for DAVE | BSD-2-Clause | https://github.com/cisco/mlspp |
| nlohmann/json (statically linked in `libdave.dll`) | JSON inside libdave | MIT | https://github.com/nlohmann/json |
| Torlink (downloaded on request; GitHub version only, not in the installer or the Store release) | search and download over BitTorrent (helper process) | MIT | https://github.com/baairon/torlink |
| Node.js (in the Torlink package) | running Torlink | MIT | https://nodejs.org |
| webtorrent and the other npm packages in the Torlink package (~200) | BitTorrent client and dependencies | MIT, Apache-2.0, ISC, BSD, BlueOak (list and texts in the package: `licenses\NPM-PACKAGES.txt` and `licenses\npm`) | https://github.com/webtorrent/webtorrent |

The full license texts for the .NET runtime and WPF (with the list of third-party components inside .NET), Velopack, Microsoft.Data.Sqlite, SQLitePCLRaw, SQLite, WebRtcVadSharp and WebRTC VAD, Microsoft Edge WebView2 SDK, NetCord, Concentus, libdave, OpenSSL, mlspp and nlohmann/json are installed with the program in the `licenses` folder (and are in the repository under `third_party/licenses`). For libmpv and the libraries inside it, see the section "Source code for libmpv".

## Torlink (GitHub version only)

Torlink is not part of the installation package. When the user turns Torlink on, the player downloads the package `torlink-runtime-<version>-r<N>` from our GitHub repository (Node.js + unmodified Torlink + npm dependencies, without `node-datachannel`), verifies the SHA-256 of the zip and of every file, and runs it as a helper process. The source, pinned versions and SHA-256 values are in `third_party/torlink/SOURCE.md`, and the package is built by `.github/workflows/torlink-runtime.yml`. The licenses of all packages are in the package (the `licenses` folder, available from Settings → Torlink → "Open licenses folder"). Torlink (© 2026 bairon.dev) is under the MIT license, which is compatible with GPL-3.0; source code: https://github.com/baairon/torlink.

## Source code for libdave

`libdave.dll` is built from source (Discord tag `v1.2.2/cpp`) with OpenSSL 3 instead of BoringSSL, on GitHub Actions of this repository (`.github/workflows/libdave.yml`). The exact versions, SHA256 values and the link to the release are in [third_party/libdave/SOURCE.md](third_party/libdave/SOURCE.md).

## Source code for libmpv

`libmpv-2.dll` is unmodified. The exact package, SHA256 and links are in [third_party/libmpv/SOURCE.md](third_party/libmpv/SOURCE.md). The source code of mpv and of all libraries in that build is available at the links above (release `20261111` contains the version list of all built-in libraries). If you need a copy of the source code of exactly that build, open an issue in this repository.

## Subtitles from the internet

The player connects to OpenSubtitles.com and SubDL.com (with your key and account) and, if you turn it on, Titlovi.com (with your account). The subtitles and their terms of use are determined by those sites; jebaited_player is not affiliated with them.
