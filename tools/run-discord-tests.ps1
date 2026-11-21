<#
.SYNOPSIS
  Testovi bota za Discord (V4-B) u pravom playeru, bez glasovnog kanala (--diag-ui-test discord, testna mapa podataka):
  - hvatanje zvuka samog playera (process loopback) i Opus kodiranje; stvarna razina na vrlo tihom tonu (oko -60 dBFS),
    dekodirana natrag u 440 Hz i uspoređena sa sirovim hvatanjem;
  - pravila tišine: pauza, video, otvoren YouTube (i povratak na slanje);
  - izmišljeni tokeni na stvarnom Discordu: oba moraju biti BadToken, ništa se ne sprema.

.EXAMPLE
  .\tools\run-discord-tests.ps1
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Test = $Name; Rezultat = $(if ($Ok) { "OK" } else { "GREŠKA" }); Detalji = $Detail })
}

$data = Join-Path $root "testdata\bot\data-discord"
if (Test-Path $data) { Remove-Item $data -Recurse -Force }
New-Item -ItemType Directory -Force -Path $data | Out-Null
[IO.File]::WriteAllText((Join-Path $data "settings.json"), '{ "Language": "hr" }')

$env:JEBAITED_DATA_DIR = $data
try { $null = Invoke-Diag -Name "discord" -File (Join-Path $root "testdata\m1\audio_only.mp3") -Seconds 1 -ExtraArgs "--diag-ui-test discord" -TimeoutSeconds 180 }
finally { Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue }

$u = (Get-Content -LiteralPath (Join-Path $script:DiagDir "discord.json") -Raw -Encoding UTF8 | ConvertFrom-Json).uiTest
if (-not $u -or $u.exception -or $u.error) { throw "discord: $($u.exception)$($u.error)" }

function Silent($m, [string]$Decision) {
    return ($m.decision -eq $Decision -and [int]$m.packets -eq 0 -and [int]$m.silenceFrames -eq 0 -and [int]$m.transitionSilence -eq 5)
}
function Sending($m) { return ($m.decision -eq "Send" -and [int]$m.packets -ge 40) }
function Info($m) { return "$($m.decision), paketa $($m.packets), tišina $($m.silenceFrames), pri prijelazu $($m.transitionSilence)" }

Check "Windows podržava hvatanje zvuka procesa (build 20348+)" ($u.supported -and [int]$u.build -ge 20348) "build $($u.build)"
Check "Glazba (utišana): hvatanje i Opus rade, 50 paketa/s" ($u.musicMuted.decision -eq "Send" -and [int]$u.musicMuted.packets -ge 60 -and [int]$u.musicMuted.peak -le 2) "$(Info $u.musicMuted), vrh $($u.musicMuted.peak)"
Check "Utišana sesija (ao-mute) utiša i ono što ide na Discord" ($u.aoMute -eq "yes" -and [int]$u.musicAoMuted.peak -le 2) "ao-mute $($u.aoMute), vrh $($u.musicAoMuted.peak)"

$raw = $u.quiet.raw; $dec = $u.quiet.decoded
# Glasnoća raste do vrha ~30 (oko -60 dBFS), najviše do 20 (sigurnosna granica). Ista razina hvatanja na istom računalu
# razlikovala se između pokretanja za ~12 dB (2026-11-21: i stari build; ne ovisi o glavnoj glasnoći, uređaju ni sesiji),
# pa je dovoljno da je pri granici uhvaćen stvaran ton jasno iznad tišine (vrh >= 16, oko -66 dBFS; tišina je 0-2).
# Sadržaj tona (440 Hz, RMS, bez pucketanja) provjeravaju sljedeće provjere.
Check "Stvarna razina na vrlo tihom tonu (oko -60 dBFS)" ([int]$u.quietVolume -le 20 -and [int]$raw.peak -ge 16 -and [int]$raw.peak -le 200) "glasnoća $($u.quietVolume) ($($u.quietRamp)), vrh $($raw.peak), sesija u Windowsima $($u.sessionVolume)"
Check "Opus dekodiran natrag = isti ton od 440 Hz" ([math]::Abs([double]$dec.hz - 440) -le 9 -and [math]::Abs([double]$dec.rms - [double]$raw.rms) -le [double]$raw.rms * 0.1) "$($dec.hz) Hz, RMS $($dec.rms) (sirovo $($raw.rms))"
Check "Bez pucketanja (skokovi kao u sirovom hvatanju)" ([int]$dec.maxStep -le [int]$raw.maxStep * 2 + 4) "najveći skok $($dec.maxStep) (sirovo $($raw.maxStep))"

