<#
.SYNOPSIS
  Testovi obavijesti (kartica na Home ekranu, strelica, tipka Obavijesti, popis, Postavke → Obavijesti) u pravom playeru,
  nad testnom mapom podataka (nikad nad tvojom):
  - scenarij `news`: položaj kartice bez playera (nisko) i uz player (visoko), glatki pomak, strelica, X, popis, detalji i radnja,
    Postavke → Obavijesti, nova verzija, spremanje u notifications.json;
  - scenarij `newsrestart`: drugo pokretanje s istom mapom (pročitano i odbačeno se ne vraća).

.EXAMPLE
  .\tools\run-news-tests.ps1
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$track = Join-Path $root "testdata\ciklus2\album\01 Prva.mp3"
if (-not (Test-Path $track)) { throw "Nema testne pjesme ($track): pokreni najprije tools\run-ui217-tests.ps1 (stvara testni album)." }

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Test = $Name; Rezultat = $(if ($Ok) { "OK" } else { "GREŠKA" }); Detalji = $Detail })
}
function Near($a, $b, $tol) { return [math]::Abs([double]$a - [double]$b) -le $tol }
function Read-Ui([string]$Name) {
    $u = (Get-Content -LiteralPath (Join-Path $script:DiagDir "$Name.json") -Raw -Encoding UTF8 | ConvertFrom-Json).uiTest
    if (-not $u -or $u.exception -or $u.error) { throw "$Name`: $($u.exception)$($u.error)" }
    return $u
}

$data = Join-Path $root "testdata\news\data"
if (Test-Path $data) { Remove-Item $data -Recurse -Force }
New-Item -ItemType Directory -Force -Path $data | Out-Null
[IO.File]::WriteAllText((Join-Path $data "settings.json"), '{ "Language": "hr" }')

$env:JEBAITED_DATA_DIR = $data
try {
    $null = Invoke-Diag -Name "news" -File $track -Seconds 1 -ExtraArgs "--diag-ui-test news" -TimeoutSeconds 180
    $n = Read-Ui "news"
    $stateFile = Join-Path $data "notifications.json"
    $stateJson = if (Test-Path $stateFile) { Get-Content $stateFile -Raw } else { "" }
    $null = Invoke-Diag -Name "newsrestart" -File $track -Seconds 1 -ExtraArgs "--diag-ui-test newsrestart" -TimeoutSeconds 120
    $rs = Read-Ui "newsrestart"
}
finally { Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue }

$total = [int]$n.start.unread
$low = $n.low; $high = $n.high; $again = $n.lowAgain

