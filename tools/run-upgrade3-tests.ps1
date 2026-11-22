<#
.SYNOPSIS
  Testovi UPGRADE 3 u pravom playeru, stvarnim mišem (kursor se sam pomiče i klikće, ne diraj miš dok traju):
  - vremenska traka: klik bilo gdje (video se reproducira i pauziran), povlačenje, oblačić s vremenom, bez neželjene pauze;
    različiti formati i rezolucije, prozor i cijeli zaslon, film od 120 minuta;
  - tipka Home: gore lijevo, skriva se i pojavljuje zajedno s kontrolama, povratak na početni ekran, pozicija spremljena,
    nastavak gledanja (i u cijelom zaslonu);
  - postavke: sve kartice, promjene zvuka i slike odmah u mpv-u, korak premotavanja, skrivanje kontrola, načini nastavka.
  Zasebna testna baza po pokretanju; tvoje postavke i povijest se ne mijenjaju.

.EXAMPLE
  .\tools\run-upgrade3-tests.ps1
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$ffmpeg = if ($env:FFMPEG) { $env:FFMPEG } else { "ffmpeg" }
$dir = Join-Path $root "testdata\upgrade3"
New-Item -ItemType Directory -Force -Path $dir | Out-Null

# Film od 120 minuta (primjer iz zahtjeva: klik na 50 % = 60. minuta).
$film = Join-Path $dir "Film.120min.2026.1080p.WEB.mkv"
if (-not (Test-Path $film)) {
    & $ffmpeg -hide_banner -loglevel error -y -f lavfi -i "testsrc2=size=854x480:rate=24" -f lavfi -i "sine=frequency=440:sample_rate=48000" `
        -t 7200 -c:v libx264 -preset ultrafast -crf 38 -g 240 -c:a aac -b:a 32k $film
    if ($LASTEXITCODE -ne 0) { throw "FFmpeg nije uspio (film 120 min)" }
}

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Test = $Name; Rezultat = $(if ($Ok) { "OK" } else { "GREŠKA" }); Detalji = $Detail })
}

function Invoke-UiTest([string]$Name, [string]$Scenario, [string]$File, [switch]$Fullscreen) {
    $db = Join-Path $script:DiagDir "upgrade3-$Name.db"
    Remove-Item "$db*" -ErrorAction SilentlyContinue
    $extra = "--diag-ui-test $Scenario --diag-db `"$db`""
    $summary = Invoke-Diag -Name "upgrade3_$Name" -File $File -Seconds 1 -Fullscreen:$Fullscreen -ExtraArgs $extra -TimeoutSeconds 300
    $json = Join-Path $script:DiagDir "upgrade3_$Name.json"
    $r = if (Test-Path -LiteralPath $json) { Get-Content -LiteralPath $json -Raw -Encoding UTF8 | ConvertFrom-Json } else { $null }
    if (-not $r -or -not $r.uiTest) { throw "$Name`: nema rezultata ($($summary.Error))" }
    if ($r.uiTest.exception) { throw "$Name`: $($r.uiTest.exception)" }
    return $r.uiTest
}

