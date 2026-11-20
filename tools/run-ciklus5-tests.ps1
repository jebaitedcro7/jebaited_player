<#
.SYNOPSIS
  Testovi ciklusa 5 u pravom playeru:
  - N2 (testna baza, serija iz testdata\m3): sljedeća epizoda na Home kartici, klik, nakon gledanja dalje, ručno, ▲ ▼, ×;
  - V8 Spotify daljinski (simulirani Spotify, testna mapa): vodič za Client ID, pjesma, playliste, pauza/play, mini player, greške;
  - N4 (testna mapa podataka i testne slike): pozadina pri pokretanju, slike raznih veličina i omjera, Ispuni / Prilagodi /
    Sredina, nepostojeća slika i datoteka koja nije slika, postavke (pregled, Zadana, Odbaci) i vrijeme pokretanja.

.EXAMPLE
  .\tools\run-ciklus5-tests.ps1
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Test = $Name; Rezultat = $(if ($Ok) { "OK" } else { "GREŠKA" }); Detalji = $Detail })
}
function Read-Ui([string]$Name) {
    $u = (Get-Content -LiteralPath (Join-Path $script:DiagDir "$Name.json") -Raw -Encoding UTF8 | ConvertFrom-Json).uiTest
    if (-not $u -or $u.exception -or $u.error) { throw "$Name`: $($u.exception)$($u.error)" }
    return $u
}

# --- Testne slike (System.Drawing; ništa se ne instalira) ----------------------------------------------------------
Add-Type -AssemblyName System.Drawing
$images = Join-Path $root "testdata\ciklus5\images"
New-Item -ItemType Directory -Force -Path $images | Out-Null
function New-TestImage([string]$Name, [int]$W, [int]$H, [System.Drawing.Imaging.ImageFormat]$Format) {
    $bmp = New-Object System.Drawing.Bitmap $W, $H
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $rect = New-Object System.Drawing.Rectangle 0, 0, $W, $H
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 20, 90, 160)), ([System.Drawing.Color]::FromArgb(255, 230, 150, 40)), 35.0
    $g.FillRectangle($brush, $rect)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(2, $W / 200))
    $g.DrawRectangle($pen, 1, 1, $W - 3, $H - 3)
    $g.DrawLine($pen, 0, 0, $W, $H); $g.DrawLine($pen, $W, 0, 0, $H)
    $font = New-Object System.Drawing.Font "Segoe UI", ([Math]::Max(10, $H / 12)), ([System.Drawing.FontStyle]::Bold)
    $g.DrawString("$W x $H", $font, [System.Drawing.Brushes]::White, ($W / 20), ($H / 20))
    $g.Dispose()
    $bmp.Save((Join-Path $images $Name), $Format)
    $bmp.Dispose()
}
New-TestImage "wide-3840x2160.jpg" 3840 2160 ([System.Drawing.Imaging.ImageFormat]::Jpeg)
New-TestImage "tall-800x1200.png" 800 1200 ([System.Drawing.Imaging.ImageFormat]::Png)
New-TestImage "small-300x200.png" 300 200 ([System.Drawing.Imaging.ImageFormat]::Png)
[IO.File]::WriteAllText((Join-Path $images "not-an-image.jpg"), "nije slika")
$wide = Join-Path $images "wide-3840x2160.jpg"

