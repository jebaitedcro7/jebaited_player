<#
.SYNOPSIS
  Testovi M6 u pravom playeru, kroz tipkovničke prečace (isti put kao prava tipkovnica):
  audio trake (i pamćenje po videu), brzina reprodukcije, omjer slike, snimka zaslona, omiljeni, sljedeća epizoda bez
  automatskog pokretanja. Zasebna testna baza; snimke idu u privremenu mapu.
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$ffmpeg = if ($env:FFMPEG) { $env:FFMPEG } else { "ffmpeg" }
$m6 = Join-Path $root "testdata\m6"
New-Item -ItemType Directory -Force -Path $m6 | Out-Null
$db = Join-Path $script:DiagDir "m6-test.db"
Remove-Item "$db*" -ErrorAction SilentlyContinue
$shots = Join-Path ([IO.Path]::GetTempPath()) "jebaited_player_diag\screenshots"
if (Test-Path $shots) { Remove-Item $shots -Recurse -Force }

# Testni video s tri audio trake: hrvatski stereo (AAC), engleski 5.1 (AC3), komentar mono (AAC).
$audioTest = Join-Path $m6 "Audio.Test.2026.mkv"
if (-not (Test-Path $audioTest)) {
    & $ffmpeg -hide_banner -loglevel error -y -f lavfi -i "testsrc2=size=1280x720:rate=25:duration=60" `
        -f lavfi -i "sine=f=440:d=60" -f lavfi -i "sine=f=660:d=60" -f lavfi -i "sine=f=880:d=60" `
        -map 0:v -map 1:a -map 2:a -map 3:a -c:v libx264 -preset ultrafast -crf 30 `
        -c:a:0 aac -ac:a:0 2 -c:a:1 ac3 -ac:a:1 6 -c:a:2 aac -ac:a:2 1 `
        -metadata:s:a:0 language=hrv -metadata:s:a:1 language=eng -metadata:s:a:2 language=hrv -metadata:s:a:2 "title=Komentar redatelja" `
        $audioTest
    if ($LASTEXITCODE -ne 0) { throw "FFmpeg nije uspio (audio test)" }
}
$video1080 = Join-Path $root "testdata\m1\1080p_h264_aac.mp4"
$subHr = Join-Path $root "testdata\m1\sub_hr.srt"
$long = Join-Path $root "testdata\m5\sync_film.mp4"
$m3 = Join-Path $root "testdata\m3"
$film = Join-Path $m3 "Filmovi\Film.Za.Test.2026.1080p.BluRay.x264-GRP.mp4"
$e2 = Join-Path $m3 "Serija.Test\Season 01\Serija.Test.S01E02.Drugi.Dan.720p.WEB-DL.x264-GRP.mp4"
$e3 = Join-Path $m3 "Serija.Test\Season 01\Serija.Test.S01E03.Kraj.720p.WEB-DL.x264-GRP.mp4"

function Run([string]$Name, [string[]]$Files, [string]$Extra, [int]$Seconds = 1) {
    $null = Invoke-Diag -Name ("M6 " + $Name) -File $Files -Seconds $Seconds -ExtraArgs "--diag-db `"$db`" $Extra" -TimeoutSeconds 120
    $json = Join-Path $script:DiagDir ((("M6 " + $Name) -replace '[^\w\-]+', '_') + ".json")
    if (Test-Path -LiteralPath $json) { return Get-Content -LiteralPath $json -Raw -Encoding UTF8 | ConvertFrom-Json }
    return $null
}
$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Rezultat = $(if ($Ok) { "OK" } else { "GREŠKA" }); Test = $Name; Detalji = $Detail })
}
function Selected($state) { (@($state.audioTracks | Where-Object { $_.StartsWith("* ") }) -join "") -replace '^\* ', '' }
$inv = [Globalization.CultureInfo]::InvariantCulture

