<#
.SYNOPSIS
  Preuzme libdave (DAVE E2EE za bota za Discord) koji koristi jebaited_player i provjeri SHA256.

.DESCRIPTION
  libdave.dll (~5 MB) nije u gitu. Izgrađen je s OpenSSL 3 u radnom toku .github\workflows\libdave.yml i objavljen
  kao pre-release. Izvor, verzije i SHA256 su u third_party\libdave\SOURCE.md.
  Provjerava se SHA256 zip arhive i SHA256 DLL-a. Ako ispravna datoteka već postoji, ništa se ne preuzima.
.PARAMETER Destination
  Mapa za libdave.dll (zadano third_party\libdave, odakle ga uzima build).
#>
param([string]$Destination)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$sourceDir = Join-Path $root "third_party\libdave"
$dir = if ($Destination) { [IO.Path]::GetFullPath($Destination) } else { $sourceDir }
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$source = Get-Content (Join-Path $sourceDir "SOURCE.md") -Raw
$url = ([regex]::Match($source, '\| URL \| (https://\S+\.zip) \|')).Groups[1].Value
$archiveSha = ([regex]::Match($source, 'SHA256 arhive \| `([0-9a-f]{64})`')).Groups[1].Value
$dllSha = ([regex]::Match($source, 'libdave\.dll` \| `([0-9a-f]{64})`')).Groups[1].Value
if (-not $url -or -not $archiveSha -or -not $dllSha) { throw "SOURCE.md nema URL ili SHA256." }

$dll = Join-Path $dir "libdave.dll"
if ((Test-Path $dll) -and (Get-FileHash $dll -Algorithm SHA256).Hash.ToLowerInvariant() -eq $dllSha) {
    "libdave.dll je već tu i ispravan."
    return
}

$work = Join-Path ([IO.Path]::GetTempPath()) ("libdave-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $archive = Join-Path $work "libdave.zip"
    "Preuzimam $url"
    Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing
    if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $archiveSha) { throw "SHA256 arhive se ne podudara sa SOURCE.md." }

    # Iz arhive se uzima samo libdave.dll (iz korijena zipa), ništa drugo se ne raspakira.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $extracted = Join-Path $work "libdave.dll"
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq "libdave.dll" } | Select-Object -First 1
        if (-not $entry) { throw "U arhivi nema libdave.dll." }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $extracted, $true)
    }
    finally { $zip.Dispose() }
    if ((Get-FileHash $extracted -Algorithm SHA256).Hash.ToLowerInvariant() -ne $dllSha) { throw "SHA256 od libdave.dll se ne podudara sa SOURCE.md." }
    Copy-Item $extracted $dll -Force
    "libdave.dll spremljen u $dir (SHA256 provjeren)."
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
