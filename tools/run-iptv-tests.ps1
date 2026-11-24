<#
.SYNOPSIS
  IPTV test u pravom playeru (scenarij `iptv`) nad testnom mapom podataka (nikad nad tvojom).
  Koristi lokalnu datoteku s testnim računom `secrets\iptv-test.json` (u .gitignore; polja server, username, password).
  Izvodi JEDNU prijavu; lozinka se ne ispisuje. Rezultat: testdata\diag\iptv.json i snimke prozora.

.EXAMPLE
  .\tools\run-iptv-tests.ps1
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$track = Join-Path $root "testdata\ciklus2\album\01 Prva.mp3"
if (-not (Test-Path $track)) { throw "Nema testne pjesme ($track): pokreni najprije tools\run-ui217-tests.ps1." }
$secret = Join-Path $root "secrets\iptv-test.json"
if (-not (Test-Path $secret)) { throw "Nema $secret (server, username, password)." }

$data = Join-Path $root "testdata\iptv\data"
if (Test-Path $data) { Remove-Item $data -Recurse -Force }
New-Item -ItemType Directory -Force -Path $data | Out-Null
[IO.File]::WriteAllText((Join-Path $data "settings.json"), '{ "Language": "hr" }')

$env:JEBAITED_DATA_DIR = $data
$env:JEBAITED_IPTV_TEST_FILE = $secret
try {
    $null = Invoke-Diag -Name "iptv" -File $track -Seconds 1 -ExtraArgs "--diag-ui-test iptv" -TimeoutSeconds 400
}
finally {
    Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue
    Remove-Item Env:JEBAITED_IPTV_TEST_FILE -ErrorAction SilentlyContinue
}

$u = (Get-Content -LiteralPath (Join-Path $script:DiagDir "iptv.json") -Raw -Encoding UTF8 | ConvertFrom-Json).uiTest
if (-not $u -or $u.exception -or $u.error) { throw "iptv: $($u.exception)$($u.error)" }
$u | ConvertTo-Json -Depth 5