Check "Kartica: prva po važnosti je 'Obavijesti su stigle', brojač = broj obavijesti" ($low.cardVisible -and $low.item -eq "notifications-intro" -and $low.badge -eq "$total" -and $total -ge 3) "kartica '$($n.lowTitle)', brojač '$($low.badge)', popis $($n.start.all -join ', ')"
Check "Bez playera: kartica NISKO, tik iznad donjeg reda (razmak 25 px)" (-not $low.barVisible -and (Near $low.cardBottom ([double]$low.rowTop - 25) 2.5)) "donji rub kartice $($low.cardBottom), donji red počinje na $($low.rowTop)"
Check "Bez playera: kratka strelica je ravna, okomita i pokazuje na verziju" ($low.arrowVisible -and $low.arrowStraight -and (Near $low.tipX $low.versionX 1.5) -and ([double]$low.tipY -gt [double]$low.cardBottom + 12) -and ([double]$low.tipY -lt [double]$low.versionTop + 8)) "vrh ($($low.tipX), $($low.tipY)), sredina verzije $($low.versionX), vrh verzije $($low.versionTop), razmak $([math]::Round([double]$low.tipY - [double]$low.cardBottom, 1))"
Check "Kartica ostaje u prozoru i desni rub je na ravnom dijelu (strelica ne izlazi iz kuta)" ([double]$low.cardRight -le [double]$low.overlayWidth - 6 -and [double]$low.cardRight -ge [double]$low.versionX + 15) "desni rub $($low.cardRight) od $($low.overlayWidth), verzija $($low.versionX)"
Check "Uz player: kartica VISOKO, iznad trake playera, ne zaklanja je" ($high.barVisible -and -not $high.overlapsBar -and (Near $high.cardBottom ([double]$high.barTop - 13) 2.5)) "donji rub $($high.cardBottom), vrh trake $($high.barTop), preklapanje $($high.overlapsBar)"
Check "Uz player: duga strelica je savijena i pokazuje na verziju" ($high.arrowVisible -and -not $high.arrowStraight -and (Near $high.tipX $high.versionX 1.5) -and [double]$high.tipY -gt [double]$high.cardBottom + 40) "vrh ($($high.tipX), $($high.tipY)), duljina $([math]::Round([double]$high.tipY - [double]$high.cardBottom, 1)) px"
Check "Pomak kartice je GLATKI (usred animacije izmedu dva polozaja) i vraca se na isto mjesto; bez animacija (Windows ih je iskljucio) pomak je trenutan" ((-not $high.animationsEnabled -and (Near $high.midAnimationBottom $high.slideTarget 1) -and (Near $high.restored $high.cardBottom 1.5)) -or ($high.animationsEnabled -and [double]$high.midAnimationBottom -gt [double]$high.cardBottom + 1 -and [double]$high.midAnimationBottom -lt [double]$high.slideTarget - 1 -and (Near $high.restored $high.cardBottom 1.5))) "animacije $($high.animationsEnabled); visoko $($high.cardBottom) -> usred $($high.midAnimationBottom) -> cilj $($high.slideTarget), vraceno $($high.restored)"
Check "Player nestane: kartica se vraća nisko (isti položaj kao prije)" ((Near $again.cardBottom $low.cardBottom 2.5) -and $again.arrowStraight -and -not $again.barVisible) "donji rub $($again.cardBottom) (prije $($low.cardBottom))"
Check "Tipka Obavijesti: na Home ekranu, ne preklapa prekidač teme" ($low.bellVisible -and -not $low.bellOverlapsToggle) "lijevi rub tipke $($low.bellLeft)"
Check "Tipka Obavijesti: popis sa svim obavijestima" ($n.popup.open -and [int]$n.popup.rows -eq $total) "redaka $($n.popup.rows) od $total, snimka $($n.popup.screenshot)"
$d = $n.dismissed
Check "X: obavijest je odbačena, kartica nestaje i ne dolazi nova 'nova funkcija/savjet' u istom pokretanju" ($d.status -eq "Dismissed" -and -not $d.cardVisible -and [int]$d.unread -eq $total - 1 -and $d.badge -eq "$($total - 1)") "'$($d.id)' = $($d.status), kartica $($d.cardVisible), nepročitano $($d.unread)"
$de = $n.detail
Check "Popis → redak otvara detalje, označava pročitano i zatvara popis" ($de.open -and $de.status -eq "Read" -and $de.popupClosed -and $de.action -eq "Visible" -and $de.title) "'$($de.title)', stanje $($de.status), snimka $($de.screenshot)"
Check "Gumb radnje u detaljima otvara pravu karticu Postavki (Zvuk)" ($n.action.tab -eq "Audio" -and $n.action.detailClosed) "kartica $($n.action.tab)"
$s = $n.settings
Check "Postavke → Obavijesti: kartica postoji i prikazuje sve obavijesti sa stanjima" ($s.tab -eq "Notifications" -and @($s.rows).Count -eq $total -and (@($s.rows) -match ":Dismissed").Count -eq 1 -and (@($s.rows) -match ":Read").Count -ge 1) "$(@($s.rows) -join ', ')"
Check "Označi sve kao pročitano: nepročitanih nema, odbačena ostaje odbačena" ([int]$n.markAll.unread -eq 0 -and -not $n.markAll.badge -and (@($n.markAll.rows) -match ":Dismissed").Count -eq 1) "$(@($n.markAll.rows) -join ', ')"
Check "Nova verzija (iz UpdateService): kartica se pojavi i nakon reakcije, a nestane kad je nema" ($n.update.cardVisible -and $n.update.item -eq "update-9.9.9" -and $n.update.title -match "9\.9\.9" -and $n.update.arrowVisible -and $n.updateGone) "'$($n.update.title)', nestaje $($n.updateGone)"
Check "Stanje je u notifications.json (ne u postavkama) i preživljava ponovno pokretanje" ($n.persist.fileExists -and $n.persist.hasDismissed -and $n.persist.notInSettings -and $stateJson -match "Dismissed") "datoteka $($n.persist.fileExists), odbačeno zapisano $($n.persist.hasDismissed)"
Check "Drugo pokretanje: nema kartice ni broja, pročitano i odbačeno se ne vraća" (-not $rs.state.cardVisible -and -not $rs.state.badge -and (@($rs.state.statuses) -match ":Dismissed").Count -eq 1 -and (@($rs.state.statuses) -match ":Unread").Count -eq 0) "$(@($rs.state.statuses) -join ', ')"
Check "Prikazano najviše jednom po pokretanju (brojač prikaza)" (@($rs.state.shown | Where-Object { [int]($_ -split ':')[1] -ge 4 }).Count -eq 0) "$(@($rs.state.shown) -join ', ')"

$results | Format-Table -AutoSize | Out-String -Width 400 | Write-Host
$failed = @($results | Where-Object { $_.Rezultat -ne "OK" }).Count
Write-Host ("Ukupno {0}, uspješno {1}, grešaka {2}" -f $results.Count, ($results.Count - $failed), $failed)
if ($failed -gt 0) { exit 1 }
