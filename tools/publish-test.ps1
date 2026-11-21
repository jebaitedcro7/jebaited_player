<#
.SYNOPSIS
  Testna instalacija: samostalni paket (win-x64) u zasebnu mapu, bez ikakvih promjena u sustavu.

.DESCRIPTION
  - Paket sadrži vlastiti .NET (ne treba SDK ni instalirani .NET), mpv (libmpv-2.dll), detektor govora i SQLite.
  - Ne stvara prečace, ne registrira formate, ne piše u registar. Briše se brisanjem mape.
  - Postavke, povijest gledanja i ključevi ostaju u %LOCALAPPDATA%\jebaited_player (isti kao razvojna verzija).
  - Prvo pokretanje na novom računalu preuzme .NET runtime pakete s nuget.org (~50–60 MB, jednom).

.EXAMPLE
  .\tools\publish-test.ps1
  .\tools\publish-test.ps1 -Target "E:\jebaited_player_test"
#>
param(
    [string]$Target = (Join-Path (Split-Path $PSScriptRoot -Parent) "publish\test")
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

if (Get-Process jebaited_player -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($Target, [StringComparison]::OrdinalIgnoreCase) }) {
    throw "Player iz $Target je otvoren. Zatvori ga pa pokreni ponovno."
}

if (Test-Path $Target) {
    # Briše se samo stara verzija paketa (prepoznaje se po exe-u), nikad neka druga mapa.
    if (-not (Test-Path (Join-Path $Target "jebaited_player.exe"))) { throw "$Target postoji, a nije testna instalacija playera. Odaberi drugu mapu." }
    Remove-Item $Target -Recurse -Force
}

dotnet publish (Join-Path $root "src\JebaitedPlayer.App\JebaitedPlayer.App.csproj") -c Release -r win-x64 --self-contained true -o $Target --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Objava nije uspjela." }

$mpv = (Get-FileHash (Join-Path $Target "libmpv-2.dll") -Algorithm SHA256).Hash.ToLowerInvariant()
$expected = ([regex]::Match((Get-Content (Join-Path $root "third_party\libmpv\SOURCE.md") -Raw), 'libmpv-2\.dll` \| `([0-9a-f]{64})')).Groups[1].Value
if ($mpv -ne $expected) { throw "libmpv-2.dll u paketu nema očekivani SHA256 ($mpv)." }

$daveFile = Join-Path $Target "libdave.dll"
if (Test-Path $daveFile) {
    $dave = (Get-FileHash $daveFile -Algorithm SHA256).Hash.ToLowerInvariant()
    $expectedDave = ([regex]::Match((Get-Content (Join-Path $root "third_party\libdave\SOURCE.md") -Raw), 'libdave\.dll` \| `([0-9a-f]{64})')).Groups[1].Value
    if ($dave -ne $expectedDave) { throw "libdave.dll u paketu nema očekivani SHA256 ($dave)." }
}
else { "Upozorenje: libdave.dll nije u paketu, pa bot za Discord neće raditi (tools\get-libdave.ps1)." }

$size = (Get-ChildItem $Target -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
"Testna instalacija: $Target ({0:N0} MB, verzija {1})" -f $size, (Get-Item (Join-Path $Target "jebaited_player.exe")).VersionInfo.ProductVersion
"Pokretanje: $Target\jebaited_player.exe (datoteku ili mapu možeš i povući na prozor)"
