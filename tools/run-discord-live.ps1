<#
.SYNOPSIS
  Stvarni test bota za Discord (V4-B) na vlasnikovom serveru: --diag-ui-test discordlive.

.DESCRIPTION
  Prije pokretanja:
  - token bota u secrets\discord-bot-token.txt (mapa secrets\ je u .gitignore; token se nikad ne ispisuje);
  - vlasnik je u glasovnom kanalu na serveru na koji je bot pozvan (bot ulazi u taj kanal).
  Redoslijed (oko 50 s): ton 10 s, pjesma 15 s, pauza 5 s, nastavak 5 s, video 5 s (mora biti tišina), ton 5 s, izlazak.
  Zvuk se čuje i iz zvučnika ovog računala (hvatanje je nakon glasnoće playera); glavna glasnoća Windowsa ne utječe na Discord.
  Na kraju se provjerava da token nije ni u rezultatu ni u dnevniku testne mape.

.PARAMETER Music
  Pjesma za drugi dio testa (zadano testdata\ciklus2\album\01 Prva.mp3).

.PARAMETER Ui
  Cijeli put kroz sučelje (--diag-ui-test discorduilive): token u prozor, „Uđi u moj kanal”, glazba (gumb u mini playeru
  svijetli), video (tišina), glazba, „Izađi”, odjava pri zatvaranju; token se na kraju uklanja iz testne mape.

.EXAMPLE
  .\tools\run-discord-live.ps1
  .\tools\run-discord-live.ps1 -Ui
#>
param([string]$Music, [switch]$Ui)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root

$tokenFile = Join-Path $root "secrets\discord-bot-token.txt"
if (-not (Test-Path $tokenFile)) { throw "Nema $tokenFile (token bota, jedan redak)." }

$data = Join-Path $root "testdata\bot\data-live"
if (Test-Path $data) { Remove-Item $data -Recurse -Force }
New-Item -ItemType Directory -Force -Path $data | Out-Null
[IO.File]::WriteAllText((Join-Path $data "settings.json"), '{ "Language": "hr" }')

$env:JEBAITED_DATA_DIR = $data
$env:JEBAITED_DISCORD_TOKEN_FILE = $tokenFile
if ($Music) { $env:JEBAITED_DISCORD_MUSIC = (Resolve-Path $Music).Path }
$scenario = if ($Ui) { "discorduilive" } else { "discordlive" }
try { $null = Invoke-Diag -Name $scenario -File (Join-Path $root "testdata\m1\audio_only.mp3") -Seconds 1 -ExtraArgs "--diag-ui-test $scenario" -TimeoutSeconds 240 }
finally { Remove-Item Env:JEBAITED_DATA_DIR, Env:JEBAITED_DISCORD_TOKEN_FILE, Env:JEBAITED_DISCORD_MUSIC -ErrorAction SilentlyContinue }

$jsonFile = Join-Path $script:DiagDir "$scenario.json"
$text = Get-Content -LiteralPath $jsonFile -Raw -Encoding UTF8

# Token ne smije biti nigdje: ni u rezultatu ni u datotekama testne mape (dnevnik).
$token = (Get-Content -LiteralPath $tokenFile -Raw).Trim()
$leaks = @()
if ($token.Length -gt 0 -and $text.Contains($token)) { $leaks += "discordlive.json" }
Get-ChildItem $data -Recurse -File | ForEach-Object {
    if ($token.Length -gt 0 -and ([IO.File]::ReadAllText($_.FullName)).Contains($token)) { $leaks += $_.Name }
}
$token = $null
if ($leaks.Count -gt 0) { Write-Host "TOKEN JE PROCURIO U: $($leaks -join ', ') — obriši te datoteke i resetiraj token u portalu." -ForegroundColor Red; exit 2 }
Write-Host "Token nije ni u rezultatu ni u dnevniku." -ForegroundColor Green

$u = ($text | ConvertFrom-Json).uiTest
if (-not $u) { throw "Nema rezultata ($scenario.json)." }

if ($Ui) {
    if ($u.exception) { Write-Host "IZNIMKA: $($u.exception)" -ForegroundColor Red }
    if ($u.error) { Write-Host "GREŠKA: $($u.error)" -ForegroundColor Red }
    "Prijava kroz prozor: $($u.connectedState) · '$($u.connected.botText)' · status '$($u.connected.status)' · kanala na popisu: $($u.channelList)"
    "Uđi u moj kanal: $($u.joined)"
    foreach ($k in 'music', 'video', 'musicAgain', 'left') {
        $s = $u.$k
        if ($s) { "{0,-11} {1,-16} odluka {2,-14} mini svijetli {3,-5} razina {4,-6} poslano {5,-5} '{6}' | '{7}'" -f $k, $s.state, $s.decision, $s.miniLit, $s.level, $s.sent, $s.channelText, $s.sendingText }
    }
    if ($u.closed) { "Zatvaranje: $($u.closed.state), aktivan $($u.closed.active), $($u.closed.ms) ms; token u testnoj mapi nakon testa: $($u.tokenFileAfter)" }
    exit 0
}
if ($u.exception) { Write-Host "IZNIMKA: $($u.exception)" -ForegroundColor Red }
if ($u.error) { Write-Host "GREŠKA: $($u.error)" -ForegroundColor Red }

"Prijava: $($u.connect.state)/$($u.connect.error) kao '$($u.connect.botName)' ($($u.connect.ms) ms) $($u.connect.detail)"
"Kanal: $($u.channel) (glasovnih kanala vidljivo: $($u.voiceChannels))"
"Vlasnik bota poznat: $($u.ownerKnown); vlasnik je u kanalu: $($u.ownerChannel)"
"Dozvole bota u kanalu: $($u.botPermissions)"
if ($u.joinProblem) { Write-Host "Ne može ući: $($u.joinProblem)" -ForegroundColor Yellow }
if ($u.join) { "Ulazak: $($u.join.state)/$($u.join.error) $($u.join.detail)" }
$rows = foreach ($k in 'tone', 'music', 'paused', 'resumed', 'video', 'toneAgain') {
    $s = $u.$k
    if ($s) { [pscustomobject]@{ Dio = $k; Datoteka = $s.file; Odluka = $s.decision; Poslano = $s.packetsSent; Vrh = $s.peak; Stanje = "$($s.state)/$($s.error)" } }
}
$rows | Format-Table -AutoSize
if ($u.after) { "Nakon izlaska: $($u.after.state)/$($u.after.error), ukupno poslano $($u.after.sent) paketa" }
"--- Vremenski tijek:"; $u.timeline
"--- NetCord (DAVE, upozorenja i greške):"
@($u.log) | Where-Object { $_ -match 'DAVE|Dave|dave|MLS|E2EE|Warn|Error|Critical|exception' } | Select-Object -Last 40
