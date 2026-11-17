<#
.SYNOPSIS
  Testna "serija" i film za M3 (testdata\m3): dvije sezone, sample datoteka, jedna duža epizoda za nastavak gledanja.
#>
param(
    [string]$FFmpeg = "ffmpeg",
    [string]$Root = (Join-Path $PSScriptRoot "..")
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path $Root).Path
$m1 = Join-Path $Root "testdata\m1"
$m3 = Join-Path $Root "testdata\m3"
$s1 = Join-Path $m3 "Serija.Test\Season 01"
$s2 = Join-Path $m3 "Serija.Test\Season 02"
$movies = Join-Path $m3 "Filmovi"
New-Item -ItemType Directory -Force -Path $s1, $s2, $movies | Out-Null

$short = Join-Path $m1 "fmt_m4v_h264_aac.m4v"   # 10 s, 720p

function Copy-Short([string]$Target) {
    if (-not (Test-Path -LiteralPath $Target)) { Copy-Item -LiteralPath $short -Destination $Target }
}

# Epizoda 1 traje 2 minute (za nastavak gledanja nakon 30 s).
$e1 = Join-Path $s1 "Serija.Test.S01E01.Pilot.720p.WEB-DL.x264-GRP.mp4"
if (-not (Test-Path -LiteralPath $e1)) {
    & $FFmpeg -hide_banner -loglevel error -y -stream_loop 11 -i $short -c copy -t 120 $e1
    if ($LASTEXITCODE -ne 0) { throw "FFmpeg nije uspio" }
}

Copy-Short (Join-Path $s1 "Serija.Test.S01E02.Drugi.Dan.720p.WEB-DL.x264-GRP.mp4")
Copy-Short (Join-Path $s1 "Serija.Test.S01E03.Kraj.720p.WEB-DL.x264-GRP.mp4")
Copy-Short (Join-Path $s1 "Serija.Test.S01E01.sample.mp4")
Copy-Short (Join-Path $s2 "Serija.Test.S02E01.720p.WEB-DL.x264-GRP.mp4")

$film = Join-Path $movies "Film.Za.Test.2026.1080p.BluRay.x264-GRP.mp4"
if (-not (Test-Path -LiteralPath $film)) { Copy-Item -LiteralPath (Join-Path $m1 "1080p_h264_aac.mp4") -Destination $film }

Get-ChildItem $m3 -Recurse -File | Select-Object @{n = 'Datoteka'; e = { $_.FullName.Substring($m3.Length + 1) } }, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
