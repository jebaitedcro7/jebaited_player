<#
.SYNOPSIS
  Test bočne trake početnog ekrana (glatko širenje, nazivi, razmak do ruba, bez pomicanja sadržaja). Bez mreže i računa.
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$track = Join-Path $root "testdata\ciklus2\album\01 Prva.mp3"
$data = Join-Path $root "testdata\homerail\data"
if (Test-Path $data) { Remove-Item $data -Recurse -Force }
New-Item -ItemType Directory -Force -Path $data | Out-Null
[IO.File]::WriteAllText((Join-Path $data "settings.json"), '{ "Language": "hr" }')
$env:JEBAITED_DATA_DIR = $data
try { $null = Invoke-Diag -Name "homerail" -File $track -Seconds 1 -ExtraArgs "--diag-ui-test homerail" -TimeoutSeconds 120 }
finally { Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue }
$u = (Get-Content -LiteralPath (Join-Path $script:DiagDir "homerail.json") -Raw -Encoding UTF8 | ConvertFrom-Json).uiTest
if (-not $u -or $u.exception -or $u.error) { throw "homerail: $($u.exception)$($u.error)" }
$u | ConvertTo-Json -Depth 5
