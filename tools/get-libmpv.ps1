<#
.SYNOPSIS
  Preuzme libmpv (engine za reprodukciju) koji koristi jebaited_player i provjeri SHA256.

.DESCRIPTION
  libmpv-2.dll (~116 MB) nije u gitu. Izvor, verzija i SHA256 su u third_party\libmpv\SOURCE.md.
  Arhiva je .7z: raspakira se s 7-Zipom (ako je instaliran) ili Windowsovim tar.exe (Windows 11 23H2+).
  Ako ispravna datoteka već postoji, ništa se ne preuzima.
.PARAMETER Destination
  Mapa za libmpv-2.dll (zadano third_party\libmpv, odakle ga uzima build).
#>
param([string]$Destination)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$sourceDir = Join-Path $root "third_party\libmpv"
$dir = if ($Destination) { [IO.Path]::GetFullPath($Destination) } else { $sourceDir }
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$source = Get-Content (Join-Path $sourceDir "SOURCE.md") -Raw
$url = ([regex]::Match($source, '\| URL \| (https://\S+\.7z) \|')).Groups[1].Value
$archiveSha = ([regex]::Match($source, 'SHA256 arhive \| `([0-9a-f]{64})`')).Groups[1].Value
$dllSha = ([regex]::Match($source, 'libmpv-2\.dll` \| `([0-9a-f]{64})`')).Groups[1].Value
if (-not $url -or -not $archiveSha -or -not $dllSha) { throw "SOURCE.md nema URL ili SHA256." }

$dll = Join-Path $dir "libmpv-2.dll"
if ((Test-Path $dll) -and (Get-FileHash $dll -Algorithm SHA256).Hash.ToLowerInvariant() -eq $dllSha) {
    "libmpv-2.dll je već tu i ispravan."
    return
}

$work = Join-Path ([IO.Path]::GetTempPath()) ("libmpv-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $archive = Join-Path $work "libmpv.7z"
    "Preuzimam $url"
    Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing
    if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $archiveSha) { throw "SHA256 arhive se ne podudara sa SOURCE.md." }

    $out = Join-Path $work "out"
    New-Item -ItemType Directory -Path $out | Out-Null
    $sevenZip = @("$env:ProgramFiles\7-Zip\7z.exe", "${env:ProgramFiles(x86)}\7-Zip\7z.exe") | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if ($sevenZip) { & $sevenZip x $archive "-o$out" libmpv-2.dll -y | Out-Null }
    else { & tar.exe -xf $archive -C $out libmpv-2.dll }
    $extracted = Join-Path $out "libmpv-2.dll"
    if (-not (Test-Path $extracted)) { throw "libmpv-2.dll nije raspakiran (treba 7-Zip ili Windows 11 23H2+)." }
    if ((Get-FileHash $extracted -Algorithm SHA256).Hash.ToLowerInvariant() -ne $dllSha) { throw "SHA256 od libmpv-2.dll se ne podudara sa SOURCE.md." }
    Copy-Item $extracted $dll -Force
    "libmpv-2.dll spremljen u $dir (SHA256 provjeren)."
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
