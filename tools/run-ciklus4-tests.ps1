<#
.SYNOPSIS
  Testovi ciklusa 4 u pravom playeru:
  - M3a (testna mapa podataka): oštećen settings.json se čuva sa strane, postavke se vraćaju iz automatske kopije;
  - M3b (testna mapa podataka): Zatvori s nespremljenim promjenama pita Spremi / Odbaci / Odustani;
  - V4 priprema: glazba i radio na zasebnom izlazu (npr. virtualni kabel za Discord), film na glavnom;
  - R1 radio (stvarna mreža): Stop zatvara vezu i pamti postaju, Play / razmak spajaju ponovno uživo,
    Pauza → Play spaja ponovno, premotavanje se ignorira, Home briše zapamćenu postaju;
  - R1 radio (bez mreže): lokalni testni stream prekine vezu, Play spaja ponovno; Stop kod datoteke i dalje
    pauzira na početku; mali cache samo za stream.

.EXAMPLE
  .\tools\run-ciklus4-tests.ps1
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$mp3 = Join-Path $root "testdata\m1\audio_only.mp3"
if (-not (Test-Path $mp3)) { throw "Nema testnog MP3-a: pokreni prvo tools\make-testdata.ps1." }

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Test = $Name; Rezultat = $(if ($Ok) { "OK" } else { "GREŠKA" }); Detalji = $Detail })
}
function Read-Ui([string]$Name) {
    $u = (Get-Content -LiteralPath (Join-Path $script:DiagDir "$Name.json") -Raw -Encoding UTF8 | ConvertFrom-Json).uiTest
    if (-not $u -or $u.exception -or $u.error) { throw "$Name`: $($u.exception)$($u.error)" }
    return $u
}
function Is-Default([string]$Value, [long]$Bytes) { return $Value -eq "$Bytes" -or $Value -match "^$([math]::Round($Bytes / 1MB))\s*MiB$" }

# --- M3a: oštećen settings.json u pravom playeru (testna mapa podataka, nikad tvoja) ------------------------------
$data = Join-Path $root "testdata\ciklus4\data"
if (Test-Path $data) { Remove-Item $data -Recurse -Force }
$stamp = (Get-Date).ToUniversalTime().AddHours(-2).ToString("yyyyMMdd-HHmmss")
$backup = Join-Path $data "backups\$($stamp)_start"
New-Item -ItemType Directory -Force -Path $backup | Out-Null
$good = '{ "Volume": 37, "Theme": "light", "Language": "hr", "SeekStepSeconds": 15 }'
$damaged = '{ "Volume": 37, "Theme": "li'
[IO.File]::WriteAllText((Join-Path $backup "settings.json"), $good)
[IO.File]::WriteAllText((Join-Path $data "settings.json"), $damaged)
$env:JEBAITED_DATA_DIR = $data
try { $null = Invoke-Diag -Name "ciklus4_settings" -File $mp3 -Seconds 1 -ExtraArgs "--diag-ui-test settings4" -TimeoutSeconds 120 }
finally { Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue }
$m = Read-Ui "ciklus4_settings"
Check "M3a: oštećene postavke vraćene iz automatske kopije" ($m.outcome -eq "RestoredFromBackup" -and [double]$m.volume -eq 37 -and $m.theme -eq "light" -and $m.appliedTheme -eq "light" -and [int]$m.seek -eq 15) "$($m.outcome), glasnoća $($m.volume), tema $($m.theme)/$($m.appliedTheme), korak $($m.seek)"
Check "M3a: oštećena datoteka sačuvana, kopija na mjestu settings.json" ($m.corruptContent -eq $damaged -and $m.fileRestored -eq $good -and $m.corruptFile -like "settings.corrupt-*.json") "$($m.corruptFile)"
Check "M3a: poruka korisniku" ($m.message -match "oštećena" -and $m.message.Contains([string]$m.corruptFile)) "'$($m.message)'"
Check "M3a: spremanje zapisuje oznaku oblika, oštećena ostaje" ([int]$m.savedVersion -eq 2 -and [int]$m.corruptFiles -eq 1) "SettingsVersion $($m.savedVersion), oštećenih $($m.corruptFiles)"

