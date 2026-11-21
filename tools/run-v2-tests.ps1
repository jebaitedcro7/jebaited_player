<#
.SYNOPSIS
  Testovi 2.0 u pravom playeru (testna mapa podataka):
  - Prečaci (--diag-ui-test shortcuts): kartica Prečaci, nova tipka i stvarna radnja, sukob i zamjena, rezervirana tipka,
    Esc, Delete, F1, Spremi, Zadano.

.EXAMPLE
  .\tools\run-v2-tests.ps1
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Test = $Name; Rezultat = $(if ($Ok) { "OK" } else { "GREŠKA" }); Detalji = $Detail })
}
function New-Data([string]$Name) {
    $d = Join-Path $root "testdata\v2\$Name"
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $d | Out-Null
    [IO.File]::WriteAllText((Join-Path $d "settings.json"), '{ "Language": "hr" }')
    return $d
}
function Read-Ui([string]$Name) {
    $u = (Get-Content -LiteralPath (Join-Path $script:DiagDir "$Name.json") -Raw -Encoding UTF8 | ConvertFrom-Json).uiTest
    if (-not $u -or $u.exception -or $u.error) { throw "$Name`: $($u.exception)$($u.error)" }
    return $u
}

# --- Prečaci ------------------------------------------------------------------------------------------------------------
$env:JEBAITED_DATA_DIR = New-Data "data-shortcuts"
try { $null = Invoke-Diag -Name "v2_shortcuts" -File (Join-Path $root "testdata\m1\1080p_h264_aac.mp4") -Seconds 1 -ExtraArgs "--diag-ui-test shortcuts" -TimeoutSeconds 120 }
finally { Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue }
$s = Read-Ui "v2_shortcuts"
Check "Prečaci: kartica sa svim radnjama" ($s.tab -eq "Shortcuts" -and [int]$s.rows -ge 46 -and $s.firstButton -eq "Razmaknica") "kartica $($s.tab), redaka $($s.rows), prva tipka '$($s.firstButton)'"
Check "Prečaci: snimanje nove tipke (U) i stvarna radnja; stara tipka i dalje radi" ($s.capturing -eq "Pritisni tipku…" -and $s.set -match "sada koristi U" -and $s.uWorks -and $s.upStillWorks) "'$($s.set)'"
Check "Prečaci: sukob najprije upozori, drugi pritisak premjesti" ($s.conflict -match "već koristi" -and $s.conflictKept -eq "toggleSubtitles" -and $s.moved -match "maknuto s" -and $s.vNow -eq "volumeUp" -and $null -eq $s.upNow -and $s.toggleSubtitlesKeys -eq "") "'$($s.conflict)' → '$($s.moved)'"
Check "Prečaci: rezervirana tipka odbijena, Esc odustaje bez promjene" ($s.reserved -match "stalnu ulogu" -and $s.escape -eq "" -and $s.muteAfterEscape -eq "M") "'$($s.reserved)'"
Check "Prečaci: Delete briše tipku (M više ne utišava)" ($s.cleared -match "uklonjena" -and -not $s.mAfterClear) "'$($s.cleared)'"
Check "Prečaci: F1 i izbornik pokazuju stvarne tipke (u paru radnji prva tipka svake)" ($s.f1Volume -match "^V / ↓" -and -not $s.f1HasMute -and $s.menuGesture -eq "V") "F1 glasnoća '$($s.f1Volume)', izbornik '$($s.menuGesture)'"
Check "Prečaci: Spremi zapisuje samo promjene" ($s.savedVolumeUp -and $s.savedMute -and $s.savedOnlyChanges) "volumeUp $($s.savedVolumeUp), mute $($s.savedMute), samo promjene $($s.savedOnlyChanges)"
Check "Prečaci: Zadano za radnju i za cijelu karticu" ($s.muteReset -eq "M" -and $s.mWorksAgain -eq "mute" -and [int]$s.afterDefaults -eq 0 -and $s.vAfter -eq "toggleSubtitles" -and $s.upAfter -eq "volumeUp" -and $null -eq $s.uAfter) "nakon Zadano: promjena $($s.afterDefaults), V=$($s.vAfter), ↑=$($s.upAfter)"

