# Privacy policy / Datenschutz / Privatnost

**jebaited_player** · by jebaited_cro · last updated 2026-11-19

[English](#english) · [Deutsch](#deutsch) · [Hrvatski](#hrvatski)

## English

jebaited_player has **no account, no ads, no telemetry and no tracking**. It does not send your watch history, your files or any analytics anywhere. Everything it remembers (settings, watch history, positions, favourites, logs, caches) stays on your computer in `%LOCALAPPDATA%\jebaited_player`.

The player connects to other systems only in these cases:

| When | Where | What is sent | How to turn it off |
|---|---|---|---|
| Update check: 20 seconds after start, then every 12 hours, or when you click “Check now” | GitHub (`api.github.com`, `github.com` and its download servers) | A normal HTTPS request for the list of releases (your IP address and the program's user agent, as with any website). A new version is downloaded from GitHub. | Settings → Updates → “Download new versions automatically” |
| You open the update history (click the version number) | GitHub API (`api.github.com`) | A request for the list of releases, to show versions newer than yours. Without internet, the history included in the player is shown. | Do not open the window |
| Searching and downloading subtitles (automatically when a video has no subtitles in your first language, or when you search yourself with Ctrl+F) | Only the subtitle sites you set up: OpenSubtitles.com, SubDL.com, Titlovi.com (off by default) | The title, year, season and episode taken from the file name, your subtitle languages, for OpenSubtitles also the file's hash and size (to find subtitles for exactly your release), and the API key, user name and password that **you** entered for that site. | Settings → Subtitles → “Search the internet …”; each site can be disabled in “Subtitle sources (keys)” |
| You search for radio stations (Radio window, Ctrl+R) | Radio Browser directory (`radio-browser.info`) | The station name, genre and country you search for (and your IP address, as with any website). Clicks are not reported. | Do not search; favorites and stream URLs do not use the directory |
| You play a radio station or a stream URL | That station's own server | A normal request for the audio stream (your IP address) | Stop the station |
| You click a link (manual, sign-up pages, GitHub) | Your web browser opens that page | Whatever your browser sends to that website | Do not click the link |

- Keys and passwords for subtitle sites are stored **encrypted** with Windows DPAPI (only your Windows account can read them) and are sent only to the site they belong to.
- Synchronizing subtitles to the speech is done **locally**; the audio never leaves your computer.
- Diagnostic logs (what the player did and any errors, with file names but no full paths, no subtitle text and no keys or passwords) are written only to your computer, are limited in size (about 50 MB) and age (14 days, 30 days for errors), and are never sent anywhere. You can view them under Settings → About → Diagnostics / Logs, export them to a ZIP yourself, or delete the `logs` folder at any time.
- When you click “Set as default player”, the player adds itself to “Open with” for your Windows user (HKCU). “Remove from ‘Open with’” and uninstalling remove these entries again. Nothing else in Windows is changed.
- The privacy policies of the services above apply to the data they receive: [GitHub](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement), [OpenSubtitles](https://www.opensubtitles.com/en/privacy), [SubDL](https://subdl.com), [Titlovi.com](https://titlovi.com).

This policy is shown in the player right after installation (language selection) and under Settings → About → Privacy.

## Deutsch

jebaited_player hat **kein Konto, keine Werbung, keine Telemetrie und kein Tracking**. Einstellungen, Wiedergabeverlauf, Positionen, Favoriten, Protokolle und Zwischenspeicher bleiben auf deinem Computer (`%LOCALAPPDATA%\jebaited_player`).

Der Player verbindet sich nur:

- **mit GitHub** für die Update-Prüfung (20 Sekunden nach dem Start, dann alle 12 Stunden oder per „Jetzt prüfen“) und beim Öffnen des Update-Verlaufs. Abschalten: Einstellungen → Updates → „Neue Versionen automatisch herunterladen“.
- **mit den Untertitelseiten, die du einrichtest** (OpenSubtitles.com, SubDL.com, Titlovi.com): Titel, Jahr, Staffel und Folge aus dem Dateinamen, deine Untertitelsprachen, bei OpenSubtitles auch Hash und Größe der Datei sowie deine Zugangsdaten für die jeweilige Seite. Abschalten: Einstellungen → Untertitel → „Im Internet suchen …“; jede Seite einzeln in „Untertitelquellen (Schlüssel)“.
- **mit dem Radio-Verzeichnis Radio Browser** (radio-browser.info), wenn du Sender suchst: Name, Genre und Land der Suche. Ein Sender, den du abspielst, sieht wie jede Webseite deine IP-Adresse. Klicks werden nicht gemeldet.
- **mit Webseiten, deren Link du anklickst** (dein Browser öffnet sie).

Schlüssel und Passwörter werden mit Windows DPAPI verschlüsselt gespeichert. Die Synchronisierung zur Sprache läuft lokal; der Ton verlässt deinen Computer nicht. Diagnoseprotokolle (Aktionen und Fehler, mit Dateinamen, aber ohne vollständige Pfade, Untertiteltext, Schlüssel oder Passwörter) bleiben nur auf deinem Computer, sind in Größe (ca. 50 MB) und Alter (14 Tage, Fehler 30 Tage) begrenzt und werden nie gesendet; Ansicht und ZIP-Export unter Einstellungen → Info → Diagnose / Protokolle.

## Hrvatski

jebaited_player **nema račun, reklame, telemetriju ni praćenje**. Postavke, povijest gledanja, pozicije, omiljeno, zapisi i predmemorija ostaju na tvom računalu (`%LOCALAPPDATA%\jebaited_player`).

Player se spaja samo:

- **na GitHub** radi provjere nadogradnji (20 sekundi nakon pokretanja, zatim svakih 12 sati ili klikom na „Provjeri sada”) i kad otvoriš povijest nadogradnji. Isključivanje: Postavke → Nadogradnje → „Automatski preuzmi nove verzije”.
- **na stranice s titlovima koje postaviš** (OpenSubtitles.com, SubDL.com, Titlovi.com): naslov, godina, sezona i epizoda iz naziva datoteke, tvoji jezici titlova, za OpenSubtitles i otisak (hash) i veličina datoteke te podaci za prijavu na tu stranicu. Isključivanje: Postavke → Titlovi → „Traži titlove na internetu …”; svaka stranica posebno u „Izvori titlova (ključevi)”.
- **na imenik radija Radio Browser** (radio-browser.info) kad tražiš postaje: naziv, žanr i država koje tražiš. Postaja koju slušaš vidi tvoju IP adresu, kao svaka web stranica. Klikovi se ne prijavljuju.
- **na stranice čiju poveznicu klikneš** (otvara ih tvoj preglednik).

Ključevi i lozinke spremaju se kriptirano (Windows DPAPI). Sinkronizacija prema govoru radi lokalno; zvuk ne napušta tvoje računalo. Dijagnostički logovi (radnje i greške, s nazivima datoteka, ali bez punih putanja, teksta titlova, ključeva i lozinki) ostaju samo na tvom računalu, ograničeni su veličinom (oko 50 MB) i starošću (14 dana, greške 30 dana) i nikad se nikamo ne šalju; pregled i izvoz u ZIP: Postavke → O aplikaciji → Dijagnostika / Logovi.