# --- N4 u pravom playeru --------------------------------------------------------------------------------------------
function New-Data([string]$Name, [string]$Json) {
    $d = Join-Path $root "testdata\ciklus5\$Name"
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $d | Out-Null
    [IO.File]::WriteAllText((Join-Path $d "settings.json"), $Json)
    return $d
}
$withBg = '{ "Language": "hr", "HomeBackgroundImage": ' + (ConvertTo-Json $wide) + ', "HomeBackgroundMode": "fill" }'
$data = New-Data "data-homebg" $withBg
$env:JEBAITED_DATA_DIR = $data; $env:JEBAITED_TEST_IMAGES = $images
try { $null = Invoke-Diag -Name "ciklus5_homebg" -File @() -Seconds 1 -ExtraArgs "--diag-ui-test homebg" -TimeoutSeconds 120 }
finally { Remove-Item Env:JEBAITED_DATA_DIR, Env:JEBAITED_TEST_IMAGES -ErrorAction SilentlyContinue }
$h = Read-Ui "ciklus5_homebg"
$screen = [int]$h.screenPx
Check "N4: pozadina iz postavki pri pokretanju" ($h.start.shown -and -not $h.start.space -and [int]$h.start.pixelWidth -le $screen) "učitano za $($h.start.ms) ms, $($h.start.pixelWidth) px (zaslon $screen px)"
$cases = @($h.cases)
$bad = @($cases | Where-Object { -not $_.shown -or $_.space })
Check "N4: sve slike i načini prikazani (3 × 3)" ($cases.Count -eq 9 -and $bad.Count -eq 0) "prikazano $($cases.Count - $bad.Count)/$($cases.Count)"
$w = $cases | Where-Object { $_.image -eq "wide-3840x2160.jpg" -and $_.mode -eq "fill" }
Check "N4: velika slika dekodirana u širini zaslona" ([int]$w.pixelWidth -le $screen -and [int]$w.pixelWidth -ge 1000) "3840 → $($w.pixelWidth) px, $($w.ms) ms"
$s = $cases | Where-Object { $_.image -eq "small-300x200.png" -and $_.mode -eq "center" }
Check "N4: mala slika se ne povećava (Sredina = stvarna veličina)" ([int]$s.pixelWidth -eq 300 -and [double]$s.width -lt [double]$s.panelWidth) "$($s.pixelWidth) px, prikaz $($s.width) × $($s.height) u $($s.panelWidth) × $($s.panelHeight)"
$fill = @($cases | Where-Object { $_.mode -eq "fill" -and ($_.stretch -ne "UniformToFill" -or [double]$_.width -lt [double]$_.panelWidth - 1 -or [double]$_.height -lt [double]$_.panelHeight - 1) })
$fit = @($cases | Where-Object { $_.mode -eq "fit" -and ($_.stretch -ne "Uniform" -or [double]$_.width -gt [double]$_.panelWidth + 1 -or [double]$_.height -gt [double]$_.panelHeight + 1) })
Check "N4: Ispuni pokriva, Prilagodi stane u ekran" ($fill.Count -eq 0 -and $fit.Count -eq 0) "Ispuni krivih $($fill.Count), Prilagodi krivih $($fit.Count)"
$m = $h.missing
Check "N4: nepostojeća slika = svemir + obavijest, postavka ostaje" ($m.space -and -not $m.shown -and $m.notice -match "nije pronađena" -and $m.settingKept) "'$($m.notice)'"
Check "N4: datoteka koja nije slika = svemir" ([bool]$h.notImage.space) "svemir $($h.notImage.space)"
$st = $h.settings
Check "N4: postavke (odabir, pregled, odmah na Home)" ($h.settingsDefaultChecked -and $st.customChecked -and $st.preview -and $st.homeShown -and $st.dirty -and $st.modesEnabled -and $st.file -eq "wide-3840x2160.jpg" -and $h.modeFromSettings -eq "Uniform") "datoteka '$($st.file)', nespremljeno $($st.dirty), način iz postavki $($h.modeFromSettings)"
Check "N4: Odbaci promjene vraća zadanu pozadinu" (-not $h.discard.image -and $h.discard.space -and $h.discard.mode -eq "fill") "slika '$($h.discard.image)', svemir $($h.discard.space)"

