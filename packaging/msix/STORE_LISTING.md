# Microsoft Store listing (jebaited_player)

Partner Center → jebaited_player → Submission. The package declares the languages **en-us, de-de, hr-hr**, so the Store needs a listing in each of them (text below). Screenshots: `Releases\msix\screenshots\<en|de|hr>\` (1920×1080 PNG, made by `tools/make-store-screenshots.ps1`).

## Common fields (same for all languages)

| Field | Value |
|---|---|
| Pricing | Free, all markets |
| Category | Photo & video |
| Privacy policy URL | https://github.com/jebaitedcro7/jebaited_player/blob/main/PRIVACY.md |
| Website | https://github.com/jebaitedcro7/jebaited_player |
| Support contact info | https://github.com/jebaitedcro7/jebaited_player/issues |
| Copyright and trademark info | © 2026 jebaited_cro |
| Additional license terms | GNU General Public License v3.0: https://github.com/jebaitedcro7/jebaited_player/blob/main/LICENSE |
| Age rating | Fill in the IARC questionnaire. There is no violence, no purchases and no user-to-user communication. The app downloads subtitle files from the internet only when the user searches for them. |

**Restricted capability `runFullTrust`, justification (Submission → Properties / Notes for certification):**
```text
jebaited_player is a desktop (WPF/.NET) video player packaged as MSIX. runFullTrust is required to run the desktop application, to play video with the bundled libmpv library and to open video and subtitle files the user selects from any folder. The app does not install drivers or services and does not update itself (updates come from the Microsoft Store).
```

**Notes for certification:**
```text
No account or sign-in is needed to play video. To test: open any video file (Open file on the main screen, or drag a file into the window). Subtitle search on the internet is optional: it uses the user's own free account or API key for a subtitle source (OpenSubtitles.com, SubDL.com or Titlovi.com), set up via right-click → Subtitles → Set up subtitles from the internet. Without it, local subtitle files and embedded subtitles work normally. IPTV is optional and shows nothing until the user adds their own connection (Xtream Codes or an M3U/M3U8 link or file) on the main screen side bar or in Settings → IPTV; the app contains no channels, playlists or stream addresses, so there is nothing to test without a service of your own.
```

---

## English (en-us)

**Description**
```text
jebaited_player is a lightweight, free video player for Windows that takes care of subtitles for you.

It finds subtitles on the internet (with your free OpenSubtitles.com, SubDL.com or Titlovi.com account), checks that they really belong to the video you are watching and synchronizes them to the speech, so the text appears when the words are spoken, even when the subtitle file was made for a different release.

Everything else stays simple: continue watching where you stopped, open a whole series as a playlist with the next episode ready, keep favorite movies and series on the main screen, switch audio tracks and subtitle styles, and choose a dark or light look. The player opens all common video and audio formats.

If you already have your own IPTV service (Xtream Codes or an M3U/M3U8 playlist), you can add it and watch live channels next to your video. The player brings no channels or playlists of its own.

There are no ads and no account. Settings and watch history stay on your computer.

Free and open source (GPL-3.0): https://github.com/jebaitedcro7/jebaited_player
```

**Product features** (one per line)
```text
Finds subtitles on the internet and checks that they match your video
Synchronizes subtitles to the speech automatically
Continue watching from where you stopped
Series as a playlist, with the next episode ready
Favorite movies and series on the main screen
Audio track, audio delay and volume levelling for quiet dialogue
Subtitle style, encoding and timing settings
Watch your own IPTV service (Xtream or M3U) next to the video; no channels are included
Opens all common video and audio formats
Dark and light mode, interface in several languages
No ads, no account; settings and history stay on your PC
```

**Search terms** (max 7): `video player`, `subtitles`, `subtitle sync`, `mkv player`, `movie player`, `series`, `opensubtitles`

**What's new in this version**
```text
First release in the Microsoft Store.
```

---

## Deutsch (de-de)

**Beschreibung**
```text
jebaited_player ist ein schlanker, kostenloser Videoplayer für Windows, der sich um die Untertitel kümmert.

Er findet Untertitel im Internet (mit Ihrem kostenlosen Konto bei OpenSubtitles.com, SubDL.com oder Titlovi.com), prüft, ob sie wirklich zu Ihrem Video gehören, und synchronisiert sie mit der Sprache – der Text erscheint, wenn die Worte gesprochen werden, auch wenn die Untertiteldatei für eine andere Version erstellt wurde.

