<#
.SYNOPSIS
  Glatkoća povlačenja ruba IPTV panela na velikom (maksimiziranom) prozoru uz 4K video. Bez mreže i računa.
.EXAMPLE
  .\tools\run-iptvdrag-tests.ps1
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$video = Join-Path $root "testdata\m1\4k_hevc10_sdr_ac3-51.mkv"
if (-not (Test-Path $video)) { throw "Nema $video" }
$data = Join-Path $root "testdata\iptvdrag\data"
if (Test-Path $data) { Remove-Item $data -Recurse -Force }
New-Item -ItemType Directory -Force -Path $data | Out-Null
[IO.File]::WriteAllText((Join-Path $data "settings.json"), '{ "Language": "hr" }')
$env:JEBAITED_DATA_DIR = $data
try { $null = Invoke-Diag -Name "iptvdrag" -File $video -Seconds 1 -ExtraArgs "--diag-ui-test iptvdrag" -TimeoutSeconds 180 }
finally { Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue }
$u = (Get-Content -LiteralPath (Join-Path $script:DiagDir "iptvdrag.json") -Raw -Encoding UTF8 | ConvertFrom-Json).uiTest
if (-not $u -or $u.exception -or $u.error) { throw "iptvdrag: $($u.exception)$($u.error)" }
$u | ConvertTo-Json -Depth 5
