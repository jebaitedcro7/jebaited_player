# Povijest promjena

Sve važnije promjene jebaited_playera. Oblik: [Keep a Changelog](https://keepachangelog.com/hr/1.1.0/); brojevi verzija: prvi broj se mijenja samo za velike prekretnice (npr. nove platforme), drugi raste sa svakim ciklusom nadogradnji s novim funkcijama, a zadnji broji popravke i nikad se ne vraća na 0 (od 1.6.3; ranije verzije slijedile su [Semantic Versioning](https://semver.org/lang/hr/)).

Drugi jezici: [English](CHANGELOG.md) · [Deutsch](CHANGELOG_DE.md). Ista povijest je i u playeru (klik na broj verzije na početnom ekranu). Ova datoteka nastaje iz `src/JebaitedPlayer.App/History/update-history.json`.

## [2.4.16] - 2026-11-24

IPTV: gledaj svoju IPTV uslugu (Xtream ili M3U) u playeru, uz novu bočnu traku na početnom ekranu.

### Novo

- IPTV: dodaj vlastitu vezu (Xtream Codes, M3U/M3U8 poveznicu ili datoteku) i gledaj kanale uživo uz video. Kategorije sa zastavama i ikonama, pretraga, favoriti, nedavno gledano, skrivanje kanala i kategorija (desni klik), sortiranje A–Ž, Ž–A, izvorno ili po tvom redoslijedu, vodič programa (sada/sljedeće, vremenska crta po danima), cijeli zaslon tipkom F ili dvoklikom. Možeš promijeniti font, veličinu teksta i boje IPTV panela te povući rubove na željenu širinu. Lozinka se sprema šifrirana na tvom računalu, nikad u dnevnike ni izvoze. Koristi samo usluge koje smiješ koristiti; player ne donosi kanale ni popise.
- Bočna traka na početnom ekranu: otvori datoteku, otvori mapu, pretraga titlova, IPTV i radio sada su na jednom mjestu; pod mišem se glatko proširi i pokaže nazive. Kartica playera na početnom ekranu ima gumb Stop za tokove uživo.

### Poboljšano

- Tokovi uživo (TV, radio) prikazuju punu traku napretka bez trajanja, a klik na sliku više ne pauzira TV uživo.
- Vjerodajnice u IPTV adresama skrivene su u dnevnicima i izvještajima o greškama.

### Poznati problemi

- IPTV poslužitelji koji nude samo običan HTTP nisu šifrirani; player na to upozorava. Reprodukcija catch-up, vlastite grupe kanala i slika u slici još nisu dostupni.

Izdanje: [v2.4.16](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.4.16) · Promjene od [v2.3.15...v2.4.16](https://github.com/jebaitedcro7/jebaited_player/compare/v2.3.15...v2.4.16)

## [2.3.15] - 2026-11-22

Obavijesti na početnom ekranu, odabir izgleda mini playera u Postavkama i ispravci.

### Novo

- Obavijesti: mala kartica na početnom ekranu prikazuje novosti, savjete i nove verzije. Kad nešto svira, nalazi se iznad trake reprodukcije, a kad ništa ne svira, nisko, tik iznad broja verzije; tanka strelica pokazuje na broj verzije, a kartica se glatko pomiče kad se traka reprodukcije pojavi ili nestane. Nova tipka Obavijesti (dolje lijevo, s brojem nepročitanih) i Postavke → Obavijesti prikazuju sve, i pročitane i odbačene. Kartica se prikazuje najviše tri puta; stanje je na tvom računalu i ništa novo ne ide na internet.
- Postavke → Izgled → Mini player: svih pet izgleda (klasični, kartica, uska traka, bočna ploča, NLO zaslon) uz pregled. Promjena se primjenjuje odmah i pamti se.

### Poboljšano

- Vrijeme reprodukcije je manje i centrirano, stabilne širine, u punom playeru i u svakom izgledu mini playera.
- Mini player: traka glasnoće odmah prati kotačić miša (prije je za zvukom kasnila trenutak).

### Ispravljeno

- Tekst upisan u polja za unos (npr. pretraga radija) bio je odrezan pri dnu.

Izdanje: [v2.3.15](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.3.15) · Promjene od [v2.2.14...v2.3.15](https://github.com/jebaitedcro7/jebaited_player/compare/v2.2.14...v2.3.15)

## [2.2.14] - 2026-11-22

Torlink: pretraga i preuzimanje online izravno u playeru (GitHub inačica, neobavezno, zadano isključeno).

### Novo

- Torlink (Postavke → Torlink, samo GitHub inačica): na početnom ekranu uz Otvori datoteku i Otvori mapu pojavljuje se treći gumb, Pretraži online, koji otvara zasebnu stranicu. Jedna pretraga kroz više javnih indeksa (filmovi, serije, anime, igre) s veličinom, seederima i izvorom, sortiranjem i filtrima te upravitelj preuzimanja (aktivna, red, završena, neuspjela; pauza, nastavak, otkaži, otvori mapu, reproduciraj). Home (Esc, Alt+←) vraća u player, a preuzimanja se nastavljaju; Ctrl+Shift+T otvara i zatvara stranicu.
- Torlink je projekt otvorenog koda baairon/torlink (MIT), korišten nepromijenjen i pokrenut samo dok treba. Zadano je isključen, prvo traži tvoj prihvat i preuzima se kao provjereni paket (SHA-256 paketa i svake datoteke). Ažuriranje samo na tvoj klik, uz automatski povratak. Dijeljenje nakon preuzimanja je zadano isključeno.
- Postavke → Torlink: uključi ili isključi (isključeno znači bez procesa i bez mrežne potrošnje), verzije i provjera ažuriranja, mapa za preuzimanja, istodobna preuzimanja, playlist.m3u, dodatni trackeri, mrežno sučelje (VPN), dijeljenje.

### Poboljšano

- Torlink nije dio Microsoft Store inačice.

### Poznati problemi

- BitTorrent drugim korisnicima otkriva tvoju IP adresu. Bez odabranog mrežnog sučelja Torlink koristi i otkrivanje na lokalnoj mreži i traži od usmjerivača otvoren port (UPnP/NAT-PMP); veze s drugim sudionicima nisu šifrirane. Vidi PRIVACY.md.

Izdanje: [v2.2.14](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.2.14) · Promjene od [v2.1.13...v2.2.14](https://github.com/jebaitedcro7/jebaited_player/compare/v2.1.13...v2.2.14)

## [2.1.13] - 2026-11-22

Omot albuma u mini playeru i na početnom ekranu, vlastita glasnoća za Discord i player koji uvijek pokaže prozor.

### Novo

- Omot albuma: izgledi „kartica” i „bočna ploča” mini playera i početni ekran prikazuju omot pjesme (ugrađen u MP3, FLAC, M4A ili Ogg/Opus, ili slika cover/folder uz pjesmu). Pjesme bez omota, radio, Spotify i YouTube zadržavaju NLO logo.
- Bot za Discord: glasnoća na Discordu može pratiti player (kao do sada) ili imati vlastitu razinu, pa kad sebi utišaš player, u kanalu više nije tiše svima (prozor Discord).
- Čuvar pokretanja: ako se player ikad pokrene bez vidljivog prozora, glavni prozor se sam vraća, a sljedeće pokretanje ga otvara normalno, na sredini zaslona (uz kratku poruku).

### Poboljšano

- Ponovno pokretanje playera dok je otvoren mini player dovodi mini player u prvi plan.

### Ispravljeno

- Pjesme s ugrađenim omotom (ili s cover.jpg uz njih) tretirale su se kao video: početni ekran ih je zatvarao, bot za Discord slao je tišinu, a za njih se mogla pokrenuti i pretraga titlova. Omot je sada samo omot, a takve pjesme se ponašaju kao sva ostala glazba.
- Ponovno pokretanje dok otvoreni player nije odgovarao nije radilo ništa; novo pokretanje se sada otvara kao zaseban player.
- Puni tekstovi licenci za .NET i WPF, Velopack, Microsoft.Data.Sqlite, SQLitePCLRaw, SQLite, WebRtcVadSharp/WebRTC i WebView2 SDK sada su uz program (mapa licenses), ne samo poveznica.
- Svijetla tema: zelena boja isticanja je malo tamnija, pa je bijeli tekst na njoj čitljiviji (kontrast 4,6:1, prije 4,4:1).

### Poznati problemi

- Utišan player (ili glasnoća 0) i uz vlastitu glasnoću za Discord šalje tišinu: bot može slati samo ono što player svira.

Izdanje: [v2.1.13](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.1.13) · Promjene od [v2.1.9...v2.1.13](https://github.com/jebaitedcro7/jebaited_player/compare/v2.1.9...v2.1.13)

## [2.1.9] - 2026-11-22

Glasnoća kotačićem bilo gdje na playeru, traka preuzimanja nadogradnje, ikone uz podatke o vremenu i jasnija postavka sljedeće epizode.

### Novo

- Preuzimanje nadogradnje: player pokazuje koja se verzija preuzima, koliko je velika i traku napretka s postotkom i laganom animacijom (Postavke → Nadogradnje), uz obavijest kad preuzimanje krene.
- Ikone ispred svakog podatka o vremenu na početnom ekranu (osjećaj temperature, min/maks, kiša, vjetar, vlažnost i ostalo).

### Poboljšano

- Kotačić miša mijenja glasnoću bilo gdje na playeru (video, kontrole, početni ekran, cijeli mini player). Popisi koji se mogu listati i dalje se listaju kotačićem. Ctrl + kotačić i dalje mijenja veličinu mini playera.
- Natpis „Podaci o vremenu: Open-Meteo.com“ više se ne prikazuje na početnom ekranu; izvor je i dalje naveden u postavkama.

### Ispravljeno

- Postavke → Općenito: vrijednost uz „Ponudi sljedeću epizodu prije kraja“ bila je izgurana izvan vidljivog dijela; klizač sada ima svoj redak i prikazuje sekunde.

Izdanje: [v2.1.9](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.1.9) · Promjene od [v2.1.8...v2.1.9](https://github.com/jebaitedcro7/jebaited_player/compare/v2.1.8...v2.1.9)

## [2.1.8] - 2026-11-21

Popravak: player se opet pokreće kad je glavni prozor bio maksimiziran, a zadnji prikaz mini player; kotačić miša mijenja glasnoću brže i glatkije.

### Poboljšano

- Kotačić miša iznad trake glasnoće mijenja glasnoću za 5 % po zarezu (prije 2 %), razmjerno okretanju kotačića, pa fini kotačići i touchpadi rade glatko. Traka u mini playeru odmah prati.

### Ispravljeno

- Player se više ne pokreće bez ijednog prozora kad je glavni prozor bio maksimiziran, a zadnji prikaz je bio mini player.

Izdanje: [v2.1.8](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.1.8) · Promjene od [v2.1.7...v2.1.8](https://github.com/jebaitedcro7/jebaited_player/compare/v2.1.7...v2.1.8)

## [2.1.7] - 2026-11-21

Mini player i početni ekran: pet izgleda mini playera, glasnoća kotačićem miša, pulsirajuća oznaka UŽIVO za radio, glazba svira dalje na početnom ekranu te potpuno prilagodljivo vrijeme, datum i prognoza.

### Novo

- Pet izgleda mini playera (desni klik → Izgled): klasični Winamp, kartica, uska traka, bočna ploča i NLO zaslon. Svi imaju iste funkcije, a tvoj odabir se pamti.
- Kotačić miša iznad trake glasnoće mijenja glasnoću u koracima od 2 % u punom i mini playeru. Kotačić iznad videa ili drugih dijelova playera više ne mijenja glasnoću slučajno.
- Crvena, blago pulsirajuća oznaka UŽIVO dok svira radio: u svakom izgledu mini playera, u punom playeru i na početnom ekranu.
- Naslov pjesme se neprekidno vrti u krug (bez praznine i bez trzaja), a zadano prikazuje samo izvođača i pjesmu. Desni klik na naslov: dodaj album, godinu, žanr, kvalitetu zvuka ili naziv datoteke.
- Traka reprodukcije na početnom ekranu: trenutna pjesma, izvor (lokalno, radio, Spotify, YouTube Music, Discord), Play/Pause, Prethodna/Sljedeća i glasnoća.
- Izgled početnog ekrana (Postavke → Izgled): font, veličina i mjesto vremena, datuma i prognoze, svaki zasebno, uz pregled i „Vrati izgled na zadano”.
- Više podataka o vremenu (Open-Meteo, besplatno, bez ključa): osjećaj temperature, najniža i najviša, vjerojatnost i količina oborina, vjetar, vlažnost, UV indeks, izlazak i zalazak sunca, sljedeći sati i dani. Prikazuje se samo ono što izvor podataka šalje.
- Zasebna tipka Minimiziraj (i stavka u izborniku): player ide na taskbar, a glazba svira dalje; razlikuje se od Mini playera i Zatvaranja.
- Discord bot je u izborniku izvora uz Radio, Spotify i YouTube Music.
- Sljedeća epizoda prije kraja (Postavke → Općenito): biraš koliko sekundi prije kraja epizode se pojavljuje kartica za sljedeću; odbrojavanje tada završava točno na kraju. 0 zadržava staro ponašanje (tek kad epizoda završi).

### Poboljšano

- Mini player i svi ostali prozori (ploča Equalizera, Radio, Discord…) uvijek ostaju potpuno unutar vidljivog dijela zaslona: uzimaju se u obzir taskbar, skaliranje zaslona i više monitora, a prozor koji se makne izvan zaslona vraća se natrag.
- Uska traka sada ima klizač glasnoće.

### Ispravljeno

- Tipka Home više ne zaustavlja glazbu ni radio. Samo se video i dalje zatvara, kao prije.
- Pri pokretanju se otvara samo jedan prozor: mini player ako je to bio zadnji prikaz (ili kad se otvara glazba), inače puni player. Prije se prvo pojavio veliki player, a nakon trenutka i mali.

### Poznati problemi

- Lokacija se bira samo upisom grada; automatsko određivanje lokacije nije uključeno (ništa se ne šalje dok ne odabereš grad).
- Aplikacija još nije digitalno potpisana, pa Windows SmartScreen ili Smart App Control mogu upozoriti prije instalacije.
- Prozor napretka instalacijskog programa dolazi iz Velopacka i samo je na engleskom.

Izdanje: [v2.1.7](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.1.7) · Promjene od [v2.0.6...v2.1.7](https://github.com/jebaitedcro7/jebaited_player/compare/v2.0.6...v2.1.7)

## [2.0.6] - 2026-11-21

2.0: tvoj vlastiti bot za Discord pušta glazbu i radio iz playera u glasovnom kanalu; svaki prečac se može promijeniti; boja isticanja po izboru.

### Novo

- Bot za Discord (Ctrl+Shift+D, gumb sa slušalicama u mini playeru ili desni klik → Bot za Discord…): tvoj vlastiti bot pušta glazbu i radio iz playera u glasovnom kanalu na tvom serveru, pa prijatelji čuju što slušaš. Napravi besplatnog bota u Discord Developer Portalu (koraci su u prozoru), zalijepi njegov token i pozovi ga; „Uđi u moj kanal” pronađe kanal u kojem jesi. Token se sprema kriptiran, samo na ovom računalu. Šalju se samo lokalna glazba i radio: filmovi, YouTube i Spotify nikad se ne šalju (bot šalje tišinu). Glasovna veza je kriptirana s kraja na kraj (Discord DAVE).
- Vlastiti prečaci (Postavke → Prečaci): svaka radnja može imati dvije tipke. Klikni tipku i pritisni novu kombinaciju; ako je već koristi druga radnja, player to javi, a drugi pritisak je premjesti. „Zadano” vraća jednu radnju ili cijelu karticu. F1 i izbornici uvijek pokazuju tvoje stvarne tipke, a tvoje tipke su u izvozu postavki i u kopijama.
- Boja isticanja (Postavke → Izgled → Boja isticanja): osam gotovih boja ili vlastita (nijansa, zasićenost i svjetlina ili #RRGGBB) uz pregled uživo. Prate je gumbi, klizači, označene stavke te zaslon i VU metar mini playera; u svakoj temi boja se po potrebi prilagodi da tekst ostane čitljiv.

### Poboljšano

- Dulje premotavanje (Ctrl+←/→) te fini i grubi pomak titlova (Ctrl+G/H, Shift+G/H) sad su zasebne radnje, pa se i njihove tipke mogu mijenjati.
- Puni tekstovi licenci komponenti bota za Discord (NetCord, Concentus, libdave, OpenSSL, mlspp, nlohmann/json) instaliraju se uz program u mapu licenses, zajedno s THIRD_PARTY_NOTICES.

### Ispravljeno

- Kratki opis privatnosti u Postavkama bio je zastario (spominjao je samo GitHub, titlove i radio); sad navodi svaku uslugu na koju se player može spojiti.
- Na F1 popisu prečaca nedostajali su Ctrl+M, Ctrl+R, Ctrl+Shift+S i Ctrl+Shift+Y; sad nastaje iz istog popisa koji player koristi, pa je uvijek potpun.

### Poznati problemi

- Bot za Discord za slanje zvuka treba Windows 11 (ili Windows 10 build 20348 ili noviji).
- Aplikacija još nije digitalno potpisana, pa Windows SmartScreen ili Smart App Control mogu upozoriti prije instalacije.
- Prozor napretka instalacije je Velopackov i samo je na engleskom.

Izdanje: [v2.0.6](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v2.0.6) · Promjene od [v1.8.4...v2.0.6](https://github.com/jebaitedcro7/jebaited_player/compare/v1.8.4...v2.0.6)

## [1.8.4] - 2026-11-21

Ciklus 6: YouTube Music, vrijeme, datum i prognoza na početnom ekranu te izvještaji o greškama.

### Novo

- YouTube Music (Ctrl+Shift+Y ili oznaka izvora u mini playeru): zalijepi link s YouTubea ili YouTube Musica (pjesma ili playlista) i svira u službenom YouTube playeru u vlastitom prozoru, bez računa i bez ključa. Pretraga traži tvoj besplatni YouTube API ključ (koraci su u prozoru; „API ključ…” ga mijenja ili uklanja). Dok je YouTube izvor, mini player pokazuje YOUTUBE i njegove tipke upravljaju YouTubeom. YouTube player mora ostati vidljiv (pravila YouTubea), pa nema reprodukcije u pozadini.
- Informacije na početnom ekranu: vrijeme i datum gore lijevo te po želji prognoza za grad koji odabereš (Postavke → Izgled; s Open-Meteoa, besplatno i bez računa, najviše svakih 30 minuta, °C ili °F). Bez interneta prikazuje se zadnja poznata prognoza s vremenom ažuriranja.
- Izvještaji o greškama: greške i padovi spremaju se na tvoje računalo kao kratki redigirani izvještaji (bez mapa, naziva videa, imena, ključeva i IP adrese). Postavke → O aplikaciji → „Pošalji preko GitHuba…” otvara pripremljeni issue koji pročitaš prije slanja.

### Poboljšano

- Jedan zvuk u isto vrijeme: kad krene YouTube, lokalna datoteka se pauzira (radio postaja stane), a Spotify više nije izvor; otvaranje datoteke ili postaje pauzira YouTube.

### Poznati problemi

- Aplikacija još nije digitalno potpisana, pa Windows SmartScreen ili Smart App Control mogu upozoriti prije instalacije.
- Prozor napretka instalacije je Velopackov i samo je na engleskom.

Izdanje: [v1.8.4](https://github.com/jebaitedcro7/jebaited_player/releases/tag/v1.8.4) · Promjene od [v1.7.4...v1.8.4](https://github.com/jebaitedcro7/jebaited_player/compare/v1.7.4...v1.8.4)

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