# 1) Audio trake: B dvaput, pa ponovno otvaranje vraća zapamćenu traku.
$r = Run "Audio" @($audioTest) "--diag-keys `"B;B`""
$k = $r.keys
$ok = ($r.extras.audioTracks.Count -eq 3) -and ($k[0].state.mpv.aid -eq "2") -and ($k[1].state.mpv.aid -eq "3")
Check "Audio: tipka B mijenja traku" $ok ("trake: {0} | nakon B: {1} | nakon B: {2}" -f ($r.extras.audioTracks -join " / "), (Selected $k[0].state), (Selected $k[1].state))
$r = Run "Audio vracanje" @($audioTest) ""
Check "Audio: zapamćena traka po videu" ($r.extras.mpv.aid -eq "3") ("pri ponovnom otvaranju: {0} (aid {1})" -f (Selected $r.extras), $r.extras.mpv.aid)

# 2) Brzina: + četiri puta (2×), stvarno brže; Backspace = 1×; − do najmanje 0,5×.
$r = Run "Brzina" @($long) "--diag-keys `"OemPlus;OemPlus;OemPlus;OemPlus;F24;F24;Back;Subtract;Subtract;Subtract`""
$k = $r.keys
$speeds = ($k | ForEach-Object { [double]::Parse($_.state.mpv.speed, $inv).ToString("0.##", $inv) }) -join " "
$dt = ($k[5].ms - $k[4].ms) / 1000.0
$dp = [double]::Parse($k[5].state.mpv.'time-pos', $inv) - [double]::Parse($k[4].state.mpv.'time-pos', $inv)
$rate = [Math]::Round($dp / $dt, 2)
$ok = ($speeds -eq "1.25 1.5 1.75 2 2 2 1 0.75 0.5 0.5") -and ($rate -gt 1.8 -and $rate -lt 2.2)
Check "Brzina: + / − / Backspace (0,5–2×)" $ok "redom: $speeds; pri 2× video ide ${rate}× brže od sata"

# 3) Omjer slike: O kroz sve načine.
$r = Run "Omjer" @($video1080) "--diag-keys `"O;O;O;O;O;O`""
$k = $r.keys
$aspects = ($k | ForEach-Object { "$($_.state.aspect)=$($_.state.mpv.'video-aspect-override')/$($_.state.mpv.panscan)" }) -join " "
$ratios = $k | ForEach-Object { [double]::Parse($_.state.mpv.'video-aspect-override', $inv) }
$pans = $k | ForEach-Object { [double]::Parse($_.state.mpv.panscan, $inv) }
$ok = ([Math]::Abs($ratios[0] - 16 / 9) -lt 0.01) -and ([Math]::Abs($ratios[1] - 4 / 3) -lt 0.01) -and ([Math]::Abs($ratios[2] - 1.85) -lt 0.01) `
    -and ([Math]::Abs($ratios[3] - 2.35) -lt 0.01) -and ($ratios[4] -eq -1) -and ($pans[4] -eq 1) -and ($ratios[5] -eq -1) -and ($pans[5] -eq 0)
Check "Omjer slike: tipka O" $ok $aspects