# --- N2: Sljedeće za gledanje (testna serija iz testdata\m3, testna baza) -----------------------------------------
$e1 = Join-Path $root "testdata\m3\Serija.Test\Season 01\Serija.Test.S01E01.Pilot.720p.WEB-DL.x264-GRP.mp4"
if (-not (Test-Path $e1)) { throw "Nema testne serije: pokreni prvo tools\run-library-tests.ps1." }
$dbn = Join-Path $script:DiagDir "ciklus5-watchnext.db"
Remove-Item "$dbn*" -ErrorAction SilentlyContinue
$null = Invoke-Diag -Name "ciklus5_watchnext" -File $e1 -Seconds 1 -ExtraArgs "--diag-ui-test watchnext --diag-db `"$dbn`"" -TimeoutSeconds 180
$n = Read-Ui "ciklus5_watchnext"
Check "N2: pogledana epizoda → sljedeća na Home kartici" ($n.afterE01.cardVisible -and @($n.afterE01.items).Count -eq 1 -and $n.afterE01.items[0] -match "S01E02.*\[auto\]") "$(@($n.afterE01.items) -join ' | ')"
Check "N2: klik na stavku pokreće epizodu" ($n.clickedOpen -and $n.opened) "klik $($n.clickedOpen), otvoreno $($n.opened)"
Check "N2: nakon gledanja lista ide dalje (E03)" (@($n.afterE02).Count -eq 1 -and $n.afterE02[0] -match "S01E03") "$(@($n.afterE02) -join ' | ')"
Check "N2: ručno dodan film na kraju, bez duplikata" (@($n.withFilm).Count -eq 2 -and $n.withFilm[1] -match "Film Za Test.*\[manual\]") "$(@($n.withFilm) -join ' | ')"
Check "N2: ▲ i ▼ mijenjaju redoslijed (▲ na prvom isključen)" ($n.clickedUp -and $n.afterUp[0] -match "Film" -and $n.upDisabledOnFirst -and $n.clickedDown -and $n.afterDown[1] -match "Film") "▲ $(@($n.afterUp) -join ' | ') ; ▼ $(@($n.afterDown) -join ' | ')"
Check "N2: × uklanja, automatika je ne vraća" ($n.clickedRemove -and $n.removedStaysRemoved -and @($n.afterRemove).Count -eq 1) "$(@($n.afterRemove) -join ' | ')"

# --- V8: Spotify daljinski (simulirani Spotify u playeru; vlasnik nema Premium) ------------------------------------
$mp3 = Join-Path $root "testdata\m1\audio_only.mp3"
$sdata = New-Data "data-spotify" '{ "Language": "hr" }'
$env:JEBAITED_DATA_DIR = $sdata
try { $null = Invoke-Diag -Name "ciklus5_spotify" -File $mp3 -Seconds 1 -ExtraArgs "--diag-ui-test spotify" -TimeoutSeconds 120 }
finally { Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue }
$sp = Read-Ui "ciklus5_spotify"
Check "V8: bez Client ID-a vodič s redirect URI-jem" ($sp.setup.setupVisible -and -not $sp.setup.nowVisible -and $sp.setup.redirect -eq "http://127.0.0.1:43821/callback") "'$($sp.setup.redirect)'"
Check "V8: kriv Client ID odbijen, ispravan spremljen" ($sp.badIdStatus -match "Client ID" -and -not $sp.afterId.setupVisible -and $sp.afterId.signInVisible -and $sp.afterId.saved -eq "0123456789abcdef0123456789abcdef") "'$($sp.badIdStatus)' | $($sp.afterId.account)"
$nw = $sp.now
Check "V8: trenutna pjesma i playliste (simulirani Spotify)" ($nw.track -eq "Testna pjesma" -and $nw.artist -eq "Test artist" -and $nw.device -match "Test PC" -and @($nw.items).Count -eq 2) "'$($nw.track)' · '$($nw.artist)' · '$($nw.device)' · $(@($nw.items) -join ', ')"
$pz = $sp.paused
Check "V8: pauza iz prozora, mini player = Spotify daljinski" ((@($pz.commands) -match "PUT /v1/me/player/pause").Count -ge 1 -and $pz.active -and $pz.source -eq "SPOTIFY" -and $pz.marquee -match "Test artist — Testna pjesma" -and $pz.localPaused) "izvor '$($pz.source)', '$($pz.marquee)', lokalno pauzirano $($pz.localPaused)"
$pl = $sp.played
Check "V8: Play u mini playeru i ▶ na playlisti" ((@($pl.commands) -match "PUT /v1/me/player/play").Count -ge 1 -and (@($pl.commands) -match "spotify:playlist:p2").Count -eq 1 -and $pl.playing) "$(@($pl.commands) -join ' | ')"
Check "V8: nema aktivnog uređaja = jasna poruka" ($sp.noDevice -match "uređaj") "'$($sp.noDevice)'"
Check "V8: lokalna datoteka vraća mini player na glazbu" (-not $sp.local.active -and $sp.local.source -ne "SPOTIFY") "izvor '$($sp.local.source)'"

# --- N4: vrijeme pokretanja bez slike i sa slikom (po 3 pokretanja, medijan) ------------------------------------------
function Median([double[]]$v) { $s = $v | Sort-Object; return $s[[int][Math]::Floor($s.Count / 2)] }
$plain = New-Data "data-plain" '{ "Language": "hr" }'
$bgData = New-Data "data-bg" $withBg
$t0 = @(); $t1 = @()
for ($i = 0; $i -lt 3; $i++) {
    $env:JEBAITED_DATA_DIR = $plain; try { $t0 += [double](Invoke-Diag -Name "ciklus5_start_plain" -File @() -Seconds 1 -TimeoutSeconds 60).StartupMs } finally { Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue }
    $env:JEBAITED_DATA_DIR = $bgData; try { $t1 += [double](Invoke-Diag -Name "ciklus5_start_bg" -File @() -Seconds 1 -TimeoutSeconds 60).StartupMs } finally { Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue }
}
$m0 = Median $t0; $m1 = Median $t1
Check "N4: slika ne usporava pokretanje" (($m1 - $m0) -lt 150) ("bez slike {0} ms, sa slikom {1} ms (medijan od 3; pojedinačno {2} | {3})" -f $m0, $m1, ($t0 -join ", "), ($t1 -join ", "))

$results | Format-Table -AutoSize -Wrap
$failed = @($results | Where-Object { $_.Rezultat -ne "OK" }).Count
Write-Host ("Ukupno {0}, uspješno {1}, grešaka {2}" -f $results.Count, ($results.Count - $failed), $failed)
if ($failed -gt 0) { exit 1 }