Check "Pauza: tišina (5 okvira tišine pri prijelazu)" (Silent $u.paused "SilencePaused") (Info $u.paused)
Check "Nastavak: opet šalje" (Sending $u.resumed) (Info $u.resumed)
Check "Video: tišina (filmovi i spotovi se nikad ne šalju)" (Silent $u.video "SilenceVideo") (Info $u.video)
Check "Ponovno glazba: šalje" (Sending $u.musicAgain) (Info $u.musicAgain)
Check "Otvoren YouTube Music: tišina i kad lokalna glazba svira" (Silent $u.youTubeOpen "SilenceYouTube") (Info $u.youTubeOpen)
Check "YouTube zatvoren: opet šalje" (Sending $u.youTubeClosed) (Info $u.youTubeClosed)
Check "Svi paketi prošli kroz slanje (bez gubitka)" ([int]$u.sentTotal -eq [int]$u.packetsTotal -and [int]$u.packetsTotal -gt 0) "poslano $($u.sentTotal) od $($u.packetsTotal)"

$b = $u.badToken; $m = $u.malformedToken
Check "Izmišljen token oblika pravog: Discord ga odbija (BadToken), ništa se ne sprema" ($b.looksLikeToken -and -not $b.connected -and $b.error -eq "BadToken" -and -not $b.file) "$($b.state)/$($b.error), $($b.ms) ms"
Check "Token koji nije token bota: BadToken bez mreže" (-not $m.looksLikeToken -and -not $m.connected -and $m.error -eq "BadToken" -and [int]$m.ms -lt 200 -and -not $m.file) "$($m.state)/$($m.error), $($m.ms) ms"

# --- Sučelje (Discord prozor i mini player), bez pravog bota --------------------------------------------------------
$dataUi = Join-Path $root "testdata\bot\data-discordui"
if (Test-Path $dataUi) { Remove-Item $dataUi -Recurse -Force }
New-Item -ItemType Directory -Force -Path $dataUi | Out-Null
[IO.File]::WriteAllText((Join-Path $dataUi "settings.json"), '{ "Language": "hr" }')
$env:JEBAITED_DATA_DIR = $dataUi
try { $null = Invoke-Diag -Name "discordui" -File (Join-Path $root "testdata\m1\audio_only.mp3") -Seconds 1 -ExtraArgs "--diag-ui-test discordui" -TimeoutSeconds 180 }
finally { Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue }
$w = (Get-Content -LiteralPath (Join-Path $script:DiagDir "discordui.json") -Raw -Encoding UTF8 | ConvertFrom-Json).uiTest
if (-not $w -or $w.exception -or $w.error) { throw "discordui: $($w.exception)$($w.error)" }

Check "Prozor: Ctrl+Shift+D ga otvara; bez tokena samo postavljanje" ($w.start.setup -and -not $w.start.bot -and -not $w.start.channel) "postavljanje $($w.start.setup), bot $($w.start.bot), kanal $($w.start.channel)"
Check "Prozor: nešto što nije token se odbija i ne sprema" ($w.badShape.setup -and $w.badShape.status -match "ne izgleda kao token" -and -not $w.badShapeFile) "'$($w.badShape.status)'"
Check "Prozor: izmišljen token se sprema kriptiran, Discord ga odbija, jasna poruka" ($w.fake.bot -and -not $w.fake.setup -and $w.fake.connect -and $w.fakeState -eq "Error/BadToken" -and $w.fake.status -match "odbio token" -and $w.fakeFile -and -not $w.fakePlain) "$($w.fakeState), '$($w.fake.status)'"
Check "Prozor: promjena tokena; prazno polje uklanja token i datoteku" ($w.changeShowsSetup -and $w.removed.setup -and $w.removed.status -match "uklonjen" -and -not $w.removedFile -and -not $w.removedHasToken) "'$($w.removed.status)'"
Check "Mini player: gumb i stavka izbornika otvaraju bota za Discord" ($w.miniButton -and $w.miniMenu -and $w.miniOpens -and $w.miniOwner -and $w.miniTooltip -eq "Bot za Discord" -and -not $w.miniLit) "opis '$($w.miniTooltip)', vlasnik mini $($w.miniOwner)"
Check "Zatvaranje playera bez spojenog bota ne čeka" ([int]$w.closeMs -lt 200) "$($w.closeMs) ms"

$results | Format-Table -AutoSize -Wrap
$failed = @($results | Where-Object { $_.Rezultat -ne "OK" }).Count
Write-Host ("Ukupno {0}, uspješno {1}, grešaka {2}" -f $results.Count, ($results.Count - $failed), $failed)
if ($failed -gt 0) { exit 1 }
