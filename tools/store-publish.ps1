<#
.SYNOPSIS
  Microsoft Store: probni prolaz ili predaja MSIX paketa preko msstore CLI-ja.

.DESCRIPTION
  Podaci za prijavu čitaju se iz secrets\store-api.env (TENANT_ID, CLIENT_ID, CLIENT_SECRET, SELLER_ID); nikad se ne ispisuju.
  Bez -Submit samo se prijavi i pročita stanje aplikacije u Storeu (ništa se ne šalje).
  -Submit stvarno predaje paket i traži ručnu potvrdu (upis YES). Pokreće se samo uz odobrenje vlasnika.

.EXAMPLE
  .\tools\store-publish.ps1 -Version 2.0.6              # probni prolaz
  .\tools\store-publish.ps1 -Version 2.0.6 -Submit      # predaja (uz potvrdu)
#>
param(
    [Parameter(Mandatory)] [string]$Version,
    [switch]$Submit,
    [switch]$Yes   # bez ručnog upisa YES (samo za zakazanu predaju koju je vlasnik odobrio)
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$productId = "9NP4GF0Q40JL"

$local = Join-Path $root "tools\msstore\bin\msstore.exe"   # službeni MSStoreCLI-win-x64.zip (v0.4.4, SHA256 provjeren), u .gitignore
$msstore = if (Test-Path $local) { $local } elseif (Get-Command msstore -ErrorAction SilentlyContinue) { "msstore" } else { throw "msstore nije pronađen ($local)." }
$envFile = Join-Path $root "secrets\store-api.env"
if (-not (Test-Path $envFile)) { throw "Nema $envFile." }
$cfg = @{}
foreach ($l in Get-Content $envFile) { if ($l -match '^\s*([A-Z_]+)\s*=\s*(.*?)\s*$') { $cfg[$Matches[1]] = $Matches[2] } }
foreach ($k in "TENANT_ID", "CLIENT_ID", "CLIENT_SECRET", "SELLER_ID") { if (-not $cfg[$k]) { throw "U store-api.env nedostaje $k." } }

$msix = Join-Path $root "Releases\msix\jebaited_player_$Version.0_x64.msix"
if (-not (Test-Path $msix)) { throw "Nema paketa $msix (pokreni tools\make-msix.ps1)." }
$hash = (Get-FileHash $msix -Algorithm SHA256).Hash
"Paket: $msix"
"SHA256: $hash"
$plan = Get-Content (Join-Path $root "docs\PLAN_SAC.md") -Raw
if ($plan -notmatch [regex]::Escape($hash)) { Write-Warning "SHA256 paketa nije zapisan u docs\PLAN_SAC.md (paket je mijenjan nakon testiranja?)." }

# Prijava (tajna se prosljeđuje CLI-ju; ne ispisuje se).
& $msstore reconfigure --tenantId $cfg.TENANT_ID --sellerId $cfg.SELLER_ID --clientId $cfg.CLIENT_ID --clientSecret $cfg.CLIENT_SECRET
if ($LASTEXITCODE -ne 0) { throw "msstore reconfigure nije uspio." }

"--- Stanje aplikacije u Storeu ($productId) ---"
$info = (& $msstore apps get $productId | Out-String)
if ($LASTEXITCODE -ne 0) { throw "msstore apps get nije uspio." }
$info
# Predaja u tijeku (certifikacija): publish bi obrisao nacrt i napravio novi, pa se tada ne predaje (izlaz 3).
$pending = $info -match '"PendingApplicationSubmission"\s*:\s*\{'

if (-not $Submit) { "Predaja u tijeku: $pending"; "Probni prolaz gotov. Ništa nije predano."; return }
if ($pending) { "ČEKA: u Storeu je predaja u tijeku (certifikacija). Ništa nije predano."; exit 3 }

if (-not $Yes) {
    $answer = Read-Host "Predati $Version u Store (aplikacija $productId)? Upiši YES"
    if ($answer -cne "YES") { "Odustao."; return }
}
& $msstore publish (Split-Path $msix -Parent) -i $msix -id $productId
if ($LASTEXITCODE -ne 0) { throw "msstore publish nije uspio." }
"Predano. Stanje: msstore submission status $productId"