Alles andere bleibt einfach: dort weiterschauen, wo Sie aufgehört haben, eine ganze Serie als Wiedergabeliste mit der nächsten Folge öffnen, Lieblingsfilme und -serien auf dem Startbildschirm, Tonspuren und Untertitelstil wechseln, dunkles oder helles Design. Der Player öffnet alle gängigen Video- und Audioformate.

Wenn Sie bereits einen eigenen IPTV-Dienst haben (Xtream Codes oder eine M3U/M3U8-Playlist), können Sie ihn hinzufügen und Live-Kanäle neben dem Video ansehen. Der Player bringt keine eigenen Kanäle oder Playlists mit.

Keine Werbung, kein Konto. Einstellungen und Wiedergabeverlauf bleiben auf Ihrem Computer.

Kostenlos und quelloffen (GPL-3.0): https://github.com/jebaitedcro7/jebaited_player
```

**Produktfunktionen**
```text
Findet Untertitel im Internet und prüft, ob sie zum Video passen
Synchronisiert Untertitel automatisch mit der Sprache
Weiterschauen, wo Sie aufgehört haben
Serien als Wiedergabeliste, die nächste Folge ist bereit
Lieblingsfilme und -serien auf dem Startbildschirm
Tonspur, Audioverzögerung und Lautstärkeausgleich für leise Dialoge
Einstellungen für Untertitelstil, Kodierung und Timing
Eigenen IPTV-Dienst (Xtream oder M3U) neben dem Video ansehen; keine Kanäle enthalten
Öffnet alle gängigen Video- und Audioformate
Dunkles und helles Design, Oberfläche in mehreren Sprachen
Keine Werbung, kein Konto; Einstellungen und Verlauf bleiben auf Ihrem PC
```

**Suchbegriffe**: `Videoplayer`, `Untertitel`, `Untertitel synchronisieren`, `MKV Player`, `Filme`, `Serien`, `OpenSubtitles`

**Neuerungen in dieser Version**
```text
Erste Version im Microsoft Store.
```

---

## Hrvatski (hr-hr)

**Opis**
```text
jebaited_player je lagan, besplatan video player za Windows koji se brine o titlovima umjesto vas.

Pronalazi titlove na internetu (uz vaš besplatni račun na OpenSubtitles.com, SubDL.com ili Titlovi.com), provjerava pripadaju li stvarno videu koji gledate i sinkronizira ih s govorom, pa se tekst pojavi kad se riječi izgovore, čak i kad je titl napravljen za drugo izdanje.

Sve ostalo ostaje jednostavno: nastavak gledanja ondje gdje ste stali, cijela serija kao popis s idućom epizodom spremnom, omiljeni filmovi i serije na početnom zaslonu, promjena audio zapisa i izgleda titlova te tamni ili svijetli izgled. Player otvara sve uobičajene video i audio formate.

Ako već imate vlastitu IPTV uslugu (Xtream Codes ili M3U/M3U8 popis), možete je dodati i gledati kanale uživo uz video. Player ne donosi vlastite kanale ni popise.

Bez reklama i bez računa. Postavke i povijest gledanja ostaju na vašem računalu.

Besplatan i otvorenog koda (GPL-3.0): https://github.com/jebaitedcro7/jebaited_player
```

**Značajke proizvoda**
```text
Pronalazi titlove na internetu i provjerava odgovaraju li videu
Automatski sinkronizira titlove s govorom
Nastavak gledanja ondje gdje ste stali
Serije kao popis, iduća epizoda je spremna
Omiljeni filmovi i serije na početnom zaslonu
Audio zapis, kašnjenje zvuka i izjednačavanje glasnoće za tihe dijaloge
Postavke izgleda, kodiranja i vremena titlova
Gledajte vlastitu IPTV uslugu (Xtream ili M3U) uz video; kanali nisu uključeni
Otvara sve uobičajene video i audio formate
Tamni i svijetli izgled, sučelje na više jezika
Bez reklama i računa; postavke i povijest ostaju na vašem računalu
```

**Pojmovi za pretraživanje**: `video player`, `titlovi`, `sinkronizacija titlova`, `mkv player`, `filmovi`, `serije`, `opensubtitles`

**Novosti u ovoj verziji**
```text
Prvo izdanje u Microsoft Storeu.
```
