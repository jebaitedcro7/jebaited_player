<#
.SYNOPSIS
  Sinkronizacija titlova na stvarnim epizodama (SyncLab real).

.DESCRIPTION
  Za svaku epizodu iz testdata\real\cases.json:
  - referenca su ugrađeni PGS titlovi istog izdanja (ffprobe), neovisno o analizi zvuka;
  - za svaki titl uz video: presuda playera i greška po replici na početku, sredini i kraju;
  - titlovi drugih epizoda i titlovi označeni kao "wrong" moraju biti odbijeni.
  cases.json i predmemorija su u testdata\real (nisu u gitu jer sadrže putanje do tvojih videa).

  Primjer cases.json:
  {
    "ffprobe": "ffprobe",
    "episodes": [
      { "name": "E05", "video": "C:\\Video\\Serija.S01E05.mkv", "pgsStream": 0, "wrong": ["Serija.S01E05.bs.srt"] }
    ]
  }

.EXAMPLE
  .\tools\run-real-sync-tests.ps1 -Label nakon-popravka
#>
param(
    [string]$Cases = "testdata\real\cases.json",
    [string]$Label = (Get-Date -Format "yyyyMMdd-HHmm"),
    [switch]$Parts
)

$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet build tools\SyncLab\SyncLab.csproj -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "SyncLab se nije izgradio." }
    $extra = @()
    if ($Parts) { $extra += "--parts" }
    & "tools\SyncLab\bin\Release\net10.0\SyncLab.exe" real $Cases --out "testdata\real\rezultat-$Label.json" @extra
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
