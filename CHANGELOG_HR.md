# Povijest promjena

Sve važnije promjene jebaited_playera. Oblik: [Keep a Changelog](https://keepachangelog.com/hr/1.1.0/); brojevi verzija: prvi broj se mijenja samo za velike prekretnice (npr. nove platforme), drugi raste sa svakim ciklusom nadogradnji s novim funkcijama, a zadnji broji popravke i nikad se ne vraća na 0 (od 1.6.3; ranije verzije slijedile su [Semantic Versioning](https://semver.org/lang/hr/)).

Drugi jezici: [English](CHANGELOG.md) · [Deutsch](CHANGELOG_DE.md). Ista povijest je i u playeru (klik na broj verzije na početnom ekranu). Ova datoteka nastaje iz `src/JebaitedPlayer.App/History/update-history.json`.

## [1.7.4] - 2026-11-21

Ciklus 5: Spotify daljinski, „Sljedeće za gledanje” na početnom ekranu i vlastita pozadina.

### Novo

- Spotify (Ctrl+Shift+S ili oznaka izvora u mini playeru): prijava sa svojom besplatnom Spotify developer aplikacijom (vodič je u prozoru), pjesma s omotom, play, pauza, sljedeća, premotavanje, glasnoća, tvoje playliste i spremljene pjesme, pretraga i izbor uređaja. Glazba svira u tvojoj Spotify aplikaciji; dok je Spotify izvor, tipke mini playera upravljaju njime. Za upravljanje treba Spotify Premium.
- „Sljedeće za gledanje” na početnom ekranu: sljedeća epizoda serije koju gledaš pojavi se sama (i preko granice sezone), možeš dodati bilo koji video (desni klik na video ili na stavku „Nastavi gledati”), redoslijed mijenjaš s ▲ ▼, a × uklanja. Stavka nestaje kad je pogledaš.
- Postavke → Izgled → Pozadina početnog ekrana: zadana (svemir) ili vlastita slika (Ispuni, Prilagodi ili Sredina), s pregledom. Slika se ne kopira; ako je premjestiš ili obrišeš, vraća se svemirska pozadina.

### Poboljšano

- Velika slika pozadine učitava se u pozadini i samo u veličini zaslona, pa ne usporava pokretanje.

### Poznati problemi

- Aplikacija još nije digitalno potpisana, pa Windows SmartScreen ili Smart App Control mogu upozoriti prije instalacije.
- Prozor napretka instalacije je Velopackov i samo je na engleskom.

Izdanje: [v1.7.4](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.7.4) · Promjene od [v1.6.3...v1.7.4](https://github.com/jebaitedcro7/jebaited_player/compare/v1.6.3...v1.7.4)

## [1.6.3] - 2026-11-20

Ciklus 4: radio nakon Stopa opet uživo, zaštićene postavke, pitanje za nespremljene promjene i zaseban izlaz za glazbu.

### Novo

- Postavke → Zvuk → „Izlaz za glazbu i radio”: glazba, radio i mini player mogu svirati na drugom izlazu nego filmovi (npr. na virtualnom kabelu za Discord). Zadano je isti izlaz kao za filmove.
- Zatvaranje postavki s nespremljenim promjenama sada pita: Spremi, Odbaci promjene ili Odustani (prozor ostaje otvoren).

### Poboljšano

- Postavke preživljavaju nadogradnje i oštećenja: oštećena datoteka postavki ostaje sačuvana sa strane (settings.corrupt-….json), a postavke se vraćaju iz zadnje automatske kopije, uz poruku. Datoteka postavki koja se ne može pročitati nikad se ne prepisuje, a postavke iz novije verzije ostaju.
- Radio uživo troši puno manje memorije: čuva se samo mali međuspremnik umjesto do 200 MB.

### Ispravljeno

- Radio: nakon Stopa (ili pauze) i Playa postaja je uvijek svirala istu pjesmu, od trenutka kad je bila uključena. Stop sada stvarno zaustavlja postaju, a Play je ponovno spaja, uživo; zaustavljena postaja ostaje prikazana u mini playeru. Razmak, medijska tipka i Play nakon prekida veze također spajaju ponovno uživo, a radio uživo se više ne može slučajno premotati.

### Poznati problemi

- Aplikacija još nije digitalno potpisana, pa Windows SmartScreen ili Smart App Control mogu upozoriti prije instalacije.
- Prozor napretka instalacije je Velopackov i samo je na engleskom.

Izdanje: [v1.6.3](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.6.3) · Promjene od [v1.5.2...v1.6.3](https://github.com/jebaitedcro7/jebaited_player/compare/v1.5.2...v1.6.3)

## [1.5.2] - 2026-11-19

Popravak radija.

### Ispravljeno

- Radio nije svirao kad je postaja bila prvo što pustiš nakon pokretanja playera (npr. kad se player otvori kao mini player). Postaje sada uvijek sviraju.

### Poznati problemi

- Aplikacija još nije digitalno potpisana, pa Windows SmartScreen ili Smart App Control mogu upozoriti prije instalacije.
- Prozor napretka instalacije je Velopackov i samo je na engleskom.

Izdanje: [v1.5.2](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.5.2) · Promjene od [v1.5.1...v1.5.2](https://github.com/jebaitedcro7/jebaited_player/compare/v1.5.1...v1.5.2)

## [1.5.1] - 2026-11-19

Radio: gumb za puštanje i jasna poruka.

### Poboljšano

- Radio: svaka postaja ima gumb ▶ (radi i klik na naziv). Postaja koja svira je zelena, a prozor piše „Povezujem…”, „Svira…” ili da postaja trenutačno ne radi, pa možeš odabrati drugu.
- Prozor Radio ostaje otvoren kad se otvori mini player.
- Broj verzije prati pravilo projekta: srednji broj raste sa svakim velikim upgradeom, a zadnji sa svakom doradom i nikad se ne vraća na 0 (1.5.0 → 1.5.1).

### Poznati problemi

- Aplikacija još nije digitalno potpisana, pa Windows SmartScreen ili Smart App Control mogu upozoriti prije instalacije.
- Prozor napretka instalacije je Velopackov i samo je na engleskom.

Izdanje: [v1.5.1](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.5.1) · Promjene od [v1.5.0...v1.5.1](https://github.com/jebaitedcro7/jebaited_player/compare/v1.5.0...v1.5.1)

## [1.5.0] - 2026-11-19

Radio te Spremi, Odbaci i Zadano u postavkama.

### Novo

- Radio (Ctrl+R, izbornik desnog klika ili oznaka izvora u mini playeru): traži među desecima tisuća postaja po nazivu, žanru i državi u besplatnom imeniku Radio Browser, spremi favorite zvjezdicom ili pusti bilo koji URL streama. Postaje sviraju u mini playeru s glazbenim equalizerom, označene s RADIO i UŽIVO, uz trenutnu pjesmu kad je postaja šalje.
- Postavke: Spremi zadržava promjene, Odbaci promjene vraća zadnje spremanje, a natpis pokazuje ima li nespremljenih promjena.
- Zadano…: vrati trenutnu karticu ili sve postavke na zadane vrijednosti. Jezik, ključevi i lozinke, glasnoća i položaji prozora ostaju.

### Poboljšano

- Radio postaje ne ulaze u „Nastavi gledati”.
- Izjava o privatnosti opisuje imenik radija.

### Poznati problemi

- Aplikacija još nije digitalno potpisana, pa Windows SmartScreen ili Smart App Control mogu upozoriti prije instalacije.
- Prozor napretka instalacije je Velopackov i samo je na engleskom.

Izdanje: [v1.5.0](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.5.0) · Promjene od [v1.4.1...v1.5.0](https://github.com/jebaitedcro7/jebaited_player/compare/v1.4.1...v1.5.0)

## [1.4.1] - 2026-11-19

Odvojeni equalizeri za filmove i glazbu, izgled mini playera i automatsko prebacivanje.

### Novo

- Dva equalizera: jedan za filmove i serije, drugi za glazbu i radio. Sam se koristi onaj za ono što svira. U Postavke → Zvuk biraš koji uređuješ, a mini player prikazuje onaj za glazbu.
- Izgled mini playera: šest fontova zaslona i četiri izgleda VU metra (trake, LED segmenti, tanke linije s vrhom, isključen). Desni klik na mini player ili Postavke → Izgled.
- Otvaranje glazbe pokreće mini player, a otvaranje filma ili serije puni player. Može se isključiti.

### Poboljšano

- Mini player se uvijek otvara gdje si ga ostavio. Ako zatvoriš player dok je mini player, sljedeći put se opet otvara kao mini player.
- Izbornik desnog klika na mini playeru: font, VU metar, veličina, traka, equalizer, popis pjesama, automatsko prebacivanje i postavke.

### Poznati problemi

- Aplikacija još nije digitalno potpisana, pa Windows SmartScreen ili Smart App Control mogu upozoriti prije instalacije.
- Prozor napretka instalacije je Velopackov i samo je na engleskom.

Izdanje: [v1.4.1](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.4.1) · Promjene od [v1.4.0...v1.4.1](https://github.com/jebaitedcro7/jebaited_player/compare/v1.4.0...v1.4.1)

## [1.4.0] - 2026-11-19

Mini music player, automatske kopije te izvoz i uvoz postavki.

### Novo

- Mini player (izbornik desnog klika ili Ctrl+M): mali prozor u Winamp stilu, u jebaited_cro izgledu, uvijek iznad ostalih prozora. Ima zeleni zaslon s vremenom i stvarnim mjeračem razine (L/D), pomični izvođač i naslov s bitrateom, traku za premotavanje, prethodna, pokreni, pauza, zaustavi, sljedeća, otvori i glasnoću te equalizer i popis pjesama koji se otvaraju ispod. Može se smanjiti u usku traku, a veličina se mijenja povlačenjem kuta, Ctrl + kotačićem miša ili Ctrl + / −. Glazba svira dalje kad se prebaciš, a Ctrl+M te vraća natrag.
- Automatske kopije: postavke i povijest gledanja kopiraju se jednom dnevno i prije svake nadogradnje. Zadnjih 7 kopija ostaje na tvom računalu (Postavke → Općenito → Kopije i prijenos).
- Izvoz i uvoz postavki: spremi postavke u datoteku i učitaj ih ponovno, i na drugom računalu. Lozinke, ključevi za stranice s titlovima i položaj prozora se ne izvoze. Prije uvoza trenutne postavke se kopiraju, a kriva datoteka se odbija bez ikakve promjene.

### Poboljšano

- Otvaranje jedne pjesme pušta cijeli album (sve pjesme iz te mape), a sljedeća pjesma kreće odmah, bez odbrojavanja.
- Glazba više ne puni „Nastavi gledati” na početnom ekranu.
- Equalizer radi isto u postavkama i u mini playeru.

### Poznati problemi

- Aplikacija još nije digitalno potpisana, pa Windows SmartScreen ili Smart App Control mogu upozoriti prije instalacije.
- Prozor napretka instalacije je Velopackov i samo je na engleskom.

Izdanje: [v1.4.0](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.4.0) · Promjene od [v1.3.0...v1.4.0](https://github.com/jebaitedcro7/jebaited_player/compare/v1.3.0...v1.4.0)

## [1.3.0] - 2026-11-19

Equalizer, pouzdan status „Pogledano” i lokalna dijagnostika.

### Novo

- Equalizer u Postavke → Zvuk: 10 pojaseva i pojačanje, preseti (Ravno, Rock, Pop, Jazz, Klasika, Elektronika, Jači basovi, Jači visoki tonovi, Vokal i dijalog) i tvoj vlastiti preset (Vlastiti). Promjene se čuju odmah, bez prekida reprodukcije, a radi zajedno s ujednačavanjem glasnoće. Tijekom gledanja je i u izborniku Audio.
- Dijagnostika / Logovi (Postavke → O aplikaciji): player lokalno bilježi greške, rušenja i probleme s reprodukcijom, da ih je lakše pronaći i popraviti. Zapis možeš pregledati, filtrirati i izvesti kao ZIP (nazivi datoteka mogu biti anonimizirani). Ništa se nikamo ne šalje, a stari zapisi se sami brišu.

### Poboljšano

- „Pogledano” sada znači stvarno pogledano: odgledano je barem 80 % videa. Preskakanje ili skok na kraj više ne označava video pogledanim, a isti dio gledan dvaput broji se jednom.
- Pogledani videi na početnom ekranu i u popisu epizoda imaju zelenu kvačicu, natpis „Pogledano” i punu zelenu traku.

### Ispravljeno

- Ponovno otvaranje pogledane epizode ili filma i ranije zatvaranje više ne briše zelenu traku ni status „Pogledano”. Mijenja ga samo „Označi kao pogledano / nepogledano” u popisu epizoda.

### Poznati problemi

- Aplikacija još nije digitalno potpisana, pa Windows SmartScreen ili Smart App Control mogu upozoriti prije instalacije.
- Prozor napretka instalacije je Velopackov i samo je na engleskom.

Izdanje: [v1.3.0](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.3.0) · Promjene od [v1.2.0...v1.3.0](https://github.com/jebaitedcro7/jebaited_player/compare/v1.2.0...v1.3.0)

## [1.2.0] - 2026-11-17

Kontrole playera, postavke u karticama i povijest nadogradnji.

### Novo

- Tipka Početna gore lijevo u playeru: povratak na početni ekran bez zatvaranja aplikacije. Pozicija se sprema, pa možeš nastaviti gledati kasnije. Pojavljuje se i skriva zajedno s ostalim kontrolama, i u cijelom zaslonu.
- Vremenska traka: klikni bilo gdje i reprodukcija odmah skoči na to mjesto. Povlačenje i dalje radi, a oblačić pokazuje vrijeme ispod miša.
- Postavke su sada podijeljene u kartice: Općenito, Jezik, Titlovi, Zvuk, Slika, Izgled, Nadogradnje i O aplikaciji.
- Nove postavke: što se događa kad ponovno otvoriš video (pitaj, nastavi ili od početka), korak premotavanja strelicama, otvaranje videa u cijelom zaslonu, player uvijek iznad ostalih prozora, željeni jezik zvuka, izlazni uređaj zvuka, kašnjenje zvuka, ujednačavanje glasnoće, hardversko dekodiranje, kvaliteta slike, zadani omjer slike, HDR prolaz, koliko dugo su kontrole vidljive i kodiranje starih titlova.
- Povijest nadogradnji: klikni na broj verzije na početnom ekranu. Uključena je u aplikaciju, pa radi i bez interneta; kad si na mreži, prikazuju se i novije verzije s GitHuba.

### Poboljšano

- Klik na vremensku traku odmah precizno premota (bez drugog skoka). Traku je lakše pogoditi i pod mišem postaje deblja.
- „Natrag na početni ekran” je i u izborniku desnog klika.
- Pravila potpisivanja koda, izjava o privatnosti i automatske izrade na GitHub Actions, kao priprema za besplatno potpisivanje preko SignPath Foundationa.

### Poznati problemi

- Aplikacija još nije digitalno potpisana, pa Windows SmartScreen ili Smart App Control mogu upozoriti prije instalacije.
- Prozor napretka instalacije je Velopackov i samo je na engleskom.

Izdanje: [v1.2.0](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.2.0) · Promjene od [v1.1.0...v1.2.0](https://github.com/jebaitedcro7/jebaited_player/compare/v1.1.0...v1.2.0)

## [1.1.0] - 2026-11-17

Jezici, novi početni ekran, teme i priručnici.

### Novo

- Sučelje na engleskom, njemačkom i hrvatskom, a pod „Više jezika” i bosanski, srpski (latinica) i slovenski. Prevedeni su svi izbornici, postavke, poruke, greške, sinkronizacija, titlovi s interneta, popis epizoda i povijest.
- Odabir jezika pri prvom pokretanju (odmah nakon instalacije); bez odabira ostaje engleski. Jezik se mijenja u Postavkama, odmah i bez ponovnog pokretanja, i pamti se.
- Novi početni ekran: Alien logo i ime jebaited_cro, diskretna svemirska pozadina, zupčanik za Postavke dolje lijevo i prekidač tamna/svijetla tema.
- Priručnici na tri jezika (Quick Start, Basic Guide, Advanced Guide) sa snimkama zaslona: README_EN, README_DE i README_HR.

### Poboljšano

- Nova instalacija dobiva zadane jezike titlova prema odabranom jeziku. Postojeće postavke, povijest gledanja i ključevi ostaju.

### Poznati problemi

- Prozor napretka instalacije je Velopackov i samo je na engleskom.
- Aplikacija još nije digitalno potpisana, pa Windows SmartScreen ili Smart App Control mogu upozoriti prije instalacije.

Izdanje: [v1.1.0](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.1.0) · Promjene od [v1.0.1...v1.1.0](https://github.com/jebaitedcro7/jebaited_player/compare/v1.0.1...v1.1.0)

## [1.0.1] - 2026-11-17

Provjera automatskih nadogradnji. Player radi isto kao u 1.0.0.

### Poboljšano

- Provjerene automatske nadogradnje s GitHuba: verzija 1.0.0 sama se nadogradi na 1.0.1, a postavke i povijest gledanja ostaju.

### Poznati problemi

- Aplikacija još nije digitalno potpisana, pa Windows SmartScreen ili Smart App Control mogu upozoriti prije instalacije.

Izdanje: [v1.0.1](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.0.1) · Promjene od [v1.0.0...v1.0.1](https://github.com/jebaitedcro7/jebaited_player/compare/v1.0.0...v1.0.1)

## [1.0.0] - 2026-11-17

Prvo javno izdanje.

### Novo

- Reprodukcija gotovo svih video i audio formata (mpv): H.264, HEVC, AV1, VP9, 4K, HDR, 5.1 zvuk i hardversko dekodiranje.
- Titlovi s interneta: OpenSubtitles, SubDL i Titlovi.com; titlovi za tvoje izdanje biraju se automatski i spremaju uz video.
- Sinkronizacija prema govoru: pomak, brzina (25 / 23,976 fps) i ispravak po dijelovima za drugu montažu; titlovi druge epizode se odbijaju.
- Ručna sinkronizacija: pomak, postupno kašnjenje, poravnanje po replici (bez žurbe) i Z/X.
- Serije: popis epizoda, nastavak gledanja i sljedeća epizoda.
- Vodič za titlove s interneta: otvori registraciju, provjeri i spremi podatke (kriptirano).
- Windows: jedna instanca (nova datoteka ide u otvoreni player), više datoteka odjednom, „Otvori pomoću”, registracija formata i postavljanje zadanog playera kroz Postavke sustava Windows.
- Instalacija bez administratora, prečaci i automatske nadogradnje (instaliraju se pri zatvaranju playera); postavke i povijest ostaju.

### Poznati problemi

- Aplikacija još nije digitalno potpisana, pa Windows SmartScreen ili Smart App Control mogu upozoriti prije instalacije.

Izdanje: [v1.0.0](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.0.0)
