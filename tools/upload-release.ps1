<#
.SYNOPSIS
  Objavi izdanje (napravljeno s tools\release.ps1) na GitHub Releases. Nakon toga ga dobivaju svi korisnici
  kao nadogradnju, zato se pokreće samo ručno, uz odobrenje vlasnika repozitorija.

.DESCRIPTION
  Koristi prijavu GitHub CLI-ja (gh auth token); token se ne zapisuje nigdje.
  Prije slanja provjeri da u mapi postoje paketi baš ove verzije.

.EXAMPLE
  .\tools\upload-release.ps1 -Version 1.0.0
#>
param(
    [Parameter(Mandatory)] [string]$Version,
    [string]$OutputDir
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutputDir) { $OutputDir = Join-Path $root "Releases" }

$props = Get-Content (Join-Path $root "Directory.Build.props") -Raw
$repoUrl = ([regex]::Match($props, '<UpdateRepositoryUrl>([^<]+)</UpdateRepositoryUrl>')).Groups[1].Value
if (-not (Test-Path (Join-Path $OutputDir "JebaitedPlayer-$Version-full.nupkg"))) { throw "U $OutputDir nema paketa za verziju $Version (pokreni release.ps1)." }

$token = (& gh auth token).Trim()
if (-not $token) { throw "GitHub CLI nije prijavljen (gh auth login)." }

Push-Location $root
try {
    & dotnet vpk upload github --repoUrl $repoUrl --token $token --outputDir $OutputDir --publish `
        --releaseName "jebaited_player $Version" --tag "v$Version"
    if ($LASTEXITCODE -ne 0) { throw "Slanje izdanja nije uspjelo." }
}
finally {
    Pop-Location
    $token = $null
}
"Objavljeno: $repoUrl/releases/tag/v$Version"