# --- M3b: X / Zatvori s nespremljenim promjenama (testna mapa podataka) -------------------------------------------
$data2 = Join-Path $root "testdata\ciklus4\data-m3b"
if (Test-Path $data2) { Remove-Item $data2 -Recurse -Force }
New-Item -ItemType Directory -Force -Path $data2 | Out-Null
[IO.File]::WriteAllText((Join-Path $data2 "settings.json"), '{ "Volume": 50, "Language": "hr", "ResumeMode": "ask" }')
$env:JEBAITED_DATA_DIR = $data2
try { $null = Invoke-Diag -Name "ciklus4_unsaved" -File $mp3 -Seconds 1 -ExtraArgs "--diag-ui-test unsaved" -TimeoutSeconds 120 }
finally { Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue }
$u = Read-Ui "ciklus4_unsaved"
Check "M3b: Odustani ostavlja prozor otvoren" ([int]$u.cancel.asked -eq 1 -and $u.cancel.open -and $u.cancel.dirty -and $u.cancel.resume -eq "start") "pitano $($u.cancel.asked), otvoren $($u.cancel.open), nespremljeno $($u.cancel.dirty)"
Check "M3b: Odbaci zatvara i vraća spremljeno" ([int]$u.discard.asked -eq 2 -and $u.discard.closed -and $u.discard.resume -eq $u.discard.original -and $u.discard.file -eq "ask") "ResumeMode $($u.discard.resume), u datoteci $($u.discard.file)"
Check "M3b: Spremi zatvara i zapisuje na disk" ([int]$u.save.asked -eq 3 -and $u.save.closed -and $u.save.resume -eq "resume" -and $u.save.file -eq "resume") "ResumeMode $($u.save.resume), u datoteci $($u.save.file)"
Check "M3b: bez promjena zatvaranje ne pita" ([int]$u.clean.asked -eq 0 -and $u.clean.closed) "pitano $($u.clean.asked), zatvoren $($u.clean.closed)"
$dg = $u.dialog
Check "M3b: prozor s pitanjem (Spremi / Odbaci promjene / Odustani)" ($dg.save -eq "Spremi" -and $dg.discard -match "Odbaci" -and $dg.cancel -eq "Odustani" -and $dg.saveIsDefault -and $dg.cancelIsCancel -and $u.dialogChoice -eq "Discard" -and $u.dialogClosed) "'$($dg.title)': $($dg.save) / $($dg.discard) / $($dg.cancel), klik → $($u.dialogChoice)"

# --- V4 priprema: zaseban izlaz za glazbu i radio (stvarni uređaj, utišano, postavke se ne spremaju) -----------------
$null = Invoke-Diag -Name "ciklus4_musicdevice" -File $mp3 -Seconds 1 -ExtraArgs "--diag-ui-test musicdevice" -TimeoutSeconds 120
$v = Read-Ui "ciklus4_musicdevice"
if (-not $v.device) {
    Check "V4: zaseban izlaz za glazbu" $false "nema izlaznog uređaja osim zadanog ($($v.devices))"
} else {
    Check "V4: postavka u prozoru (Isti kao za filmove → uređaj)" ($v.buttonBefore -eq "Isti kao za filmove" -and $v.buttonAfter -eq $v.device) "'$($v.buttonBefore)' → '$($v.buttonAfter)'"
    Check "V4: glazba svira na zasebnom izlazu" ($v.musicPlaying.musicMode -and $v.musicPlaying.mpv -eq $v.musicAgain.expected) "mpv '$($v.musicPlaying.mpv)'"
    Check "V4: film ostaje na glavnom izlazu" (-not $v.video.musicMode -and $v.video.mpv -eq $v.video.main) "mpv '$($v.video.mpv)' (glavni '$($v.video.main)')"
    Check "V4: natrag na glazbu = opet zaseban izlaz" ($v.musicAgain.musicMode -and $v.musicAgain.mpv -eq $v.musicAgain.expected) "mpv '$($v.musicAgain.mpv)'"
}

