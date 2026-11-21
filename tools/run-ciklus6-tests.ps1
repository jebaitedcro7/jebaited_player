<#
.SYNOPSIS
  Testovi ciklusa 6 u pravom playeru:
  - V9 YouTube Music (stvarni ugrađeni player u WebView2, mini player, simulirana pretraga i kvota);
  - N1 (testna mapa podataka): redigirani izvještaji, pristanak, slanje na simuliranu točku;
  - N3 (testna mapa podataka, simulirani Open-Meteo + jedan stvarni poziv): vrijeme i datum, grad, prognoza, °F, bez interneta.

.EXAMPLE
  .\tools\run-ciklus6-tests.ps1
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
function New-Data([string]$Name, [string]$Json) {
    $d = Join-Path $root "testdata\ciklus6\$Name"
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $d | Out-Null
    [IO.File]::WriteAllText((Join-Path $d "settings.json"), $Json)
    return $d
}

# --- N3: Home info --------------------------------------------------------------------------------------------------
$env:JEBAITED_DATA_DIR = New-Data "data-homeinfo" '{ "Language": "hr" }'
try { $null = Invoke-Diag -Name "ciklus6_homeinfo" -File @() -Seconds 1 -ExtraArgs "--diag-ui-test homeinfo" -TimeoutSeconds 120 }
finally { Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue }
$h = Read-Ui "ciklus6_homeinfo"
Check "N3: vrijeme i datum na početnom ekranu, prognoza zadano isključena" ($h.start.homeVisible -and $h.start.time -match "\d" -and $h.start.date.Length -gt 5 -and -not $h.start.weatherVisible) "'$($h.start.time)' · '$($h.start.date)'"
Check "N3: bez grada nema prognoze ni mreže" (-not $h.beforeCity) "prognoza vidljiva $($h.beforeCity)"
Check "N3: grad iz postavki, prognoza na početnom ekranu" ([int]$h.found -eq 1 -and $h.weather.text -match "8 °C · Oblačno · Zagreb" -and $h.weather.visible -and -not $h.weather.source -and $h.weather.cityText -match "Zagreb") "'$($h.weather.text)' | $($h.weather.cityText)"
Check "N3: °F" ($h.fahrenheit -match "47 °F") "'$($h.fahrenheit)'"
Check "N3: bez interneta zadnja prognoza s vremenom ažuriranja" ($h.offline.stale -and $h.offline.text -match "47 °F" -and $h.offline.text -match "ažurirano") "'$($h.offline.text)'"
Check "N3: isključeno = ne prikazuje se" (-not $h.hidden.time -and -not $h.hidden.weather -and $h.hidden.date) "vrijeme $($h.hidden.time), prognoza $($h.hidden.weather), datum $($h.hidden.date)"
Check "N3: stvarni Open-Meteo (internet)" ($h.real.place -match "Zagreb" -and $null -ne $h.real.temperature) "$($h.real.place): $($h.real.temperature) °C, kod $($h.real.code) $($h.real.error)"

# --- N1: dijagnostika -----------------------------------------------------------------------------------------------
$env:JEBAITED_DATA_DIR = New-Data "data-reports" '{ "Language": "hr" }'
try { $null = Invoke-Diag -Name "ciklus6_reports" -File @() -Seconds 1 -ExtraArgs "--diag-ui-test reports" -TimeoutSeconds 120 }
finally { Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue }
$rp = Read-Ui "ciklus6_reports"
Check "N1: greška → redigirani izvještaj u lokalnom redu" ([int]$rp.queued.count -eq 1 -and $rp.queued.message -match "Holiday.mkv" -and $rp.queued.message -notmatch "Someone|SECRET42") "'$($rp.queued.message)'"
Check "N1: bez pristanka ništa se ne šalje" ([int]$rp.sentWithoutConsent -eq 0 -and -not $rp.before.checked -and $rp.before.status -match "spremljenih" -and $rp.before.github) "'$($rp.before.status)'"
Check "N1: GitHub issue (ručno slanje) bez osobnih podataka; izdanje bez prijemne točke ne pita za pristanak" (-not $rp.releaseHasEndpoint -and $rp.github -like "https://github.com/jebaitedcro7/jebaited_player/issues/new*" -and $rp.github -notmatch "Someone|SECRET42") "duljina $($rp.github.Length)"
Check "N1: s pristankom šalje se na prijemnu točku" ($rp.after.consent -and [int]$rp.after.pending -eq 0 -and @($rp.after.requests).Count -eq 1 -and $rp.after.requests[0] -match "POST /report" -and $rp.after.requests[0] -notmatch "Someone|SECRET42" -and $rp.after.status -match "Nema") "'$($rp.after.status)'"

# --- V9: YouTube Music (stvarni ugrađeni player, internet; pretraga simulirana) ----------------------------------------
$env:JEBAITED_DATA_DIR = New-Data "data-youtube" '{ "Language": "hr" }'
try { $null = Invoke-Diag -Name "ciklus6_youtube" -File (Join-Path $root "testdata\m1\audio_only.mp3") -Seconds 1 -ExtraArgs "--diag-ui-test youtube" -TimeoutSeconds 180 }
finally { Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue }
$y = Read-Ui "ciklus6_youtube"
Check "V9: prozor s WebView2, vodič za ključ, kriv link odbijen" ($y.ready -and $y.keyPanel -and -not $y.badLink -and $y.badLinkStatus -match "nije link") "'$($y.badLinkStatus)'"
Check "V9: stvarni ugrađeni YouTube player svira (utišano)" ($y.played -and [int]$y.embed.state -eq 1 -and [double]$y.embed.time -gt 1 -and $y.embed.id -eq "M7lc1UVf-VE" -and $null -eq $y.embed.error) "'$($y.embed.title)', $([math]::Round([double]$y.embed.time,1)) / $($y.embed.duration) s, greška '$($y.embed.error)'"
Check "V9: YouTube postaje izvor, lokalno se pauzira, mini player YOUTUBE" ($y.embed.active -and $y.embed.localPaused -and $y.mini.source -eq "YOUTUBE" -and $y.mini.marquee -match "Embedded") "'$($y.mini.source)' · '$($y.mini.marquee)'"
Check "V9: pauza i play iz mini playera" ([int]$y.pausedByMini -eq 2 -and [int]$y.playedByMini -eq 1) "pauza $($y.pausedByMini), play $($y.playedByMini)"
Check "V9: pretraga (simulirani Data API) i limit kvote" ([int]$y.found -eq 1 -and $y.foundTitle -eq "Test song" -and $y.quotaStatus -match "limit") "'$($y.foundTitle)' · '$($y.quotaStatus)'"
Check "V9: ključ se može promijeniti i ukloniti" ($y.keyPanelHiddenWithKey -and $y.keyPanelShown -and $y.keyRemoved -and $y.keyRemovedStatus -match "uklonjen" -and $y.keyPanelAfterRemove) "'$($y.keyRemovedStatus)'"
Check "V9: lokalna datoteka vraća lokalni izvor (YouTube pauziran)" (-not $y.local.active -and $y.local.source -ne "YOUTUBE" -and $y.closed) "izvor '$($y.local.source)', zatvoren $($y.closed)"

$results | Format-Table -AutoSize -Wrap
$failed = @($results | Where-Object { $_.Rezultat -ne "OK" }).Count
Write-Host ("Ukupno {0}, uspješno {1}, grešaka {2}" -f $results.Count, ($results.Count - $failed), $failed)
if ($failed -gt 0) { exit 1 }
