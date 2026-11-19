<#
.SYNOPSIS
  Testovi ciklusa 3 u pravom playeru:
  - M2a/M2b (testna mapa podataka, nikad tvoja): nespremljeno → Odbaci, Spremi na disk, Zadano za karticu i za sve
    (jezik, glasnoća i ključ ostaju), Odbaci nakon zadanog;
  - V3 Radio (stvarna mreža): pretraga Radio Browsera, stvarni stream u mini playeru (uživo, RADIO, glazbeni EQ),
    favorit, kriv i ručni URL, stream ne ide u povijest.

.EXAMPLE
  .\tools\run-ciklus3-tests.ps1
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$clip = Join-Path $root "testdata\ciklus1\Kratki.Film.2026.mkv"
if (-not (Test-Path $clip)) { throw "Nema testnog klipa: pokreni prvo tools\run-ciklus1-tests.ps1." }

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Test = $Name; Rezultat = $(if ($Ok) { "OK" } else { "GREŠKA" }); Detalji = $Detail })
}
function Read-Ui([string]$Name) {
    $u = (Get-Content -LiteralPath (Join-Path $script:DiagDir "$Name.json") -Raw -Encoding UTF8 | ConvertFrom-Json).uiTest
    if (-not $u -or $u.exception -or $u.error) { throw "$Name`: $($u.exception)$($u.error)" }
    return $u
}

# --- M2a / M2b --------------------------------------------------------------------------------------------------
$data = Join-Path $root "testdata\ciklus3\data"
if (Test-Path $data) { Remove-Item $data -Recurse -Force }
New-Item -ItemType Directory -Force -Path $data | Out-Null
'{ "Volume": 50, "Theme": "light", "Language": "hr", "ResumeMode": "ask", "SeekStepSeconds": 15, "Online": { "OpenSubtitlesApiKey": "test-kljuc" } }' |
    Set-Content -LiteralPath (Join-Path $data "settings.json") -Encoding UTF8
$env:JEBAITED_DATA_DIR = $data
try { $null = Invoke-Diag -Name "ciklus3_settings" -File $clip -Seconds 1 -ExtraArgs "--diag-ui-test settings3" -TimeoutSeconds 180 }
finally { Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue }
$s = Read-Ui "ciklus3_settings"
Check "M2a: na početku sve spremljeno" (-not $s.start.dirty -and -not $s.start.saveEnabled) "'$($s.start.status)'"
Check "M2a: promjena = nespremljeno" ($s.changed.dirty -and $s.changed.saveEnabled -and $s.changed.status -ne $s.start.status) "'$($s.changed.status)'"
Check "M2a: Odbaci promjene vraća spremljeno" ($s.discarded.resume -eq $s.discarded.original -and -not $s.discarded.dirty -and $s.discarded.newWindow) "ResumeMode $($s.discarded.resume)"
Check "M2a: Spremi zapisuje na disk" ($s.saved.fileHasResume -and -not $s.saved.dirty) "datoteka $($s.saved.fileHasResume), '$($s.saved.status)'"
Check "M2b: Zadano za karticu Općenito" ($s.defaultsTab.resume -eq "ask" -and $s.defaultsTab.theme -eq "light" -and $s.defaultsTab.dirty) "ResumeMode $($s.defaultsTab.resume), tema $($s.defaultsTab.theme)"
$d = $s.defaultsAll
Check "M2b: Zadano za sve (jezik, glasnoća, ključ ostaju)" ($d.theme -eq "dark" -and $d.appliedTheme -eq "dark" -and [int]$d.seek -eq 5 -and $d.language -eq "hr" -and [double]$d.volume -eq 50 -and $d.key -eq "test-kljuc" -and $d.dirty) "tema $($d.theme), korak $($d.seek), jezik $($d.language), glasnoća $($d.volume), ključ '$($d.key)'"
Check "M2a: Odbaci nakon zadanog" ($s.afterDiscard.resume -eq "resume" -and $s.afterDiscard.theme -eq "light" -and -not $s.afterDiscard.dirty) "ResumeMode $($s.afterDiscard.resume), tema $($s.afterDiscard.theme)"