function Test-Timeline([string]$Name, [string]$File, [switch]$Fullscreen) {
    $u = Invoke-UiTest $Name "timeline" $File -Fullscreen:$Fullscreen
    $d = [double]$u.duration
    foreach ($c in $u.playingClicks) {
        # Video se reproducira: pozicija = kliknuto mjesto + vrijeme od klika (do 1 s), pauza se ne mijenja.
        $ok = $null -ne $c.errorVsHover -and $c.errorVsHover -ge -0.3 -and $c.errorVsHover -le ([double]$c.secondsSinceClick + 0.6) -and -not $c.pausedAfter
        Check "$Name klik $([int]($c.fraction*100)) % (reprodukcija)" $ok "oblačić $($c.hoverText), mpv $($c.timePos) s, razlika $($c.errorVsHover) s, pauza $($c.pausedBefore)→$($c.pausedAfter)"
    }
    foreach ($c in $u.pausedClicks) {
        # Pauzirano: točno na vrijeme iz oblačića (najviše 2 kadra), video ostaje pauziran.
        $ok = $null -ne $c.errorVsHover -and [math]::Abs([double]$c.errorVsHover) -le 0.1 -and $c.pausedAfter
        Check "$Name klik $([int]($c.fraction*100)) % (pauza)" $ok "oblačić $($c.hoverText), mpv $($c.timePos) s, razlika $($c.errorVsHover) s, pauza $($c.pausedBefore)→$($c.pausedAfter)"
    }
    $g = $u.drag
    $monotonic = $true
    for ($i = 1; $i -lt $g.positionsDuringDrag.Count; $i++) { if ($g.positionsDuringDrag[$i] -lt $g.positionsDuringDrag[$i - 1]) { $monotonic = $false } }
    $bubbles = @($g.bubblesDuringDrag | Where-Object { $_ }).Count
    $ok = $monotonic -and [math]::Abs([double]$g.error) -le [math]::Max(0.1, $d / 1000) -and $g.pausedAfter -and -not $g.scrubbingAfter -and $bubbles -eq $g.bubblesDuringDrag.Count
    Check "$Name povlačenje 20 % → 60 %" $ok "pozicije $($g.positionsDuringDrag[0]) … $($g.positionsDuringDrag[-1]), kraj $($g.timePosAfter) s (cilj $($g.expected)), oblačića $bubbles/$($g.bubblesDuringDrag.Count)"
}

function Test-Home([string]$Name, [string]$Scenario, [string]$File, [bool]$Fullscreen) {
    $u = Invoke-UiTest $Name $Scenario $File
    $off = $u.homeOffsetFromVideoTopLeft
    Check "$Name Home gore lijevo" ($off[0] -ge 0 -and $off[0] -le 40 -and $off[1] -ge 0 -and $off[1] -le 30) "pomak od kuta videa: $($off -join ', ') px"
    Check "$Name vidljivo s kontrolama" ($u.visibleAfterMove.bottom -and $u.visibleAfterMove.home) "dolje $($u.visibleAfterMove.bottom), Home $($u.visibleAfterMove.home)"
    Check "$Name skriva se s kontrolama" (-not $u.visibleAfterIdle.bottom -and -not $u.visibleAfterIdle.home) "dolje $($u.visibleAfterIdle.bottom), Home $($u.visibleAfterIdle.home)"
    Check "$Name vraća se pomakom miša" ($u.visibleAfterSecondMove.bottom -and $u.visibleAfterSecondMove.home) "dolje $($u.visibleAfterSecondMove.bottom), Home $($u.visibleAfterSecondMove.home)"
    $a = $u.afterHome
    Check "$Name klik: početni ekran, aplikacija otvorena" (-not $a.hasMedia -and $a.welcomeVisible -and $a.windowVisible -and -not $a.fullscreen -and -not $a.homeVisible) "početni $($a.welcomeVisible), prozor $($a.windowVisible), cijeli zaslon $($a.fullscreen)"
    Check "$Name pozicija spremljena" ([math]::Abs([double]$a.savedPosition - [double]$a.positionBeforeClick) -le 1.5) "prije klika $($a.positionBeforeClick) s, spremljeno $($a.savedPosition) s; $($a.recent[0])"
    Check "$Name nastavak gledanja" ($null -ne $u.resumeOffer -and [math]::Abs([double]$u.resumeOffer - [double]$a.savedPosition) -le 0.5 -and [math]::Abs([double]$u.timePosAfterResume - [double]$u.resumeOffer) -le 3 -and -not $u.pausedAfterResume) "ponuđeno $($u.resumeOffer) s, nastavljeno na $($u.timePosAfterResume) s"
}

