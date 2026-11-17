<#
.SYNOPSIS
  Bilješke izdanja (engleski, njemački, hrvatski, datum i poveznica na prethodnu verziju) iz povijesti nadogradnji
  (src\JebaitedPlayer.App\History\update-history.json).

.DESCRIPTION
  Koriste ih tools\release.ps1 (Velopack --releaseNotes, a vpk upload ih stavi u GitHub Release) i GitHub Actions
  (.github\workflows\release.yml). Povijest se piše samo na jednom mjestu; player je prikazuje i bez interneta.

.EXAMPLE
  .\tools\release-notes.ps1 -Version 1.2.0 -Out Releases\notes-1.2.0.md
#>
param(
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [string]$Out
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$Out = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force -Path (Split-Path $Out -Parent) | Out-Null
$env:JEBAITED_RELEASE_NOTES_VERSION = $Version
$env:JEBAITED_RELEASE_NOTES_OUT = $Out
try {
    & dotnet test (Join-Path $root "tests\JebaitedPlayer.App.Tests\JebaitedPlayer.App.Tests.csproj") -c Release --nologo `
        --filter "FullyQualifiedName~Release_notes_file_for_the_release_script" | Out-Host
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $Out)) { throw "Bilješke za verziju $Version nisu napravljene (je li verzija u update-history.json?)." }
}
finally {
    Remove-Item Env:\JEBAITED_RELEASE_NOTES_VERSION, Env:\JEBAITED_RELEASE_NOTES_OUT -ErrorAction SilentlyContinue
}
"Bilješke izdanja $Version`: $Out"
