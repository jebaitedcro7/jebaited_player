# Änderungsprotokoll

Alle wichtigen Änderungen an jebaited_player. Format: [Keep a Changelog](https://keepachangelog.com/de/1.1.0/); Versionen nach [Semantic Versioning](https://semver.org/lang/de/) (MAJOR = große Änderungen, die die Kompatibilität betreffen können, MINOR = neue Funktionen, PATCH = Fehlerbehebungen und kleine Verbesserungen).

Andere Sprachen: [English](CHANGELOG.md) · [Hrvatski](CHANGELOG_HR.md). Derselbe Verlauf ist im Player enthalten (Klick auf die Versionsnummer auf dem Startbildschirm). Diese Datei wird aus `src/JebaitedPlayer.App/History/update-history.json` erzeugt.

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
