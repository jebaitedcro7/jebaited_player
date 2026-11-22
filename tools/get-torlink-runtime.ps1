<#
.SYNOPSIS
  Preuzme (ili uzme lokalni) Torlink runtime zip, provjeri SHA-256 i raspakira ga u zadanu mapu (za razvoj i testove).

.DESCRIPTION
  Isti obrazac kao tools\get-libdave.ps1: URL i SHA-256 su u third_party\torlink\SOURCE.md.
  Player sam ne koristi ovu skriptu: sam preuzima i provjerava paket (RuntimeStore/Updater) kad korisnik uključi Torlink.
.PARAMETER Destination
  Mapa u koju se raspakira runtime (mora biti prazna ili nepostojeca).
.PARAMETER Zip
  Lokalni zip (npr. iz tools\build-torlink-runtime.ps1); preskace preuzimanje. SHA-256 se i dalje provjerava prema MANIFEST.json.
#>
param(
    [Parameter(Mandatory = $true)][string]$Destination,
    [string]$Zip
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dest = [IO.Path]::GetFullPath($Destination)
if ((Test-Path $dest) -and (Get-ChildItem $dest -Force | Select-Object -First 1)) { throw "Odredisna mapa nije prazna: $dest" }
New-Item -ItemType Directory -Force -Path $dest | Out-Null

if (-not $Zip) {
    $source = Get-Content (Join-Path $root 'third_party\torlink\SOURCE.md') -Raw
    $url = ([regex]::Match($source, '\| URL \(planiran\) \| (https://\S+\.zip) \|')).Groups[1].Value
    $sha = ([regex]::Match($source, 'SHA256 arhive[^|]*\| `([0-9a-f]{64})`')).Groups[1].Value
    if (-not $url -or -not $sha) { throw 'SOURCE.md nema URL ili SHA-256.' }
    $Zip = Join-Path ([IO.Path]::GetTempPath()) ('torlink-runtime-' + [Guid]::NewGuid().ToString('N') + '.zip')
    "Preuzimam $url"
    Invoke-WebRequest -Uri $url -OutFile $Zip -UseBasicParsing
    if ((Get-FileHash $Zip -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sha) { throw 'SHA-256 arhive se ne podudara sa SOURCE.md.' }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory((Resolve-Path $Zip).Path, $dest)

# Provjera svake datoteke prema MANIFEST.json
$manifest = Get-Content (Join-Path $dest 'MANIFEST.json') -Raw | ConvertFrom-Json
$bad = 0
foreach ($p in $manifest.files.PSObject.Properties) {
    $f = Join-Path $dest ($p.Name -replace '/', '\')
    if (-not (Test-Path $f) -or (Get-FileHash $f -Algorithm SHA256).Hash.ToLowerInvariant() -ne $p.Value) { $bad++; Write-Host "NE PODUDARA SE: $($p.Name)" }
}
if ($bad -gt 0) { throw "$bad datoteka ne odgovara manifestu." }
"Torlink $($manifest.torlinkVersion) r$($manifest.revision) (Node $($manifest.nodeVersion)) u $dest, sve datoteke provjerene."
