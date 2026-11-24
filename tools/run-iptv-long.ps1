<#
.SYNOPSIS
  Dugotrajna IPTV reprodukcija (zadano 8 minuta): pauzira li se program sam, zastaje li, javlja li greške.
  Koristi secrets\iptv-test.json (jedna prijava).
.EXAMPLE
  .\tools\run-iptv-long.ps1 -Minutes 10
#>
param([int]$Minutes = 8)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$track = Join-Path $root "testdata\ciklus2\album\01 Prva.mp3"
$secret = Join-Path $root "secrets\iptv-test.json"
$data = Join-Path $root "testdata\iptv\data-long"
if (Test-Path $data) { Remove-Item $data -Recurse -Force }
New-Item -ItemType Directory -Force -Path $data | Out-Null
[IO.File]::WriteAllText((Join-Path $data "settings.json"), '{ "Language": "hr" }')
$env:JEBAITED_DATA_DIR = $data
$env:JEBAITED_IPTV_TEST_FILE = $secret
$env:JEBAITED_IPTV_MINUTES = "$Minutes"
try { $null = Invoke-Diag -Name "iptvlong" -File $track -Seconds 1 -ExtraArgs "--diag-ui-test iptvlong" -TimeoutSeconds (($Minutes + 4) * 60) }
finally { Remove-Item Env:JEBAITED_DATA_DIR, Env:JEBAITED_IPTV_TEST_FILE, Env:JEBAITED_IPTV_MINUTES -ErrorAction SilentlyContinue }
$u = (Get-Content -LiteralPath (Join-Path $script:DiagDir "iptvlong.json") -Raw -Encoding UTF8 | ConvertFrom-Json).uiTest
$u | ConvertTo-Json -Depth 5