# --- V3 Radio (stvarna mreža) -----------------------------------------------------------------------------------
$db = Join-Path $script:DiagDir "ciklus3-radio.db"
Remove-Item "$db*" -ErrorAction SilentlyContinue
$null = Invoke-Diag -Name "ciklus3_radio" -File $clip -Seconds 1 -ExtraArgs "--diag-ui-test radio --diag-db `"$db`"" -TimeoutSeconds 240
$r = Read-Ui "ciklus3_radio"
Check "V3: pretraga Radio Browsera (jazz)" ([int]$r.search.count -gt 5) "$($r.search.count) postaja: $($r.search.first -join ' / ')"
$st = $r.stream
Check "V3: stvarni stream svira" ($st.station -and [double]$st.position -gt 2 -and -not $st.paused) "'$($st.station)', $($st.position) s"
Check "V3: mini player, RADIO, uživo, glazbeni EQ" ($st.miniVisible -and -not $st.mainVisible -and $st.source -eq "RADIO" -and $st.live -match "UŽIVO|LIVE" -and $st.musicMode) "izvor '$($st.source)', '$($st.live)', glazba $($st.musicMode)"
Check "V3: naslov = postaja (+ pjesma ako je šalje)" ($st.title -and $st.title.StartsWith([string]$st.station)) "'$($st.title)' (icy: '$($st.icy)')"
$fb = $r.feedback
Check "1.5.1: gumb ▶ u retku pokreće postaju" ($r.clickedPlayButton -and $st.station) "klik $($r.clickedPlayButton), '$($st.station)'"
Check "1.5.1: natpis Svira, zeleni redak, prozor Radio ostaje" ($fb.status -match "Svira|Playing" -and $fb.rowPlaying -and $fb.playGlyph -and $fb.radioVisible) "'$($fb.status)', redak $($fb.rowPlaying), vidljiv $($fb.radioVisible)"
Check "V3: stream nije u popisu ni povijesti" (-not $st.libraryCurrent -and -not $r.historyHasUrls) "popis '$($st.libraryCurrent)', povijest s URL-om $($r.historyHasUrls)"
Check "V3: favorit" ([int]$r.favorite.rows -ge 1 -and $r.favorite.inDb -and $r.favorite.star -and -not $r.favoriteRemoved) "redaka $($r.favorite.rows), u bazi $($r.favorite.inDb), uklonjen $(-not $r.favoriteRemoved)"
Check "V3: kriv URL odbijen, ručni URL svira" (-not $r.badUrl -and $r.manualUrl.accepted -and $r.manualUrl.path -and [double]$r.manualUrl.position -ge 1.5) "kriv $($r.badUrl), ručni $($r.manualUrl.accepted), $($r.manualUrl.position) s"

# --- 1.5.2: pokretanje bez datoteke, odmah mini player, radio kao prvo što se pusti ---------------------------------
$dbc = Join-Path $script:DiagDir "ciklus3-radiocold.db"
Remove-Item "$dbc*" -ErrorAction SilentlyContinue
$null = Invoke-Diag -Name "ciklus3_radiocold" -File @() -Seconds 1 -ExtraArgs "--diag-ui-test radiocold --diag-db `"$dbc`"" -TimeoutSeconds 240
$c = Read-Ui "ciklus3_radiocold"
Check "1.5.2: bez datoteke, mini player, radio svira" (-not $c.playerBefore -and $c.playerAfterMini -and $c.clicked -and $c.station -and [double]$c.position -gt 2 -and $c.miniVisible) "mpv prije $($c.playerBefore) → poslije mini $($c.playerAfterMini), '$($c.station)' $($c.position) s, '$($c.status)'"

$results | Format-Table -AutoSize -Wrap
$failed = @($results | Where-Object { $_.Rezultat -ne "OK" }).Count
Write-Host ("Ukupno {0}, uspješno {1}, grešaka {2}" -f $results.Count, ($results.Count - $failed), $failed)
if ($failed -gt 0) { exit 1 }
