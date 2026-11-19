<#
.SYNOPSIS
  Testovi ciklusa 1 u pravom playeru:
  - V2 Equalizer: kartica Zvuk (11 klizača, uključivanje, klizač → vlastiti preset), svi preseti stižu u mpv
    bez prekida reprodukcije, zajedno s ujednačavanjem glasnoće, stvarna promjena razine tona (astats), CPU A/B;
  - M6a/M6b: premotavanje do kraja ne označava pogledano, gledanje do kraja označava, ponovno otvaranje i zatvaranje
    na 10 % ne poništava status, natpis Watched i puna traka na početnom ekranu, ručna oznaka.
  Zasebna testna baza po pokretanju; tvoje postavke i povijest se ne mijenjaju.

.EXAMPLE
  .\tools\run-ciklus1-tests.ps1
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$ffmpeg = if ($env:FFMPEG) { $env:FFMPEG } else { "ffmpeg" }

$film = Join-Path $root "testdata\upgrade3\Film.120min.2026.1080p.WEB.mkv"
if (-not (Test-Path $film)) { throw "Nema testnog filma: pokreni prvo tools\run-upgrade3-tests.ps1 (stvara ga)." }

$dir = Join-Path $root "testdata\ciklus1"
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$clip = Join-Path $dir "Kratki.Film.2026.mkv"
if (-not (Test-Path $clip)) {
    & $ffmpeg -hide_banner -loglevel error -y -f lavfi -i "testsrc2=size=640x360:rate=24" -f lavfi -i "sine=frequency=440:sample_rate=48000" `
        -t 60 -c:v libx264 -preset ultrafast -crf 36 -g 48 -c:a aac -b:a 48k $clip
    if ($LASTEXITCODE -ne 0) { throw "FFmpeg nije uspio (kratki film)" }
}

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Test = $Name; Rezultat = $(if ($Ok) { "OK" } else { "GREŠKA" }); Detalji = $Detail })
}

function Invoke-UiTest([string]$Name, [string]$Scenario, [string]$File) {
    $db = Join-Path $script:DiagDir "ciklus1-$Name.db"
    Remove-Item "$db*" -ErrorAction SilentlyContinue
    $extra = "--diag-ui-test $Scenario --diag-db `"$db`""
    $summary = Invoke-Diag -Name "ciklus1_$Name" -File $File -Seconds 1 -ExtraArgs $extra -TimeoutSeconds 300
    $json = Join-Path $script:DiagDir "ciklus1_$Name.json"
    $r = if (Test-Path -LiteralPath $json) { Get-Content -LiteralPath $json -Raw -Encoding UTF8 | ConvertFrom-Json } else { $null }
    if (-not $r -or -not $r.uiTest) { throw "$Name`: nema rezultata ($($summary.Error))" }
    if ($r.uiTest.exception) { throw "$Name`: $($r.uiTest.exception)" }
    return $r.uiTest
}

# --- V2 Equalizer ---------------------------------------------------------------------------------------------
$e = Invoke-UiTest "equalizer" "equalizer" $film
Check "EQ: kartica Zvuk ima preamp + 10 pojaseva" ($e.columns -eq 11 -and ($e.labels -join ",") -match "31,62,125,250,500,1k,2k,4k,8k,16k$") "stupaca $($e.columns): $($e.labels -join ', ')"
Check "EQ: uključivanje iz sučelja" (-not $e.panelEnabledBefore -and $e.panelEnabledAfter -and $e.afFlat -eq "") "prije $($e.panelEnabledBefore), poslije $($e.panelEnabledAfter), Flat af='$($e.afFlat)'"
$s = $e.slider
Check "EQ: klizač 31 Hz → vlastiti preset, odmah u mpv-u" ($s.preset -eq "custom" -and $s.af -match "equalizer=f=31:t=o:w=1:g=6" -and $s.af -match "volume=-6dB" -and -not $s.paused -and [double]$s.advanced -gt 0.5) "preset $($s.preset) ($($s.presetButton)), af=$($s.af), napredak $($s.advanced) s"
foreach ($p in $e.presets.PSObject.Properties) {
    $v = $p.Value
    $inner = ($v.expected -replace '^lavfi=\[', '' -replace '\]$', '')
    $okAf = if ($v.expected -eq "") { $v.af -eq "" } else { $v.af -match [regex]::Escape($inner) }
    Check "EQ: preset $($p.Name) u mpv-u bez prekida" ($okAf -and -not $v.paused -and [double]$v.advanced -gt 0.4 -and $v.ao) "af=$($v.af), napredak $($v.advanced) s, ao=$($v.ao)"
}
Check "EQ: zajedno s ujednačavanjem (jedan graf, EQ prije dynaudnorm)" ($e.withNormalize -match "equalizer=.*dynaudnorm") "af=$($e.withNormalize)"
$l = $e.level
Check "EQ: stvarna promjena zvuka (440 Hz, pojas 500 Hz −12 dB)" ($null -ne $l.differenceDb -and [double]$l.differenceDb -le -6) "bez EQ $($l.off) dB, s EQ $($l.cut500) dB, razlika $($l.differenceDb) dB $($l.offRaw)$($l.cut500Raw)"
$off = @($e.cpu | Where-Object { -not $_.eq } | ForEach-Object { [double]$_.cpuPercentOfOneCore })
$on = @($e.cpu | Where-Object { $_.eq } | ForEach-Object { [double]$_.cpuPercentOfOneCore })
$avgOff = ($off | Measure-Object -Average).Average
$avgOn = ($on | Measure-Object -Average).Average
Check "EQ: CPU ne raste primjetno (Rock, 10 pojaseva)" (($avgOn - $avgOff) -le 2) ("bez EQ {0:N2} %, s EQ {1:N2} % jedne jezgre (mjerenja: {2} / {3})" -f $avgOff, $avgOn, ($off -join ", "), ($on -join ", "))
Check "EQ: isključivanje vraća zvuk kakav je u datoteci" ($e.afOff -eq "" -and -not $e.pausedAtEnd) "af='$($e.afOff)'"

