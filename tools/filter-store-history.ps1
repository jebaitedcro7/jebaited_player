<#
.SYNOPSIS
  Store (MSIX) izdanje je UVIJEK bez Torlinka (trajno pravilo, docs/RELEASE_CHECKLIST.md). Povijest nadogradnji ugrađena u program
  zato se za Store build prepisuje bez verzija označenih "scope": "github" (bilješke koje opisuju samo GitHub funkcije, npr. Torlink).
  Poziva ga JebaitedPlayer.App.csproj kad je TorlinkEnabled=false; GitHub build ugrađuje cijelu povijest.
#>
param(
    [Parameter(Mandatory)] [string]$In,
    [Parameter(Mandatory)] [string]$Out
)
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
$json = [IO.File]::ReadAllText($In, $utf8) | ConvertFrom-Json
$kept = @($json.versions | Where-Object { $_.scope -ne 'github' })
$json.versions = $kept
$text = $json | ConvertTo-Json -Depth 30
if ($text -match 'Torlink') { throw "Torlink je ostao u povijesti za Store: oznaci te verzije sa ""scope"": ""github""." }
New-Item -ItemType Directory -Force -Path (Split-Path $Out -Parent) | Out-Null
[IO.File]::WriteAllText($Out, $text, $utf8)
Write-Host "Povijest za Store: $($kept.Count) verzija (izbaceno $(@($json.versions).Count - $kept.Count))."