# --- R1 bez mreže: lokalni stream koji prekine vezu, Stop kod datoteke -------------------------------------------
$dbd = Join-Path $script:DiagDir "ciklus4-radiodrop.db"
Remove-Item "$dbd*" -ErrorAction SilentlyContinue
$null = Invoke-Diag -Name "ciklus4_radiodrop" -File $mp3 -Seconds 1 -ExtraArgs "--diag-ui-test radiodrop --diag-db `"$dbd`"" -TimeoutSeconds 180
$d = Read-Ui "ciklus4_radiodrop"
$f = $d.fileStop
Check "R1: Stop kod datoteke = pauza na početku" ($f.paused -and $f.hasMedia -and [double]$f.position -lt 0.5 -and -not $f.stopped) "s $($f.before) s na $($f.position) s, pauza $($f.paused)"
Check "R1: datoteka ima zadani cache mpv-a" ((Is-Default $f.maxBytes 150MB) -and (Is-Default $f.maxBackBytes 50MB)) "naprijed $($f.maxBytes), unatrag $($f.maxBackBytes)"
$s = $d.stream
Check "R1: lokalni stream svira, mali cache" ($s.live -and [double]$s.position -ge 1 -and (Is-Default $s.maxBytes 4MB) -and (Is-Default $s.maxBackBytes 1MB)) "$($s.position) s, naprijed $($s.maxBytes), unatrag $($s.maxBackBytes)"
$e = $d.ended
Check "R1: prekid veze = kraj streama" ($e.eof -and $e.hasMedia) "kraj $($e.eof), pauza $($e.paused), $($e.position) s"
$p = $d.replay
Check "R1: Play na kraju streama spaja ponovno" ([int]$p.loads -eq 1 -and [int]$p.connections -ge 1 -and -not $p.eof -and -not $p.paused) "učitavanja $($p.loads), novih veza $($p.connections), $($p.position) s"
$b = $d.backToFile
Check "R1: natrag na datoteku = zadani cache" ($b.path -and (Is-Default $b.maxBytes 150MB) -and (Is-Default $b.maxBackBytes 50MB)) "naprijed $($b.maxBytes), unatrag $($b.maxBackBytes)"

# --- R1 stvarna mreža: prava postaja ---------------------------------------------------------------------------
$dbs = Join-Path $script:DiagDir "ciklus4-radiostop.db"
Remove-Item "$dbs*" -ErrorAction SilentlyContinue
$null = Invoke-Diag -Name "ciklus4_radiostop" -File @() -Seconds 1 -ExtraArgs "--diag-ui-test radiostop --diag-db `"$dbs`"" -TimeoutSeconds 300
$r = Read-Ui "ciklus4_radiostop"
Check "R1: prava postaja svira (bez datoteke, mini player)" ([bool]$r.station) "'$($r.station)' od $($r.count) postaja"
Check "R1: mali cache za postaju" ((Is-Default $r.cache.maxBytes 4MB) -and (Is-Default $r.cache.maxBackBytes 1MB)) "naprijed $($r.cache.maxBytes), unatrag $($r.cache.maxBackBytes)"
$st = $r.stop
Check "R1: Stop zatvara vezu i pamti postaju" (-not $st.hasMedia -and $st.idle -eq "yes" -and $st.stopped -eq $r.station) "svirala $($st.positionBefore) s, mpv u mirovanju '$($st.idle)', postaja '$($st.stopped)'"
Check "R1: mini player prikazuje zaustavljenu postaju" ($st.marquee -match "ZAUSTAVLJENO|STOPPED" -and $st.compact -match "ZAUSTAVLJENO|STOPPED" -and -not $st.remaining -and $st.marquee.StartsWith([string]$r.station) -and $st.source -eq "RADIO") "'$($st.marquee)', uska traka '$($st.compact)', izvor '$($st.source)'"
$pl = $r.play
Check "R1: Play nakon Stopa = nova veza, uživo" ([int]$pl.loads -eq 1 -and $pl.hasMedia -and $pl.samePath -and -not $pl.paused -and $pl.live -match "UŽIVO|LIVE" -and $pl.stoppedCleared) "učitavanja $($pl.loads), $($pl.position) s, '$($pl.live)'"
Check "R1: Pauza → Play = nova veza" ($r.pause.paused -and $r.pause.hasMedia -and [int]$r.pausePlay.loads -eq 1 -and -not $r.pausePlay.paused) "pauza $($r.pause.paused), učitavanja $($r.pausePlay.loads), $($r.pausePlay.position) s"
Check "R1: radio se ne premotava (Home, strelica, izravno)" ([double]$r.seek.after -ge [double]$r.seek.before) "prije $($r.seek.before) s, poslije $($r.seek.after) s"
$sp = $r.space
Check "R1: razmak nakon Stopa spaja ponovno" ($sp.handled -and [int]$sp.loads -eq 1 -and $sp.hasMedia) "obrađeno $($sp.handled), učitavanja $($sp.loads), $($sp.position) s"
$h = $r.home
Check "R1: Home briše zapamćenu postaju" ($h.hadStopped -and $h.cleared -and -not $h.spaceHandled -and [int]$h.loads -eq 0 -and -not $h.hasMedia) "bila $($h.hadStopped), obrisana $($h.cleared), razmak $($h.spaceHandled)"

$results | Format-Table -AutoSize -Wrap
$failed = @($results | Where-Object { $_.Rezultat -ne "OK" }).Count
Write-Host ("Ukupno {0}, uspješno {1}, grešaka {2}" -f $results.Count, ($results.Count - $failed), $failed)
if ($failed -gt 0) { exit 1 }
