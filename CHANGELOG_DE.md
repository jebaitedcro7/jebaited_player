# Änderungsprotokoll

Alle wichtigen Änderungen an jebaited_player. Format: [Keep a Changelog](https://keepachangelog.com/de/1.1.0/); Versionsnummern: Die erste Zahl ändert sich nur bei großen Meilensteinen (zum Beispiel neue Plattformen), die zweite wächst mit jedem Upgrade-Zyklus mit neuen Funktionen, und die letzte zählt Korrekturen und wird nie zurückgesetzt (seit 1.6.3; frühere Versionen folgten [Semantic Versioning](https://semver.org/lang/de/)).

Andere Sprachen: [English](CHANGELOG.md) · [Hrvatski](CHANGELOG_HR.md). Derselbe Verlauf ist im Player enthalten (Klick auf die Versionsnummer auf dem Startbildschirm). Diese Datei wird aus `src/JebaitedPlayer.App/History/update-history.json` erzeugt.

## [2.4.17] - 2026-11-25

Bessere automatische Untertitel-Synchronisierung bei lauten Szenen, ein neuer IPTV-Tab in den Einstellungen und fünf weitere Sprachen.

### Neu

- Fünf weitere Sprachen der Oberfläche: Spanisch, Französisch, Italienisch, Portugiesisch und Russisch (insgesamt 11). Wähle sie unter Einstellungen → Sprache bei „Weitere Sprachen“.
- Einstellungen → IPTV: alle IPTV-Einstellungen in einem Tab (ein/aus, Verbindungen und die Standardverbindung, Standardsortierung, kompakte Zeilen, letzten Kanal fortsetzen, Schriftart, Textgröße und Farben). Änderungen gelten sofort auch für ein geöffnetes IPTV-Panel.

### Verbessert

- Automatische Untertitel-Synchronisierung: Ist das Ergebnis nicht eindeutig (Untertitel für eine andere Fassung), prüft der Player die Sprache zusätzlich mit einem strengeren Sprachdetektor und behält das bessere Ergebnis. In Szenen mit Musik und Lärm liegt die Stelle, an der sich der Versatz ändert, so deutlich näher an der tatsächlichen.
- Die Liste der Komponenten anderer Autoren (THIRD_PARTY_NOTICES) ist jetzt auf Englisch.

### Behoben

- Die Lautstärkeanzeige im Video verdeckt den Home-Knopf nicht mehr: Sie erscheint jetzt oben rechts.
- Untertitel für eine andere Fassung konnten in lauten Szenen bis zu etwa drei Minuten zu spät auf den nächsten Versatz springen (zum Beispiel 6 Sekunden zu früh um Minute 27).

### Bekannte Probleme

- Wo eine Szene keine klare Sprache hat, kann der Versatz genau an der Stelle der Änderung noch ein bis zwei Sekunden danebenliegen. Die neuen Übersetzungen wurden ohne Prüfung durch Muttersprachler geschrieben; Korrekturen sind willkommen.

Release: [v2.4.17](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.4.17) · Änderungen seit [v2.4.16...v2.4.17](https://github.com/jebaitedcro7/jebaited_player/compare/v2.4.16...v2.4.17)

## [2.4.16] - 2026-11-24

IPTV: Schau deinen eigenen IPTV-Dienst (Xtream oder M3U) im Player, dazu eine neue Seitenleiste auf dem Startbildschirm.

### Neu

- IPTV: Füge deine eigene Verbindung hinzu (Xtream Codes, M3U/M3U8-Link oder -Datei) und sieh Live-Kanäle neben dem Video. Kategorien mit Flaggen und Symbolen, Suche, Favoriten, zuletzt gesehen, Kanäle und Kategorien ausblenden (Rechtsklick), Sortierung A–Z, Z–A, Original oder eigene Reihenfolge, Programmführer (Jetzt/Danach, Zeitleiste pro Tag), Vollbild mit F oder Doppelklick. Schriftart, Textgröße und Farben des IPTV-Panels sind einstellbar, die Ränder lassen sich auf die gewünschte Breite ziehen. Das Passwort wird verschlüsselt auf deinem Computer gespeichert, nie in Protokollen oder Exporten. Nutze nur Dienste, die du nutzen darfst; der Player bringt keine Kanäle oder Playlists mit.
- Seitenleiste auf dem Startbildschirm: Datei öffnen, Ordner öffnen, Untertitelsuche, IPTV und Radio sind jetzt an einer Stelle; sie wird unter der Maus sanft breiter und zeigt die Namen. Die Player-Karte auf dem Startbildschirm hat eine Stopp-Taste für Live-Streams.

### Verbessert

- Live-Streams (TV, Radio) zeigen eine volle Fortschrittsleiste ohne Dauer, und ein Klick auf das Bild pausiert einen Live-TV-Stream nicht mehr.
- Zugangsdaten in IPTV-Adressen werden in Protokollen und Fehlerberichten verborgen.

### Bekannte Probleme

- IPTV-Server, die nur einfaches HTTP anbieten, sind nicht verschlüsselt; der Player warnt davor. Catch-up-Wiedergabe, eigene Kanalgruppen und Bild-in-Bild gibt es noch nicht.

Release: [v2.4.16](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.4.16) · Änderungen seit [v2.3.15...v2.4.16](https://github.com/jebaitedcro7/jebaited_player/compare/v2.3.15...v2.4.16)

## [2.3.15] - 2026-11-22

Benachrichtigungen auf dem Startbildschirm, Auswahl des Mini-Player-Aussehens in den Einstellungen und Korrekturen.

### Neu

- Benachrichtigungen: Eine kleine Karte auf dem Startbildschirm zeigt Neuigkeiten, Tipps und neue Versionen. Sie sitzt über der Wiedergabeleiste, wenn etwas läuft, und tief, direkt über der Versionsnummer, wenn nichts läuft; ein dünner Pfeil zeigt auf die Versionsnummer, und die Karte bewegt sich sanft, wenn die Wiedergabeleiste erscheint oder verschwindet. Die neue Taste Benachrichtigungen (unten links, mit der Zahl der ungelesenen) und Einstellungen → Benachrichtigungen listen alles auf, auch gelesene und verworfene. Eine Karte wird höchstens dreimal angezeigt; der Status bleibt auf deinem Computer, und es wird nichts Neues ins Internet gesendet.
- Einstellungen → Aussehen → Mini-Player: alle fünf Looks (klassisch, Karte, schmale Leiste, Seitenleiste, UFO-Display) mit Vorschau. Die Änderung gilt sofort und wird gespeichert.

### Verbessert

- Die Wiedergabezeit ist kleiner und zentriert, mit stabiler Breite, im vollen Player und in jedem Mini-Player-Look.
- Mini-Player: Die Lautstärkeleiste folgt dem Mausrad sofort (sie hinkte dem Ton kurz hinterher).

### Behoben

- Text, der in Eingabefeldern getippt wurde (zum Beispiel in der Radiosuche), war unten abgeschnitten.

Release: [v2.3.15](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.3.15) · Änderungen seit [v2.2.14...v2.3.15](https://github.com/jebaitedcro7/jebaited_player/compare/v2.2.14...v2.3.15)

## [2.2.14] - 2026-11-22

Torlink: online suchen und herunterladen direkt im Player (GitHub-Version, optional, standardmäßig aus).

### Neu

- Torlink (Einstellungen → Torlink, nur GitHub-Version): Auf dem Startbildschirm erscheint neben „Datei öffnen“ und „Ordner öffnen“ ein dritter Knopf, Online suchen, der eine eigene Seite öffnet. Eine Suche über mehrere öffentliche Indizes (Filme, Serien, Anime, Spiele) mit Größe, Seedern und Quelle, Sortierung und Filtern sowie eine Download-Verwaltung (aktiv, Warteschlange, abgeschlossen, fehlgeschlagen; pausieren, fortsetzen, abbrechen, Ordner öffnen, abspielen). Start (Esc, Alt+←) führt zum Player zurück und die Downloads laufen weiter; Strg+Umschalt+T öffnet und schließt die Seite.
- Torlink ist das Open-Source-Projekt baairon/torlink (MIT), unverändert verwendet und nur gestartet, solange es gebraucht wird. Es ist standardmäßig aus, fragt zuerst nach deiner Zustimmung und wird als geprüftes Paket geladen (SHA-256 des Pakets und jeder Datei). Updates nur auf deinen Klick, mit automatischem Zurücksetzen. Das Teilen nach einem Download ist standardmäßig aus.
- Einstellungen → Torlink: ein- oder ausschalten (aus bedeutet kein Prozess und keine Netzwerknutzung), Versionen und Update-Prüfung, Download-Ordner, gleichzeitige Downloads, playlist.m3u, zusätzliche Tracker, Netzwerkschnittstelle (VPN), Teilen.

### Verbessert

- Torlink ist nicht Teil der Microsoft-Store-Version.

### Bekannte Probleme

- BitTorrent zeigt anderen Nutzern deine IP-Adresse. Ohne gewählte Netzwerkschnittstelle nutzt Torlink auch die LAN-Erkennung und bittet den Router um einen offenen Port (UPnP/NAT-PMP); Peer-Verbindungen sind nicht verschlüsselt. Siehe PRIVACY.md.

Release: [v2.2.14](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.2.14) · Änderungen seit [v2.1.13...v2.2.14](https://github.com/jebaitedcro7/jebaited_player/compare/v2.1.13...v2.2.14)

## [2.1.13] - 2026-11-22

Albumcover im Mini-Player und auf dem Startbildschirm, eine eigene Lautstärke für Discord und ein Player, der immer ein Fenster zeigt.

### Neu

- Albumcover: Die Designs „Karte“ und „Seitenleiste“ des Mini-Players und der Startbildschirm zeigen das Cover des Titels (eingebettet in MP3-, FLAC-, M4A- oder Ogg/Opus-Dateien oder ein cover/folder-Bild neben dem Titel). Titel ohne Cover, Radio, Spotify und YouTube behalten das UFO-Logo.
- Discord-Bot: Die Lautstärke auf Discord kann dem Player folgen (wie bisher) oder einen eigenen Wert haben. Wenn du den Player für dich leiser stellst, wird es im Kanal nicht mehr für alle leiser (Discord-Fenster).
- Startschutz: Startet der Player einmal ohne sichtbares Fenster, wird das Hauptfenster automatisch zurückgeholt, und der nächste Start öffnet es normal in der Bildschirmmitte (mit einem kurzen Hinweis).

### Verbessert

- Wird der Player erneut gestartet, während der Mini-Player offen ist, kommt der Mini-Player in den Vordergrund.

### Behoben

- Titel mit eingebettetem Cover (oder einer cover.jpg daneben) wurden wie ein Video behandelt: Der Startbildschirm schloss sie, der Discord-Bot sendete Stille und die Untertitelsuche konnte für sie starten. Ein Cover ist jetzt nur ein Cover, solche Titel verhalten sich wie jede andere Musik.
- Ein erneuter Start, während der laufende Player nicht reagierte, bewirkte nichts; der neue Start öffnet jetzt einen eigenen Player.
- Die vollständigen Lizenztexte von .NET und WPF, Velopack, Microsoft.Data.Sqlite, SQLitePCLRaw, SQLite, WebRtcVadSharp/WebRTC und dem WebView2 SDK liegen jetzt dem Programm bei (Ordner licenses), nicht nur als Link.
- Helles Design: Das grüne Akzentgrün ist etwas dunkler, weißer Text darauf ist besser lesbar (Kontrast 4,6:1, vorher 4,4:1).

### Bekannte Probleme

- Ein stummgeschalteter Player (oder Lautstärke 0) sendet auch mit eigener Discord-Lautstärke Stille: Der Bot kann nur senden, was der Player abspielt.

Release: [v2.1.13](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.1.13) · Änderungen seit [v2.1.9...v2.1.13](https://github.com/jebaitedcro7/jebaited_player/compare/v2.1.9...v2.1.13)

## [2.1.9] - 2026-11-22

Lautstärke mit dem Mausrad überall im Player, ein Fortschrittsbalken für Updates, Wetter-Symbole und eine klarere Einstellung für die nächste Folge.

### Neu

- Update-Download: Der Player zeigt, welche Version geladen wird, wie groß sie ist und einen Fortschrittsbalken mit Prozent und sanfter Animation (Einstellungen → Updates) sowie einen Hinweis beim Start des Downloads.
- Symbole vor jeder Wetterangabe auf dem Startbildschirm (gefühlte Temperatur, Min/Max, Regen, Wind, Luftfeuchtigkeit und mehr).

### Verbessert

- Das Mausrad ändert die Lautstärke überall im Player (Video, Steuerung, Startbildschirm, ganzer Mini-Player). Listen, die scrollen können, scrollen weiterhin mit dem Rad. Strg + Rad ändert weiterhin die Größe des Mini-Players.
- Der Text „Wetterdaten von Open-Meteo.com“ wird auf dem Startbildschirm nicht mehr angezeigt; die Quelle steht weiterhin in den Einstellungen.

### Behoben

- Einstellungen → Allgemein: Der Wert neben „Nächste Folge vor dem Ende anbieten“ wurde aus dem sichtbaren Bereich geschoben; der Regler hat jetzt eine eigene Zeile und zeigt die Sekunden.

Release: [v2.1.9](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.1.9) · Änderungen seit [v2.1.8...v2.1.9](https://github.com/jebaitedcro7/jebaited_player/compare/v2.1.8...v2.1.9)

## [2.1.8] - 2026-11-21

Korrektur: Der Player startet wieder, wenn das Hauptfenster maximiert war und der Mini-Player die letzte Ansicht war; das Mausrad ändert die Lautstärke schneller und flüssiger.

### Verbessert

- Das Mausrad über der Lautstärkeleiste ändert die Lautstärke um 5 % pro Raststufe (vorher 2 %), proportional zur Drehung, sodass feine Mausräder und Touchpads flüssig arbeiten. Der Regler im Mini-Player folgt sofort.

### Behoben

- Der Player startet nicht mehr ohne Fenster, wenn das Hauptfenster maximiert war und die letzte Ansicht der Mini-Player war.

Release: [v2.1.8](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.1.8) · Änderungen seit [v2.1.7...v2.1.8](https://github.com/jebaitedcro7/jebaited_player/compare/v2.1.7...v2.1.8)

## [2.1.7] - 2026-11-21

Mini-Player und Startbildschirm: fünf Mini-Player-Looks, Lautstärke per Mausrad, pulsierende LIVE-Anzeige fürs Radio, Musik läuft auf dem Startbildschirm weiter, Uhrzeit, Datum und Wetter frei anpassbar.

### Neu

- Fünf Mini-Player-Looks (Rechtsklick → Aussehen): klassisches Winamp, Karte, schmale Leiste, Seitenleiste und UFO-Display. Alle haben dieselben Funktionen, deine Wahl wird gespeichert.
- Das Mausrad über der Lautstärkeleiste ändert die Lautstärke in 2-%-Schritten, im normalen und im Mini-Player. Das Mausrad über dem Video oder anderen Bereichen ändert die Lautstärke nicht mehr versehentlich.
- Eine rote, sanft pulsierende LIVE-Anzeige, solange ein Radiosender läuft: in jedem Mini-Player-Look, im normalen Player und auf dem Startbildschirm.
- Der Titel läuft in einer Endlosschleife (ohne Lücke und ohne Sprung) und zeigt standardmäßig nur Interpret und Titel. Rechtsklick auf den Titel: Album, Jahr, Genre, Klangqualität oder Dateiname hinzufügen.
- Wiedergabeleiste auf dem Startbildschirm: aktueller Titel, Quelle (lokal, Radio, Spotify, YouTube Music, Discord), Play/Pause, Zurück/Weiter und Lautstärke.
- Aussehen des Startbildschirms (Einstellungen → Aussehen): Schriftart, Größe und Position von Uhrzeit, Datum und Wetter jeweils einzeln, mit Vorschau und „Aussehen zurücksetzen“.
- Mehr Wetterdetails (Open-Meteo, kostenlos, ohne Schlüssel): gefühlte Temperatur, Minimum und Maximum, Regenwahrscheinlichkeit und -menge, Wind, Luftfeuchtigkeit, UV-Index, Sonnenauf- und -untergang, nächste Stunden und Tage. Angezeigt wird nur, was der Wetterdienst liefert.
- Eigene Minimieren-Schaltfläche (und Menüeintrag): Der Player geht in die Taskleiste und die Musik läuft weiter; getrennt von Mini-Player und Schließen.
- Der Discord-Bot steht im Quellenmenü neben Radio, Spotify und YouTube Music.
- Nächste Folge vor dem Ende (Einstellungen → Allgemein): Du wählst, wie viele Sekunden vor dem Ende einer Folge die Karte für die nächste erscheint; der Countdown endet dann genau am Ende. 0 behält das bisherige Verhalten (erst nach dem Ende).

### Verbessert

- Der Mini-Player und alle anderen Fenster (Equalizer-Bereich, Radio, Discord …) bleiben immer vollständig im sichtbaren Bildschirmbereich: Taskleiste, Skalierung und mehrere Monitore werden berücksichtigt, und ein nach außen geschobenes Fenster wird zurückgeholt.
- Die schmale Leiste hat jetzt einen Lautstärkeregler.

### Behoben

- Die Taste Home stoppt Musik und Radio nicht mehr. Nur Videos werden weiterhin geschlossen, wie bisher.
- Beim Start öffnet sich nur ein Fenster: der Mini-Player, wenn er die letzte Ansicht war (oder bei einer Musikdatei), sonst der normale Player. Vorher erschien zuerst der große Player und kurz danach der Mini-Player.

### Bekannte Probleme

- Der Ort wird nur über den Städtenamen gewählt; eine automatische Ortung ist nicht enthalten (vor der Städtewahl wird nichts gesendet).
- Die App ist noch nicht digital signiert, daher können Windows SmartScreen oder Smart App Control vor der Installation warnen.
- Das Fortschrittsfenster des Installers stammt von Velopack und ist nur auf Englisch.

Release: [v2.1.7](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.1.7) · Änderungen seit [v2.0.6...v2.1.7](https://github.com/jebaitedcro7/jebaited_player/compare/v2.0.6...v2.1.7)

## [2.0.6] - 2026-11-21

2.0: Dein eigener Discord-Bot spielt Musik und Radio des Players in einem Sprachkanal; jedes Tastenkürzel lässt sich ändern; Akzentfarbe nach Wahl.

### Neu

- Discord-Bot (Strg+Umschalt+D, die Kopfhörer-Taste im Mini-Player oder Rechtsklick → Discord-Bot …): Dein eigener Bot spielt Musik und Radio des Players in einem Sprachkanal auf deinem Server, damit Freunde hören, was du hörst. Erstelle einen kostenlosen Bot im Discord Developer Portal (die Schritte stehen im Fenster), füge sein Token ein und lade ihn ein; „Meinem Kanal beitreten“ findet den Kanal, in dem du bist. Das Token wird verschlüsselt nur auf diesem Computer gespeichert. Gesendet werden nur lokale Musik und Radio: Filme, YouTube und Spotify werden nie gesendet (der Bot sendet Stille). Die Sprachverbindung ist Ende-zu-Ende-verschlüsselt (Discord DAVE).
- Eigene Tastenkürzel (Einstellungen → Tastenkürzel): Jede Aktion kann zwei Tasten haben. Auf eine Taste klicken und die neue Kombination drücken; nutzt sie schon eine andere Aktion, meldet der Player das, und ein zweiter Druck verschiebt sie. „Standard“ stellt eine Aktion oder den ganzen Tab wieder her. F1 und die Menüs zeigen immer deine tatsächlichen Tasten, und deine Tasten sind im Export der Einstellungen und in den Sicherungen enthalten.
- Akzentfarbe (Einstellungen → Darstellung → Akzentfarbe): acht fertige Farben oder eine eigene (Farbton, Sättigung und Helligkeit oder #RRGGBB) mit Live-Vorschau. Schaltflächen, Regler, markierte Einträge sowie Anzeige und Pegelanzeige des Mini-Players folgen ihr; in jedem Design wird die Farbe bei Bedarf angepasst, damit Text lesbar bleibt.

### Verbessert

- Der längere Sprung (Strg+←/→) und die feine und grobe Untertitelverschiebung (Strg+G/H, Umschalt+G/H) sind jetzt eigene Aktionen, daher lassen sich auch ihre Tasten ändern.
- Die vollständigen Lizenztexte der Komponenten des Discord-Bots (NetCord, Concentus, libdave, OpenSSL, mlspp, nlohmann/json) werden zusammen mit THIRD_PARTY_NOTICES im Ordner licenses mit dem Programm installiert.

### Behoben

- Der kurze Datenschutztext in den Einstellungen war veraltet (er nannte nur GitHub, Untertitel und Radio); jetzt nennt er jeden Dienst, mit dem sich der Player verbinden kann.
- In der F1-Liste der Tastenkürzel fehlten Strg+M, Strg+R, Strg+Umschalt+S und Strg+Umschalt+Y; sie entsteht jetzt aus derselben Liste, die der Player verwendet, und ist daher immer vollständig.

### Bekannte Probleme

- Der Discord-Bot braucht zum Senden von Ton Windows 11 (oder Windows 10 Build 20348 oder neuer).
- Die App ist noch nicht digital signiert, daher können Windows SmartScreen oder Smart App Control vor der Installation warnen.
- Das Fortschrittsfenster des Installationsprogramms stammt von Velopack und ist nur auf Englisch.

Release: [v2.0.6](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.0.6) · Änderungen seit [v1.8.4...v2.0.6](https://github.com/jebaitedcro7/jebaited_player/compare/v1.8.4...v2.0.6)

## [1.8.4] - 2026-11-21

Zyklus 6: YouTube Music, Uhrzeit, Datum und Wetter auf dem Startbildschirm und Fehlerberichte.

### Neu

- YouTube Music (Strg+Umschalt+Y oder die Quellenanzeige im Mini-Player): Füge einen Link von YouTube oder YouTube Music ein (Song oder Playlist); er läuft im offiziellen YouTube-Player in einem eigenen Fenster, ohne Konto und ohne Schlüssel. Die Suche braucht deinen eigenen kostenlosen YouTube-API-Schlüssel (die Schritte stehen im Fenster; „API-Schlüssel …“ ändert oder entfernt ihn). Solange YouTube die Quelle ist, zeigt der Mini-Player YOUTUBE und seine Tasten steuern YouTube. Der YouTube-Player muss sichtbar bleiben (YouTube-Regeln), daher gibt es keine Wiedergabe im Hintergrund.
- Informationen auf dem Startbildschirm: Uhrzeit und Datum oben links und auf Wunsch das Wetter für eine Stadt deiner Wahl (Einstellungen → Darstellung; von Open-Meteo, kostenlos und ohne Konto, höchstens alle 30 Minuten, °C oder °F). Ohne Internet wird das zuletzt bekannte Wetter mit der Uhrzeit der Aktualisierung angezeigt.
- Fehlerberichte: Fehler und Abstürze werden als kurze bereinigte Berichte auf deinem Computer gespeichert (keine Ordner, Videonamen, Namen, Schlüssel oder IP-Adresse). Einstellungen → Info → „Über GitHub senden …“ öffnet ein vorbereitetes Issue, das du vor dem Senden lesen kannst.

### Verbessert

- Nur ein Ton gleichzeitig: Startet YouTube, pausiert die lokale Datei (ein Radiosender stoppt) und Spotify ist nicht mehr die Quelle; eine Datei oder ein Sender pausiert YouTube.

### Bekannte Probleme

- Die App ist noch nicht digital signiert, daher können Windows SmartScreen oder Smart App Control vor der Installation warnen.
- Das Fortschrittsfenster des Installationsprogramms stammt von Velopack und ist nur auf Englisch.

Release: [v1.8.4](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.8.4) · Änderungen seit [v1.7.4...v1.8.4](https://github.com/jebaitedcro7/jebaited_player/compare/v1.7.4...v1.8.4)

## [1.7.4] - 2026-11-21

Zyklus 5: Spotify-Fernbedienung, „Als Nächstes“ auf dem Startbildschirm und eigener Hintergrund.

### Neu

- Spotify (Strg+Umschalt+S oder die Quellenanzeige im Mini-Player): Anmeldung mit deiner eigenen kostenlosen Spotify-Entwickler-App (Anleitung im Fenster), Song mit Cover, Wiedergabe, Pause, Weiter, Spulen, Lautstärke, deine Playlists und gespeicherten Songs, Suche und Geräteauswahl. Die Musik läuft in deiner Spotify-App; solange Spotify die Quelle ist, steuern die Tasten des Mini-Players Spotify. Zum Steuern braucht man Spotify Premium.
- „Als Nächstes“ auf dem Startbildschirm: Die nächste Folge einer Serie, die du schaust, erscheint von selbst (auch über Staffeln hinweg). Du kannst jedes Video hinzufügen (Rechtsklick auf das Video oder einen Eintrag in „Weiterschauen“), die Reihenfolge mit ▲ ▼ ändern und Einträge mit × entfernen. Ein Eintrag verschwindet, sobald er angesehen ist.
- Einstellungen → Darstellung → Hintergrund des Startbildschirms: Standard (Weltraum) oder ein eigenes Bild (Füllen, Einpassen oder Zentriert) mit Vorschau. Das Bild wird nicht kopiert; wird es verschoben oder gelöscht, kommt der Weltraum-Hintergrund zurück.

### Verbessert

- Ein großes Hintergrundbild wird im Hintergrund und nur in Bildschirmgröße geladen, daher verlangsamt es den Start nicht.

### Bekannte Probleme

- Die App ist noch nicht digital signiert, daher können Windows SmartScreen oder Smart App Control vor der Installation warnen.
- Das Fortschrittsfenster des Installationsprogramms stammt von Velopack und ist nur auf Englisch.

Release: [v1.7.4](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.7.4) · Änderungen seit [v1.6.3...v1.7.4](https://github.com/jebaitedcro7/jebaited_player/compare/v1.6.3...v1.7.4)

## [1.6.3] - 2026-11-20

Zyklus 4: Radio nach Stopp wieder live, geschützte Einstellungen, Rückfrage bei ungespeicherten Änderungen und eigene Ausgabe für Musik.

### Neu

- Einstellungen → Audio → „Ausgabe für Musik und Radio“: Musik, Radio und der Mini-Player können über eine andere Ausgabe laufen als Filme (zum Beispiel ein virtuelles Kabel für Discord). Standardmäßig ist es dieselbe Ausgabe wie für Filme.
- Beim Schließen der Einstellungen mit ungespeicherten Änderungen kommt jetzt die Frage: Speichern, Änderungen verwerfen oder Abbrechen (das Fenster bleibt offen).

### Verbessert

- Einstellungen überstehen Updates und Beschädigungen: Eine beschädigte Einstellungsdatei wird beiseitegelegt (settings.corrupt-….json) und die Einstellungen kommen aus der letzten automatischen Sicherung zurück, mit einer Meldung. Eine Einstellungsdatei, die nicht gelesen werden kann, wird nie überschrieben, und Einstellungen einer neueren Version bleiben erhalten.
- Live-Radio braucht viel weniger Speicher: Es wird nur noch ein kleiner Puffer gehalten statt bis zu 200 MB.

### Behoben

- Radio: Nach Stopp (oder Pause) und Wiedergabe spielte ein Sender immer dasselbe Lied, ab dem Moment, in dem er eingeschaltet wurde. Stopp hält den Sender jetzt wirklich an, und Wiedergabe verbindet neu, live; der angehaltene Sender bleibt im Mini-Player angezeigt. Leertaste, Medientaste und Wiedergabe nach einem Verbindungsabbruch verbinden ebenfalls live neu, und Live-Radio lässt sich nicht mehr versehentlich zurückspulen.

### Bekannte Probleme

- Die App ist noch nicht digital signiert, daher können Windows SmartScreen oder Smart App Control vor der Installation warnen.
- Das Fortschrittsfenster des Installationsprogramms stammt von Velopack und ist nur auf Englisch.

Release: [v1.6.3](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.6.3) · Änderungen seit [v1.5.2...v1.6.3](https://github.com/jebaitedcro7/jebaited_player/compare/v1.5.2...v1.6.3)

## [1.5.2] - 2026-11-19

Radio-Korrektur.

### Behoben

- Das Radio spielte nichts, wenn ein Sender das Erste war, das nach dem Start abgespielt wurde (zum Beispiel wenn der Player als Mini-Player startete). Sender spielen jetzt immer.

### Bekannte Probleme

- Die App ist noch nicht digital signiert, daher können Windows SmartScreen oder Smart App Control vor der Installation warnen.
- Das Fortschrittsfenster des Installationsprogramms stammt von Velopack und ist nur auf Englisch.

Release: [v1.5.2](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.5.2) · Änderungen seit [v1.5.1...v1.5.2](https://github.com/jebaitedcro7/jebaited_player/compare/v1.5.1...v1.5.2)

## [1.5.1] - 2026-11-19

Radio: eine Abspieltaste und klare Rückmeldung.

### Verbessert

- Radio: Jeder Sender hat eine ▶-Taste (ein Klick auf den Namen funktioniert auch). Der laufende Sender ist grün, und das Fenster zeigt „Verbinde …“, „Läuft …“ oder dass ein Sender gerade nicht funktioniert, damit du einen anderen wählen kannst.
- Das Radiofenster bleibt offen, wenn sich der Mini-Player öffnet.
- Die Versionsnummer folgt der Projektregel: Die mittlere Zahl wächst mit jedem großen Upgrade, die letzte mit jeder Nachbesserung und wird nie zurückgesetzt (1.5.0 → 1.5.1).

### Bekannte Probleme

- Die App ist noch nicht digital signiert, daher können Windows SmartScreen oder Smart App Control vor der Installation warnen.
- Das Fortschrittsfenster des Installationsprogramms stammt von Velopack und ist nur auf Englisch.

Release: [v1.5.1](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.5.1) · Änderungen seit [v1.5.0...v1.5.1](https://github.com/jebaitedcro7/jebaited_player/compare/v1.5.0...v1.5.1)

## [1.5.0] - 2026-11-19

Radio sowie Speichern, Verwerfen und Standard in den Einstellungen.

### Neu

- Radio (Ctrl+R, Rechtsklickmenü oder die Quellenanzeige im Mini-Player): Suche unter zehntausenden Sendern nach Name, Genre und Land im kostenlosen Verzeichnis Radio Browser, speichere Favoriten mit dem Stern oder spiele eine beliebige Stream-URL. Sender laufen im Mini-Player mit dem Musik-Equalizer, gekennzeichnet mit RADIO und LIVE, mit dem aktuellen Titel, wenn der Sender ihn sendet.
- Einstellungen: Speichern behält die Änderungen, Änderungen verwerfen kehrt zum letzten Speichern zurück, und eine Anzeige zeigt, ob es nicht gespeicherte Änderungen gibt.
- Standard …: den aktuellen Tab oder alle Einstellungen auf die Standardwerte zurücksetzen. Sprache, Schlüssel und Passwörter, Lautstärke und Fensterpositionen bleiben.

### Verbessert

- Radiosender erscheinen nicht unter „Weiterschauen“.
- Die Datenschutzerklärung beschreibt das Radioverzeichnis.

### Bekannte Probleme

- Die App ist noch nicht digital signiert, daher können Windows SmartScreen oder Smart App Control vor der Installation warnen.
- Das Fortschrittsfenster des Installationsprogramms stammt von Velopack und ist nur auf Englisch.

Release: [v1.5.0](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.5.0) · Änderungen seit [v1.4.1...v1.5.0](https://github.com/jebaitedcro7/jebaited_player/compare/v1.4.1...v1.5.0)

## [1.4.1] - 2026-11-19

Getrennte Equalizer für Filme und Musik, Aussehen des Mini-Players und automatisches Umschalten.

### Neu

- Zwei Equalizer: einer für Filme und Serien, einer für Musik und Radio. Verwendet wird automatisch der für das, was gerade läuft. Unter Einstellungen → Audio wählst du, welchen du bearbeitest; der Mini-Player zeigt den für Musik.
- Aussehen des Mini-Players: sechs Anzeigeschriften und vier VU-Meter-Stile (Balken, LED-Segmente, dünne Linien mit Spitze, aus). Per Rechtsklick auf den Mini-Player oder unter Einstellungen → Darstellung.
- Musik öffnet den Mini-Player, ein Film oder eine Serie den vollen Player. Das lässt sich ausschalten.

### Verbessert

- Der Mini-Player öffnet sich immer dort, wo du ihn gelassen hast. Wird der Player als Mini-Player geschlossen, startet er beim nächsten Mal wieder als Mini-Player.
- Rechtsklickmenü am Mini-Player: Schrift, VU-Meter, Größe, Leiste, Equalizer, Wiedergabeliste, automatisches Umschalten und Einstellungen.

### Bekannte Probleme

- Die App ist noch nicht digital signiert, daher können Windows SmartScreen oder Smart App Control vor der Installation warnen.
- Das Fortschrittsfenster des Installationsprogramms stammt von Velopack und ist nur auf Englisch.

Release: [v1.4.1](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.4.1) · Änderungen seit [v1.4.0...v1.4.1](https://github.com/jebaitedcro7/jebaited_player/compare/v1.4.0...v1.4.1)

## [1.4.0] - 2026-11-19

Mini-Musikplayer, automatische Sicherungen sowie Export und Import der Einstellungen.

### Neu

- Mini-Player (Rechtsklickmenü oder Ctrl+M): ein kleines Fenster im Winamp-Stil im jebaited_cro-Look, das im Vordergrund bleibt. Es hat eine grüne Anzeige mit Zeit und echter Pegelanzeige (L/R), laufendem Interpreten und Titel mit Bitrate, Suchleiste, Zurück, Abspielen, Pause, Stopp, Weiter, Öffnen und Lautstärke sowie einen Equalizer und eine Wiedergabeliste, die sich darunter öffnen. Er lässt sich zu einer schmalen Leiste verkleinern, und seine Größe ändert sich durch Ziehen an der Ecke, mit Ctrl + Mausrad oder mit Ctrl + / −. Die Musik läuft beim Umschalten weiter, Ctrl+M führt zurück.
- Automatische Sicherungen: Einstellungen und Wiedergabeverlauf werden einmal täglich und vor jedem Update kopiert. Die letzten 7 Kopien bleiben auf deinem Computer (Einstellungen → Allgemein → Sicherung und Übertragung).
- Einstellungen exportieren und importieren: Speichere deine Einstellungen in einer Datei und lade sie wieder, auch auf einem anderen Computer. Passwörter, Schlüssel für Untertitelseiten und die Fensterposition werden nicht exportiert. Vor einem Import werden die aktuellen Einstellungen gesichert, und eine falsche Datei wird abgelehnt, ohne etwas zu ändern.

### Verbessert

- Ein einzelnes Lied öffnet das ganze Album (alle Lieder in diesem Ordner), und das nächste Lied startet sofort, ohne Countdown.
- Musik füllt „Weiterschauen“ auf dem Startbildschirm nicht mehr.
- Der Equalizer funktioniert in den Einstellungen und im Mini-Player gleich.

### Bekannte Probleme

- Die App ist noch nicht digital signiert, daher können Windows SmartScreen oder Smart App Control vor der Installation warnen.
- Das Fortschrittsfenster des Installationsprogramms stammt von Velopack und ist nur auf Englisch.

Release: [v1.4.0](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.4.0) · Änderungen seit [v1.3.0...v1.4.0](https://github.com/jebaitedcro7/jebaited_player/compare/v1.3.0...v1.4.0)

## [1.3.0] - 2026-11-19

Equalizer, ein zuverlässiger Status „Angesehen“ und lokale Diagnose.

### Neu

- Equalizer unter Einstellungen → Audio: 10 Bänder und Vorverstärkung, Voreinstellungen (Neutral, Rock, Pop, Jazz, Klassik, Elektronisch, Mehr Bass, Mehr Höhen, Gesang und Dialog) und eine eigene Einstellung (Eigene). Änderungen sind sofort hörbar, ohne die Wiedergabe zu unterbrechen, und er funktioniert zusammen mit dem Lautstärkeausgleich. Beim Anschauen ist er auch im Audio-Menü.
- Diagnose / Protokolle (Einstellungen → Info): Der Player führt ein lokales Protokoll über Fehler, Abstürze und Wiedergabeprobleme, damit sie gefunden und behoben werden können. Es lässt sich ansehen, filtern und als ZIP exportieren (Dateinamen können anonymisiert werden). Nichts wird irgendwohin gesendet, alte Protokolle werden automatisch gelöscht.

### Verbessert

- „Angesehen“ heißt jetzt wirklich angesehen: Mindestens 80 % des Videos wurden tatsächlich abgespielt. Überspringen oder ein Sprung ans Ende markiert ein Video nicht mehr als angesehen, und derselbe Teil zählt nur einmal.
- Angesehene Videos zeigen auf dem Startbildschirm und in der Episodenliste ein grünes Häkchen, „Angesehen“ und einen vollen grünen Balken.

### Behoben

- Wird eine angesehene Episode oder ein Film erneut geöffnet und früher geschlossen, verschwinden der grüne Balken und der Status „Angesehen“ nicht mehr. Nur „Als angesehen / nicht angesehen markieren“ in der Episodenliste ändert ihn.

### Bekannte Probleme

- Die App ist noch nicht digital signiert, daher können Windows SmartScreen oder Smart App Control vor der Installation warnen.
- Das Fortschrittsfenster des Installationsprogramms stammt von Velopack und ist nur auf Englisch.

Release: [v1.3.0](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.3.0) · Änderungen seit [v1.2.0...v1.3.0](https://github.com/jebaitedcro7/jebaited_player/compare/v1.2.0...v1.3.0)

## [1.2.0] - 2026-11-17

Player-Steuerung, Einstellungen in Tabs und der Update-Verlauf.

### Neu

- Startseite-Taste oben links im Player: zurück zum Startbildschirm, ohne die App zu schließen. Die Position wird gespeichert, du kannst später weiterschauen. Sie erscheint und verschwindet zusammen mit den anderen Steuerelementen, auch im Vollbild.
- Zeitleiste: Klicke irgendwo hin, um sofort dorthin zu springen. Ziehen funktioniert weiterhin, und eine Blase zeigt die Zeit unter der Maus.
- Die Einstellungen sind jetzt in Tabs aufgeteilt: Allgemein, Sprache, Untertitel, Audio, Video, Darstellung, Updates und Info.
- Neue Einstellungen: was beim erneuten Öffnen eines Videos passiert (fragen, fortsetzen oder von vorne), Sprungweite der Pfeiltasten, Videos im Vollbild öffnen, Player im Vordergrund halten, bevorzugte Audiosprache, Audioausgabegerät, Audioverzögerung, Lautstärkeausgleich, Hardware-Dekodierung, Bildqualität, Standard-Seitenverhältnis, HDR-Durchleitung, wie lange die Steuerelemente sichtbar bleiben und die Kodierung alter Untertitel.
- Update-Verlauf: Klicke auf die Versionsnummer auf dem Startbildschirm. Er ist in der App enthalten und funktioniert ohne Internet; online werden auch neuere Versionen von GitHub angezeigt.

### Verbessert

- Ein Klick auf die Zeitleiste springt sofort genau an die Stelle (kein zweiter Sprung). Die Zeitleiste ist leichter zu treffen und wird unter der Maus dicker.
- „Zurück zum Startbildschirm“ gibt es auch im Rechtsklickmenü.
- Richtlinie zur Code-Signierung, Datenschutzerklärung und automatische Builds mit GitHub Actions als Vorbereitung für die kostenlose Code-Signierung durch die SignPath Foundation.

### Bekannte Probleme

- Die App ist noch nicht digital signiert, daher können Windows SmartScreen oder Smart App Control vor der Installation warnen.
- Das Fortschrittsfenster des Installationsprogramms stammt von Velopack und ist nur auf Englisch.

Release: [v1.2.0](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.2.0) · Änderungen seit [v1.1.0...v1.2.0](https://github.com/jebaitedcro7/jebaited_player/compare/v1.1.0...v1.2.0)

## [1.1.0] - 2026-11-17

Sprachen, ein neuer Startbildschirm, Designs und Handbücher.

### Neu

- Oberfläche auf Englisch, Deutsch und Kroatisch; unter „Weitere Sprachen“ auch Bosnisch, Serbisch (Latein) und Slowenisch. Alle Menüs, Einstellungen, Meldungen, Fehler, Synchronisierung, Online-Untertitel, die Folgenliste und der Verlauf sind übersetzt.
- Sprachauswahl beim ersten Start (direkt nach der Installation); ohne Auswahl bleibt Englisch. Die Sprache lässt sich in den Einstellungen sofort und ohne Neustart ändern und wird gespeichert.
- Neuer Startbildschirm: das Alien-Logo und der Name jebaited_cro, ein dezenter Weltraumhintergrund, ein Zahnrad für die Einstellungen unten links und ein Schalter für dunkles/helles Design.
- Handbücher in drei Sprachen (Quick Start, Basic Guide, Advanced Guide) mit Screenshots: README_EN, README_DE und README_HR.

### Verbessert

- Eine neue Installation erhält Standard-Untertitelsprachen passend zur gewählten Sprache. Vorhandene Einstellungen, der Wiedergabeverlauf und Schlüssel bleiben erhalten.

### Bekannte Probleme

- Das Fortschrittsfenster des Installationsprogramms stammt von Velopack und ist nur auf Englisch.
- Die App ist noch nicht digital signiert, daher können Windows SmartScreen oder Smart App Control vor der Installation warnen.

Release: [v1.1.0](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.1.0) · Änderungen seit [v1.0.1...v1.1.0](https://github.com/jebaitedcro7/jebaited_player/compare/v1.0.1...v1.1.0)

## [1.0.1] - 2026-11-17

Prüfung der automatischen Updates. Der Player funktioniert wie in 1.0.0.

### Verbessert

- Automatische Updates von GitHub geprüft: Version 1.0.0 aktualisiert sich selbst auf 1.0.1, Einstellungen und Wiedergabeverlauf bleiben erhalten.

### Bekannte Probleme

- Die App ist noch nicht digital signiert, daher können Windows SmartScreen oder Smart App Control vor der Installation warnen.

Release: [v1.0.1](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.0.1) · Änderungen seit [v1.0.0...v1.0.1](https://github.com/jebaitedcro7/jebaited_player/compare/v1.0.0...v1.0.1)

## [1.0.0] - 2026-11-17

Erste öffentliche Version.

### Neu

- Spielt fast jedes Video- und Audioformat ab (mpv): H.264, HEVC, AV1, VP9, 4K, HDR, 5.1-Ton und Hardware-Dekodierung.
- Untertitel aus dem Internet: OpenSubtitles, SubDL und Titlovi.com; die Untertitel für deine Version werden automatisch gewählt und neben dem Video gespeichert.
- Synchronisierung zur Sprache: Versatz, Geschwindigkeit (25 / 23,976 fps) und abschnittsweise Korrektur für eine andere Schnittfassung; Untertitel einer anderen Folge werden abgelehnt.
- Manuelle Synchronisierung: Versatz, schrittweise Verzögerung, Ausrichtung nach Zeile (ohne Eile) und Z/X.
- Serien: Folgenliste, Weiterschauen und die nächste Folge.
- Assistent für Untertitel aus dem Internet: öffnet die Registrierung, prüft und speichert deine Daten (verschlüsselt).
- Windows: eine einzige Instanz (eine neue Datei geht an den offenen Player), mehrere Dateien auf einmal, „Öffnen mit“, Registrierung der Dateitypen und Festlegen als Standardplayer über die Windows-Einstellungen.
- Installation ohne Administratorrechte, Verknüpfungen und automatische Updates (beim Schließen des Players installiert); Einstellungen und Verlauf bleiben erhalten.

### Bekannte Probleme

- Die App ist noch nicht digital signiert, daher können Windows SmartScreen oder Smart App Control vor der Installation warnen.

Release: [v1.0.0](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.0.0)