# --- Boja isticanja ------------------------------------------------------------------------------------------------------
$env:JEBAITED_DATA_DIR = New-Data "data-accent"
try { $null = Invoke-Diag -Name "v2_accent" -File (Join-Path $root "testdata\m1\audio_only.mp3") -Seconds 1 -ExtraArgs "--diag-ui-test accent" -TimeoutSeconds 120 }
finally { Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue }
$a = Read-Ui "v2_accent"
Check "Boja: početno zadana zelena, 8 gotovih boja" ($a.start.accent -eq "#7BD88F" -and $a.start.lcd -eq "#7BD88F" -and $a.start.saved -eq "" -and $a.start.swatches -match "^green\*" -and @($a.start.swatches -split ",").Count -eq 8) "$($a.start.accent), kružići $($a.start.swatches)"
Check "Boja: gotova boja (plava) odmah u playeru, mini LCD-u i VU metru" ($a.blue.accent -eq $a.blueExpected -and $a.blue.lcd -eq "#6CB4FF" -and $a.blue.vu -eq "#6CB4FF" -and $a.blue.saved -eq "#6CB4FF" -and $a.blue.swatches -match "blue\*") "naglasak $($a.blue.accent), LCD $($a.blue.lcd), VU $($a.blue.vu), tekst $($a.blue.foreground), označeno $($a.blue.selection)"
Check "Boja: svijetla tema potamni boju (čitljivo), tamna je vrati" ($a.light.accent -ne $a.blue.accent -and [double]$a.lightContrast -ge 3 -and $a.light.lcd -eq "#6CB4FF" -and $a.backDark.accent -eq $a.blue.accent) "svijetla $($a.light.accent) (kontrast $($a.lightContrast)), tamna $($a.backDark.accent)"
Check "Boja: birač pregled uživo, Odustani vraća prijašnju" ($a.pickerHex -eq "#FF00AA" -and $a.livePreview.lcd -ne "#6CB4FF" -and $a.livePreview.accent -ne $a.blue.accent -and $a.pickerAfterHue -ne "#FF00AA" -and $a.afterCancel.accent -eq $a.blue.accent -and $a.afterCancel.saved -eq "#6CB4FF") "pregled $($a.livePreview.accent), nijansa 30 → $($a.pickerAfterHue), nakon Odustani $($a.afterCancel.accent)"
Check "Boja: vlastita boja ima svoj kružić i sprema se" ($a.custom.saved -eq "#FF00AA" -and $a.custom.swatches -match "custom\*" -and $a.savedInFile) "spremljeno $($a.custom.saved), kružići $($a.custom.swatches)"
Check "Boja: Zadano (Izgled) vraća zelenu teme" ($a.afterDefaults.accent -eq "#7BD88F" -and $a.afterDefaults.foreground -eq "#0B1A0F" -and $a.afterDefaults.selection -eq "#2E4A35" -and $a.afterDefaults.lcd -eq "#7BD88F" -and $a.afterDefaults.saved -eq "" -and $a.afterDefaults.swatches -match "^green\*" -and $a.afterDefaults.swatches -notmatch "custom" -and $a.overrideRemoved) "$($a.afterDefaults.accent), LCD $($a.afterDefaults.lcd), kružići $($a.afterDefaults.swatches)"

$results | Format-Table -AutoSize -Wrap
$failed = @($results | Where-Object { $_.Rezultat -ne "OK" }).Count
Write-Host ("Ukupno {0}, uspješno {1}, grešaka {2}" -f $results.Count, ($results.Count - $failed), $failed)
if ($failed -gt 0) { exit 1 }