# --- M6a / M6b --------------------------------------------------------------------------------------------------
$w = Invoke-UiTest "watched" "watched" $clip
Check "M6b: skok na kraj nije pogledano" (-not $w.skipToEnd.watched -and [double]$w.skipToEnd.coverage -lt 0.2 -and [double]$w.skipToEnd.watchedProgress -lt 0.2) "pogledano $($w.skipToEnd.watched), odgledano $($w.skipToEnd.coverage), traka $($w.skipToEnd.watchedProgress)"
Check "M6b: gledanje do kraja = pogledano" ($w.watchedToEnd.watched -and [double]$w.watchedToEnd.coverage -ge 0.8 -and [double]$w.watchedToEnd.watchedProgress -eq 1) "pogledano $($w.watchedToEnd.watched), odgledano $($w.watchedToEnd.coverage), pozicija $($w.watchedToEnd.position)"
Check "M6a: ponovno otvaranje kreće od početka" ([double]$w.reopenStartedAt -lt 3 -and $null -eq $w.reopenResumeOffer) "počelo na $($w.reopenStartedAt) s, ponuda nastavka $($w.reopenResumeOffer)"
Check "M6a: zatvoreno na 10 % ostaje pogledano" ($w.afterReopen.watched -and [double]$w.afterReopen.watchedProgress -eq 1) "pogledano $($w.afterReopen.watched), traka $($w.afterReopen.watchedProgress), pozicija $($w.afterReopen.position)"
$rec = $w.recent | Select-Object -First 1
Check "M6b: početni ekran: natpis Watched i puna traka" ($rec.watched -and [double]$rec.progress -eq 1 -and $rec.detail -eq $w.watchedText) "detalj '$($rec.detail)', traka $($rec.progress), snimka $($w.homeScreenshot)"
Check "Ručno: nepogledano briše traku" (-not $w.manualUnwatched.watched -and $null -eq $w.manualUnwatched.coverage -and -not $w.playlistAfterUnwatch.watched -and [double]$w.playlistAfterUnwatch.progress -eq 0) "baza $($w.manualUnwatched.watched)/$($w.manualUnwatched.coverage), popis $($w.playlistAfterUnwatch.watched)/$($w.playlistAfterUnwatch.progress)"
Check "Ručno: pogledano vraća natpis i punu traku" ($w.manualWatched.watched -and $w.playlistAfterWatch.watched -and [double]$w.playlistAfterWatch.progress -eq 1 -and $w.playlistAfterWatch.text -eq $w.playlistWatchedText) "baza $($w.manualWatched.watched), popis $($w.playlistAfterWatch.watched)/$($w.playlistAfterWatch.progress) '$($w.playlistAfterWatch.text)'"

$results | Format-Table -AutoSize -Wrap
$failed = @($results | Where-Object { $_.Rezultat -ne "OK" }).Count
Write-Host ("Ukupno {0}, uspješno {1}, grešaka {2}" -f $results.Count, ($results.Count - $failed), $failed)
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $script:DiagDir "ciklus1_rezultati.json") -Encoding UTF8
if ($failed -gt 0) { exit 1 }