$m1 = Join-Path $root "testdata\m1"
Test-Timeline "120min" $film
Test-Timeline "120min-fullscreen" $film -Fullscreen
Test-Timeline "1080p-h264" (Join-Path $m1 "1080p_h264_aac.mp4")
Test-Timeline "4k-hevc-hdr" (Join-Path $m1 "4k_hevc10_hdr10_eac3.mkv")
Test-Timeline "4k-av1" (Join-Path $m1 "4k_av1_10bit_opus.mkv")
Test-Timeline "8k-hevc" (Join-Path $m1 "8k_hevc_aac.mkv")
Test-Timeline "avi-mpeg4" (Join-Path $m1 "fmt_avi_mpeg4_mp3.avi")
Test-Timeline "webm-vp9" (Join-Path $m1 "fmt_webm_vp9_opus.webm")
Test-Timeline "audio-mp3" (Join-Path $m1 "audio_only.mp3")
Test-Home "home" "home" $film $false
Test-Home "home-fullscreen" "home-fullscreen" $film $true

$s = Invoke-UiTest "settings" "settings" $film
$tabsOk = @($s.tabs.PSObject.Properties | Where-Object { $_.Value.selected -eq $_.Name -and $_.Value.contentHeight -gt 100 }).Count
# 2.0: deveta kartica Prečaci. 2.2.14: deseta kartica Torlink (samo GitHub inačica, ova skripta ide nad njom).
Check "postavke: 11 kartica (10 + Torlink)" ($tabsOk -eq 11) "otvara se $tabsOk/11 kartica: $(($s.tabs.PSObject.Properties | ForEach-Object { $_.Value.header }) -join ', ')"
Check "postavke: audio uređaji" (@($s.audioDevices).Count -ge 1) "$(@($s.audioDevices).Count) uređaja"
$c = $s.changed
Check "postavke: promjene odmah u mpv-u" ($c.hwdec -eq "no" -and $c.scale -eq "ewa_lanczossharp" -and $c.deband -eq "yes" -and $c.af -match "dynaudnorm" -and [double]$c.'audio-delay' -eq 0.25 -and $c.alang -match "^en" -and $c.'target-colorspace-hint' -eq "yes") "hwdec=$($c.hwdec), scale=$($c.scale), deband=$($c.deband), af=$($c.af), audio-delay=$($c.'audio-delay'), alang=$($c.alang), HDR=$($c.'target-colorspace-hint')"
$f = $s.fast; $b = $s.balanced
Check "postavke: brzo i natrag na zadano" ($f.scale -eq "bilinear" -and $b.scale -ne "bilinear" -and $b.hwdec -eq "auto-safe" -and $b.af -eq "" -and [double]$b.'audio-delay' -eq 0) "brzo scale=$($f.scale); zadano scale=$($b.scale), hwdec=$($b.hwdec)"
Check "postavke: omjer slike" ($s.aspect -match "^1\.33") "video-aspect-override=$($s.aspect)"
Check "postavke: korak premotavanja 10 s / Ctrl 60 s" ([double]$s.seekStep.afterRight -eq 610 -and [double]$s.seekStep.afterCtrlLeft -eq 550) "600 → $($s.seekStep.afterRight) → $($s.seekStep.afterCtrlLeft)"
Check "postavke: skrivanje kontrola nakon 6 s" ($s.hideDelay.visibleAfter4s -and $s.hideDelay.hiddenAfter7s) "vidljive nakon 4 s: $($s.hideDelay.visibleAfter4s), skrivene nakon 7 s: $($s.hideDelay.hiddenAfter7s)"
$rr = $s.resume
Check "postavke: nastavi automatski" ($null -eq $rr.resume.offer -and [math]::Abs([double]$rr.resume.timePos - [double]$rr.resume.saved) -le 4 -and -not $rr.resume.paused) "spremljeno $($rr.resume.saved), sada $($rr.resume.timePos), pauza $($rr.resume.paused)"
Check "postavke: uvijek od početka" ($null -eq $rr.start.offer -and [double]$rr.start.timePos -le 4) "sada $($rr.start.timePos) s"
Check "postavke: pitaj" ($null -ne $rr.ask.offer -and $rr.ask.paused) "ponuđeno $($rr.ask.offer) s, pauzirano $($rr.ask.paused)"

$results | Format-Table -AutoSize -Wrap | Out-String -Width 220
$failed = @($results | Where-Object Rezultat -ne "OK").Count
"UPGRADE 3: $($results.Count - $failed)/$($results.Count) OK"
if ($failed -gt 0) { exit 1 }