# 4) Snimka zaslona: S (s titlom) i Shift+S (bez), pune rezolucije; razlikuju se samo u dijelu s titlom.
$r = Run "Snimka" @($video1080, $subHr) "--diag-probe 10 --diag-keys `"S;Shift+S`""
$a = $r.keys[0].state.lastScreenshot; $b = $r.keys[1].state.lastScreenshot
Add-Type -AssemblyName System.Drawing
function BottomDiff([string]$x, [string]$y) {
    $i1 = [Drawing.Bitmap]::FromFile($x); $i2 = [Drawing.Bitmap]::FromFile($y)
    $sum = 0; $n = 0; $top = 0; $tn = 0
    for ($px = 200; $px -lt $i1.Width - 200; $px += 16) {
        for ($py = [int]($i1.Height * 0.75); $py -lt $i1.Height - 10; $py += 8) {
            $p = $i1.GetPixel($px, $py); $q = $i2.GetPixel($px, $py); $sum += [Math]::Abs($p.R - $q.R) + [Math]::Abs($p.G - $q.G) + [Math]::Abs($p.B - $q.B); $n++
        }
        for ($py = 20; $py -lt [int]($i1.Height * 0.4); $py += 8) {
            $p = $i1.GetPixel($px, $py); $q = $i2.GetPixel($px, $py); $top += [Math]::Abs($p.R - $q.R) + [Math]::Abs($p.G - $q.G) + [Math]::Abs($p.B - $q.B); $tn++
        }
    }
    $size = "$($i1.Width)x$($i1.Height)"
    $i1.Dispose(); $i2.Dispose()
    return @([Math]::Round($sum / $n, 1), [Math]::Round($top / $tn, 1), $size)
}
if ($a -and $b -and (Test-Path $a) -and (Test-Path $b)) {
    $d = BottomDiff $a $b
    $ok = ($d[2] -eq "1920x1080") -and ($d[0] -gt 5) -and ($d[1] -lt 3) -and ((Split-Path $a) -eq $shots)
    Check "Snimka zaslona: S i Shift+S" $ok ("{0} ({1:N0} KB) i {2} ({3:N0} KB), {4}; razlika dolje (titl) {5}, gore {6}" -f (Split-Path $a -Leaf), ((Get-Item $a).Length / 1KB), (Split-Path $b -Leaf), ((Get-Item $b).Length / 1KB), $d[2], $d[0], $d[1])
}
else { Check "Snimka zaslona: S i Shift+S" $false "snimke nisu spremljene: '$a' '$b'" }

# 5) Omiljeni: film (Ctrl+D), serija iz epizode (cijela serija), početni ekran, uklanjanje iz druge epizode iste serije.
$r = Run "Omiljeni film" @($film) "--diag-keys `"Ctrl+D`""
$f1 = $r.keys[0].state
$r = Run "Omiljena serija" @($e2) "--diag-keys `"Ctrl+D`""
$f2 = $r.keys[0].state
$r = Run "Omiljeni pocetni" @() ""
$homeFav = $r.library.favorites
$r = Run "Omiljena serija ukloni" @($e3) "--diag-keys `"Ctrl+D`""
$f3 = $r.keys[0].state
$ok = $f1.isFavorite -and ($f1.favorites.Count -eq 1) -and $f2.isFavorite -and ($f2.favorites[0] -like "serija: Serija Test | *\Serija.Test") `
    -and ($homeFav.Count -eq 2) -and (-not $f3.isFavorite) -and ($f3.favorites.Count -eq 1) -and ($f3.favorites[0] -like "film:*")
Check "Omiljeni: film, serija, početni ekran, uklanjanje" $ok ("film: {0} | serija: {1} | početni ekran: {2} | nakon Ctrl+D na S01E03: {3}" -f $f1.favorites[0], $f2.favorites[0], ($homeFav -join " ; "), ($f3.favorites -join " ; "))

# 6) Sljedeća epizoda bez automatskog pokretanja: kartica ostaje, epizoda se ne mijenja.
$r = Run "Sljedeca rucno" @($e2) "--diag-upnext 2 --diag-autonext off"
$ok = $r.upNext.sawCard -and ($r.upNext.to -like "*S01E02*") -and $r.upNext.upNextStillShown -and ($r.upNext.upNextText -eq "Sljedeća epizoda")
Check "Sljedeća epizoda: samo ponuda kad je isključeno" $ok ("kartica '{0}', tekst '{1}', ostaje na {2}" -f $r.upNext.cardTitle, $r.upNext.upNextText, $r.upNext.to)
$r = Run "Sljedeca auto" @($e2) "--diag-upnext 2"
Check "Sljedeća epizoda: automatski (zadano)" ($r.upNext.to -like "*S01E03*") ("iz {0} u {1}" -f $r.upNext.from, $r.upNext.to)

$results
"{0} / {1} prošlo" -f @($results | Where-Object Rezultat -eq "OK").Count, $results.Count
