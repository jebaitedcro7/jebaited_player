<#
.SYNOPSIS
  Testovi M3 u pravom playeru: mapa serije, nastavak gledanja (s pitanjem), sljedeća epizoda,
  pamćenje titlova po videu i "Nastavi gledati". Koristi zasebnu testnu bazu (ne dira stvarnu povijest).
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$m3 = Join-Path $root "testdata\m3"
$db = Join-Path $script:DiagDir "m3-test.db"
Remove-Item "$db*" -ErrorAction SilentlyContinue

$series = Join-Path $m3 "Serija.Test"
$e1 = Join-Path $m3 "Serija.Test\Season 01\Serija.Test.S01E01.Pilot.720p.WEB-DL.x264-GRP.mp4"
$e2 = Join-Path $m3 "Serija.Test\Season 01\Serija.Test.S01E02.Drugi.Dan.720p.WEB-DL.x264-GRP.mp4"
$e3 = Join-Path $m3 "Serija.Test\Season 01\Serija.Test.S01E03.Kraj.720p.WEB-DL.x264-GRP.mp4"
$film = Join-Path $m3 "Filmovi\Film.Za.Test.2026.1080p.BluRay.x264-GRP.mp4"
$sync = Join-Path $root "testdata\m2\sync.srt"
$embedded = Join-Path $root "testdata\m1\subs_zh-default_en_hr.mkv"
$embedded2 = Join-Path $root "testdata\m1\subs_zh-default_en.mkv"

function Run([string]$Name, [string[]]$Files, [string]$Extra, [int]$Seconds = 1) {
    $null = Invoke-Diag -Name ("LIB " + $Name) -File $Files -Seconds $Seconds -ExtraArgs "--diag-db `"$db`" $Extra" -TimeoutSeconds 90
    $json = Join-Path $script:DiagDir ((("LIB " + $Name) -replace '[^\w\-]+', '_') + ".json")
    if (Test-Path -LiteralPath $json) { return Get-Content -LiteralPath $json -Raw -Encoding UTF8 | ConvertFrom-Json }
    return $null
}

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Rezultat = $(if ($Ok) { "OK" } else { "GREŠKA" }); Test = $Name; Detalji = $Detail })
}

# 1) Mapa serije (prazna baza): redoslijed, bez "sample", kreće od prve epizode.
$r = Run "Mapa serije" @($series) ""
$list = @($r.library.playlist)
$expected = @("S01E01 · Pilot", "S01E02 · Drugi Dan", "S01E03 · Kraj", "S02E01")
$labels = $list | ForEach-Object { ($_ -replace '^[\* ][✓ ] ', '' -replace ' \[.*$', '').Trim() }
Check "Mapa serije: redoslijed i bez sample datoteke" (($labels -join '|') -eq ($expected -join '|')) ($labels -join ' | ')
Check "Mapa serije: kreće od prve epizode" ($r.library.currentIndex -eq 0) "indeks $($r.library.currentIndex), naslov '$($r.library.windowTitle)'"

# 2) Gledanje do 40. sekunde pa zatvaranje (pozicija se sprema pri zatvaranju).
$r = Run "Gledanje do 40 s" @($e1) "--diag-probe 40"
Check "Spremanje pozicije" ($null -ne $r) "zaustavljeno na $($r.subtitleProbe.'time-pos') s"

# 3) Ponovno otvaranje: pitanje + "Nastavi".
$r = Run "Nastavi" @($e1) "--diag-resume continue"
$ok = $r.resume.offered -ge 39 -and $r.resume.offered -le 41 -and $r.resume.pausedWhileAsking -and -not $r.resume.pausedAfter -and $r.resume.positionAfter -ge 40
Check "Pitanje pa Nastavi" $ok "ponuđeno $($r.resume.offerText); dok pita pauzirano=$($r.resume.pausedWhileAsking), na $($r.resume.positionWhileAsking) s; nakon izbora $($r.resume.positionAfter) s, pauzirano=$($r.resume.pausedAfter)"

# 4) Mapa serije sad kreće od započete epizode (i opet pita).
$r = Run "Mapa nakon gledanja" @($series) "--diag-resume continue"
Check "Mapa: kreće od započete epizode i pita" (($r.library.currentIndex -eq 0) -and ($r.resume.offered -ge 40)) "indeks $($r.library.currentIndex), $($r.resume.offerText)"

# 5) Ponovno otvaranje: pitanje + "Od početka".
$r = Run "Od pocetka" @($e1) "--diag-resume start"
Check "Pitanje pa Od početka" (($r.resume.offered -ge 40) -and ($r.resume.positionAfter -lt 5) -and -not $r.resume.pausedAfter) "ponuđeno $($r.resume.offerText); nakon izbora $($r.resume.positionAfter) s"

# 6) Nakon "od početka" (pozicija < 30 s) ne pita više.
$r = Run "Bez pitanja" @($e1) "--diag-resume continue"
Check "Kratko gledanje: bez pitanja" (-not $r.resume.offered) "ponuđeno: '$($r.resume.offered)'"

# 7) Sljedeća epizoda (kartica + odbrojavanje 2 s).
$r = Run "Sljedeca epizoda" @($e2) "--diag-upnext 2"
Check "Sljedeća epizoda: kartica i prelazak" ($r.upNext.sawCard -and $r.upNext.to -like "*S01E03*") "kartica '$($r.upNext.cardTitle)', iz $($r.upNext.from) u $($r.upNext.to)"

# 8) Sljedeća epizoda preko sezone (S01E03 → S02E01, otvorena samo jedna datoteka).
$r = Run "Nova sezona" @($e3) "--diag-upnext 2"
Check "Sljedeća epizoda u novoj sezoni" ($r.upNext.to -like "*S02E01*") "iz $($r.upNext.from) u $($r.upNext.to)"

# 9) Titlovi i sinkronizacija zapamćeni po filmu (vanjski titl, pomak -2 s).
$null = Run "Titlovi A" @($film, $sync) "--diag-probe 6.5 --diag-sub-delay -2000"
$r = Run "Titlovi B" @($film) "--diag-probe 6.5"
Check "Vanjski titl i pomak zapamćeni" (($r.subtitleProbe.'sub-text' -eq "Replika 4") -and ([double]$r.subtitleProbe.'sub-delay' -eq -2)) "prikazano '$($r.subtitleProbe.'sub-text')', pomak $($r.subtitleProbe.'sub-delay') s"

# 10) Ugrađeni titl zapamćen (korisnik odabrao engleski umjesto hrvatskog).
$null = Run "Ugradeni A" @($embedded) "--diag-select-sub en"
$r = Run "Ugradeni B" @($embedded) "--diag-probe 2"
Check "Ugrađeni izbor zapamćen (EN umjesto HR)" ($r.subtitleProbe.'sub-text' -eq "English subtitle") "prikazano '$($r.subtitleProbe.'sub-text')'"

# 11) "Bez titlova" zapamćeno.
$null = Run "Bez titlova A" @($embedded2) "--diag-select-sub none"
$r = Run "Bez titlova B" @($embedded2) "--diag-probe 2"
Check "Izbor 'bez titlova' zapamćen" ([string]::IsNullOrEmpty($r.subtitleProbe.'sub-text')) "prikazano '$($r.subtitleProbe.'sub-text')'"

# 12) Početni ekran: "Nastavi gledati".
$r = Run "Pocetni ekran" @() "" 2
$recent = @($r.library.recent)
Check "Nastavi gledati" ($recent.Count -ge 3) ($recent -join ' || ')

$results
"{0} / {1} prošlo" -f @($results | Where-Object Rezultat -eq "OK").Count, $results.Count
