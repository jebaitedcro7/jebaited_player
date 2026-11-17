<#
.SYNOPSIS
  Testovi titlova s interneta (M4) u pravom playeru, uz lažni izvor (bez mreže i ključeva):
  automatsko preuzimanje istog izdanja, ponuda nesigurnih rezultata, "nema rezultata", "nema ključeva".
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$m4 = Join-Path $root "testdata\m4"
New-Item -ItemType Directory -Force -Path $m4 | Out-Null
$video = Join-Path $m4 "Online.Test.2026.1080p.WEB-DL.x264-GRP.mp4"
if (-not (Test-Path -LiteralPath $video)) { Copy-Item -LiteralPath (Join-Path $root "testdata\m1\1080p_h264_aac.mp4") -Destination $video }
$expectedText = "Preuzeto s interneta: čćžšđ ČĆŽŠĐ"

function Clean { Get-ChildItem $m4 -Filter "*.srt" -ErrorAction SilentlyContinue | Remove-Item }
function Run([string]$Name, [string]$Extra) {
    $null = Invoke-Diag -Name ("ONLINE " + $Name) -File @($video) -Seconds 1 -ExtraArgs $Extra -TimeoutSeconds 60
    $json = Join-Path $script:DiagDir ((("ONLINE " + $Name) -replace '[^\w\-]+', '_') + ".json")
    if (Test-Path -LiteralPath $json) { return Get-Content -LiteralPath $json -Raw -Encoding UTF8 | ConvertFrom-Json }
    return $null
}

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Rezultat = $(if ($Ok) { "OK" } else { "GREŠKA" }); Test = $Name; Detalji = $Detail })
}

# 1) Isto izdanje: automatski preuzmi, spremi uz video, učitaj.
Clean
$r = Run "Sigurno" "--diag-fake-online sure --diag-probe 5"
$saved = Get-ChildItem $m4 -Filter "*.srt" | Select-Object -ExpandProperty Name
$ok = ($r.online.lastAutoResult -like "auto:*hr*") -and ($saved -contains "Online.Test.2026.1080p.WEB-DL.x264-GRP.hr.srt") -and ($r.subtitleProbe.'sub-text' -eq $expectedText)
Check "Isto izdanje: preuzeto, spremljeno uz video i učitano" $ok "auto='$($r.online.lastAutoResult)'; spremljeno: $($saved -join ', '); prikazano '$($r.subtitleProbe.'sub-text')' ($($r.subtitleProbe.selectedEncoding))"

# 2) Ponovno otvaranje: titl se uzima s diska, pretraga se ne ponavlja.
$r = Run "Ponovno" "--diag-fake-online sure --diag-probe 5"
Check "Ponovno otvaranje: bez nove pretrage" ([string]::IsNullOrEmpty($r.online.lastAutoResult) -and $r.subtitleProbe.'sub-text' -eq $expectedText) "auto='$($r.online.lastAutoResult)', prikazano '$($r.subtitleProbe.'sub-text')'"

# 3) Nesiguran rezultat: samo ponuda, ništa se ne učitava ni sprema.
Clean
$r = Run "Nesigurno" "--diag-fake-online unsure --diag-probe 5"
$saved = @(Get-ChildItem $m4 -Filter "*.srt")
$ok = ($r.online.lastAutoResult -like "ponuđeno*") -and ($saved.Count -eq 0) -and [string]::IsNullOrEmpty($r.subtitleProbe.'sub-text') -and (@($r.online.results).Count -eq 1)
Check "Nesiguran: ponuđen, ne učitava se sam" $ok "auto='$($r.online.lastAutoResult)'; rezultati: $(@($r.online.results) -join ' | '); spremljeno datoteka: $($saved.Count)"

# 4) Nema rezultata.
$r = Run "Nema" "--diag-fake-online none --diag-probe 5"
Check "Nema rezultata" ($r.online.lastAutoResult -eq "nema rezultata") "auto='$($r.online.lastAutoResult)'"

# 5) Bez izvora (dijagnostika bez --diag-online-live nikad ne ide na mrežu): ne traži.
$r = Run "Bez kljuceva" "--diag-probe 5"
Check "Bez izvora: nema pretrage" ((-not $r.online.anyProvider) -and [string]::IsNullOrEmpty($r.online.lastAutoResult)) "ima izvora: $($r.online.anyProvider); auto='$($r.online.lastAutoResult)'"

Clean
$results
"{0} / {1} prošlo" -f @($results | Where-Object Rezultat -eq "OK").Count, $results.Count
