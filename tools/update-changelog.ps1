<#
.SYNOPSIS
  Iz povijesti nadogradnji (src\JebaitedPlayer.App\History\update-history.json) ponovno napiše
  CHANGELOG.md (engleski), CHANGELOG_DE.md i CHANGELOG_HR.md.

.DESCRIPTION
  Povijest se piše samo na jednom mjestu (update-history.json): iz nje player prikazuje povijest (i bez interneta),
  a iz nje nastaju i CHANGELOG datoteke i bilješke izdanja na GitHubu (tools\release.ps1).
  Jedinični test Changelog_files_match_the_history provjerava da su datoteke ažurne; ova skripta ih zapiše.

.EXAMPLE
  .\tools\update-changelog.ps1
#>
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$env:JEBAITED_WRITE_CHANGELOG = "1"
try {
    & dotnet test (Join-Path $root "tests\JebaitedPlayer.App.Tests\JebaitedPlayer.App.Tests.csproj") -c Debug --nologo `
        --filter "FullyQualifiedName~Changelog_files_match_the_history" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "CHANGELOG nije zapisan (test nije prošao)." }
}
finally {
    Remove-Item Env:\JEBAITED_WRITE_CHANGELOG -ErrorAction SilentlyContinue
}
"CHANGELOG.md, CHANGELOG_DE.md i CHANGELOG_HR.md su ažurni."
