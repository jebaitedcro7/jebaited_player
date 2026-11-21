<#
.SYNOPSIS
  Testovi 2.1.7 u pravom playeru (nad testnom mapom podataka, nikad nad tvojom):
  - scenarij ui217: kotačić glasnoće, položaj uz taskbar, Minimize, LIVE, pet izgleda mini playera, naslov i podaci o pjesmi,
    Discord u izborniku izvora, Home bez prekida glazbe, izgled vremena i prognoze;
  - pokretanje bez dvostrukog prozora: prati se svaki prozor procesa od prvog trenutka (mini ili puni, nikad oba).

.EXAMPLE
  .\tools\run-ui217-tests.ps1
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$ffmpeg = if ($env:FFMPEG) { $env:FFMPEG } else { "ffmpeg" }

# Testni album: tri pjesme s oznakama (isti kao u ciklusu 2).
$album = Join-Path $root "testdata\ciklus2\album"
New-Item -ItemType Directory -Force -Path $album | Out-Null
$tracks = @(
    @{ File = "01 Prva.mp3"; Hz = 330; Title = "Prva pjesma"; Codec = @("-c:a", "libmp3lame", "-b:a", "192k") },
    @{ File = "02 Druga.flac"; Hz = 440; Title = "Druga pjesma"; Codec = @("-c:a", "flac") },
    @{ File = "03 Treca.m4a"; Hz = 550; Title = "Treca pjesma"; Codec = @("-c:a", "aac", "-b:a", "128k") })
foreach ($t in $tracks) {
    $out = Join-Path $album $t.File
    if (-not (Test-Path $out)) {
        $ffArgs = @("-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=$($t.Hz):sample_rate=44100", "-t", "40", "-ac", "2") + $t.Codec +
                @("-metadata", "artist=jebaited_cro", "-metadata", "title=$($t.Title)", "-metadata", "album=Test album", $out)
        & $ffmpeg @ffArgs
        if ($LASTEXITCODE -ne 0) { throw "FFmpeg nije uspio ($($t.File))" }
    }
}
$clip = Join-Path $root "testdata\ciklus1\Kratki.Film.2026.mkv"

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Test = $Name; Rezultat = $(if ($Ok) { "OK" } else { "GREŠKA" }); Detalji = $Detail })
}

# --- Scenarij ui217 -----------------------------------------------------------------------------------------------
$data = Join-Path $root "testdata\ui217\data"
if (Test-Path $data) { Remove-Item $data -Recurse -Force }
New-Item -ItemType Directory -Force -Path $data | Out-Null
'{ "Language": "hr", "AutoUpdate": false, "OnlineWizardOffered": true }' | Set-Content -LiteralPath (Join-Path $data "settings.json") -Encoding UTF8
$db = Join-Path $script:DiagDir "ui217.db"
Remove-Item "$db*" -ErrorAction SilentlyContinue
$env:JEBAITED_DATA_DIR = $data
$env:JEBAITED_UI217_CLIP = $clip
try {
    $summary = Invoke-Diag -Name "ui217" -File (Join-Path $album "01 Prva.mp3") -Seconds 1 -ExtraArgs "--diag-ui-test ui217 --diag-db `"$db`"" -TimeoutSeconds 300
} finally {
    Remove-Item Env:JEBAITED_DATA_DIR, Env:JEBAITED_UI217_CLIP -ErrorAction SilentlyContinue
}
$u = (Get-Content -LiteralPath (Join-Path $script:DiagDir "ui217.json") -Raw -Encoding UTF8 | ConvertFrom-Json).uiTest
if (-not $u -or $u.exception -or $u.error) { throw "ui217: $($u.exception)$($u.error) ($($summary.Error))" }

# 9) Home
$h = $u.home
Check "9: Home ne prekida glazbu" ($h.homeView -and $h.welcomeVisible -and $h.hasMedia -and -not $h.paused -and [double]$h.advanced -gt 1.2 -and $h.idle -ne "yes") "početni ekran $($h.welcomeVisible), glazba $($h.hasMedia), napredak $($h.advanced) s, mpv idle=$($h.idle)"
Check "9: Home ne učitava ponovno (isti audio engine)" ([int]$h.loads -eq 0) "novih učitavanja: $($h.loads)"
Check "9: Home prikazuje pjesmu, izvor i traku kontrola" ($h.bar -and $h.track -match "jebaited_cro — Prva pjesma" -and $h.source -match "LOKALNO" -and -not $h.overlayVisible -and -not $h.audioPanel) "'$($h.track)', izvor '$($h.source)', kontrole playera $($h.overlayVisible), snimka $($h.screenshot)"
$c = $u.homeControls
Check "9: Home Play/Pause, glasnoća" ($c.pausedByHome -and $c.resumed -and [double]$c.volumeStep -eq 5 -and $c.volumeBack) "pauza $($c.pausedByHome), nastavak $($c.resumed), korak glasnoće $($c.volumeStep)"
$n = $u.homeNext
Check "9: Home Sljedeća / Prethodna mijenjaju pjesmu uz isti prikaz" ($n.current -match "02" -and $n.homeView -and $n.welcomeVisible -and -not $n.paused -and $n.track -match "Druga pjesma") "'$($n.current)', naslov '$($n.track)'"
$sw = $u.switches
Check "9: Home → mini → puni player bez prekida" ([double]$sw.toMiniAdvanced -gt 1 -and [double]$sw.backAdvanced -gt 1 -and [int]$sw.extraLoads -eq 0 -and -not $sw.paused) "mini +$($sw.toMiniAdvanced) s, natrag +$($sw.backAdvanced) s, novih učitavanja $($sw.extraLoads)"
$al = $u.afterLoad
Check "9: nova pjesma vraća prikaz playera" (-not $al.homeView -and -not $al.welcomeVisible -and $al.audioPanel) "home $($al.homeView), početni $($al.welcomeVisible)"
if ($u.videoHome) {
    Check "9: video se i dalje zatvara tipkom Home" ($u.videoHome.wasVideo -and -not $u.videoHome.hasMediaAfter -and $u.videoHome.welcome) "video $($u.videoHome.wasVideo), nakon Home medij $($u.videoHome.hasMediaAfter)"
}

# 1) Kotačić
$w = $u.wheel
Check "1: kotačić iznad trake glasnoće (mini): korak i smjer" ([double]$w.step -eq 5 -and [double]$w.miniDown -eq 45) "gore +$($w.step), dvaput dolje → $($w.miniDown)"
Check "1: kotačić drugdje ne mijenja glasnoću (mini)" ([double]$w.elsewhere -eq 45 -and [double]$w.realElsewhere -eq [double]$w.realUp) "iznad naslova/vremena: $($w.elsewhere), pravi miš: $($w.realUp) → $($w.realElsewhere)"
Check "1: pravi kotačić miša iznad trake glasnoće" ([double]$w.realUp -eq 45) "40 → $($w.realUp)"
Check "1: glasnoća ostaje 0–100" ([double]$w.clampedTop -eq 100 -and [double]$w.clampedBottom -eq 0) "gore $($w.clampedTop), dolje $($w.clampedBottom)"
Check "1: puni player: samo traka glasnoće" ([double]$w.overlayStep -eq 5 -and [double]$w.overlayTimeline -eq 55) "traka glasnoće +$($w.overlayStep), vremenska traka ostavlja $($w.overlayTimeline)"

# 6) Izgledi
$d = $u.designs
$expect = @{ classic = "Winamp"; card = "MiniCardSkin"; strip = "Winamp"; side = "MiniSideSkin"; ufo = "MiniUfoSkin" }
foreach ($id in "classic", "card", "strip", "side", "ufo") {
    $e = $d.$id
    $skinOk = if ($id -in "card", "side", "ufo") { $e.skin -eq $expect[$id] } else { -not $e.skin }
    $titleOk = $e.title -match "jebaited_cro — (Prva|Druga) pjesma" -and $e.title -notmatch "kHz|kbps"
    Check "6: izgled $id" ($e.active -eq $id -and $e.saved -eq $id -and $skinOk -and $titleOk -and $e.inside) "$($e.skin) $($e.size -join '×'), '$($e.title)', snimka $($e.screenshot)"
    if ($id -ne "strip") {
        Check "6: izgled ${id}: Play/Pause i glasnoća rade isto" ($e.pausedByButton -and $e.resumedByButton -and $e.volumeSliderFollows -and [double]$e.wheelStep -eq 5) "pauza $($e.pausedByButton), nastavak $($e.resumedByButton), traka prati $($e.volumeSliderFollows), kotačić +$($e.wheelStep)"
    } else {
        Check "6: izgled strip: glasnoća radi isto" ($e.volumeSliderFollows -and [double]$e.wheelStep -eq 5) "traka prati $($e.volumeSliderFollows), kotačić +$($e.wheelStep)"
    }
    if ($id -in "card", "side", "ufo") {
        Check "6: izgled ${id}: sve funkcije (LIVE, EQ, Discord)" ($e.hasLive -and $e.hasEq -and $e.hasDiscord -and $e.source -match "LOKALNO" -and $e.time -match "\d") "vrijeme '$($e.time)', izvor '$($e.source)'"
    }
}
Check "6: odabir se sprema (JSON → učitavanje)" ($u.designPersist.design -eq "side" -and $u.designPersist.playlistShown) "izgled '$($u.designPersist.design)'"

# 7) Naslov
$t = $u.title
Check "7: zadano samo izvođač i pjesma" ($t.default -match "^jebaited_cro — .+pjesma$" -and $t.default -notmatch "kbps|kHz|Test album") "'$($t.default)'"
Check "7: odabrani podaci (kvaliteta, album)" ($t.withExtras -match "kbps" -and $t.withExtras -match "Test album") "'$($t.withExtras)'"
Check "7: dugi naslov se vrti u krug, kratki stoji" ($t.longScrolls -and [double]$t.cycleSeconds -gt 3 -and [double]$t.textWidth -gt [double]$t.boxWidth -and -not $t.shortScrolls) "krug $($t.cycleSeconds) s, tekst $($t.textWidth) px u $($t.boxWidth) px"
Check "7: postavka podataka je spremljena" (($t.infoFields -join ",") -eq "artist,title") "$($t.infoFields -join ',')"

# 8) Discord
Check "8: Discord Bot u izborniku izvora" (@($u.sourceMenu).Count -eq 4 -and ($u.sourceMenu -join "|") -match "Radio.*Spotify.*YouTube.*Discord|Radio.*Spotify.*YouTube.*Bot za Discord") "$($u.sourceMenu -join ' | ')"

# 2) Položaj
$p = $u.placement
Check "2: mini player s Equalizerom ostaje iznad taskbara" ($p.afterEqInside -and [double]$p.miniBottomWithPanels -le [double]$p.workBottom) "dno prozora $($p.miniBottomWithPanels), dno radnog područja $($p.workBottom)"
Check "2: prozor izvučen izvan zaslona se vraća" ($p.afterFarInside -and $p.stripInside) "klasični $($p.afterFarInside), uska traka $($p.stripInside)"
Check "2: dodatni prozor (Discord) otvoren uz rub je cijeli vidljiv" ($p.discordInside -and $p.clampedBottomEdge) "Discord $($p.discordInside)"

# 3) Minimize
$m = $u.minimize
Check "3: Minimize mini playera, glazba svira" ($m.miniMinimized -and -not $m.miniPaused -and [double]$m.miniAdvanced -gt 1) "minimiziran $($m.miniMinimized), napredak $($m.miniAdvanced) s"
Check "3: Minimize punog playera, glazba svira, povratak" ($m.fullMinimized -and -not $m.fullPaused -and [double]$m.fullAdvanced -gt 1 -and $m.mainVisibleAfter) "minimiziran $($m.fullMinimized), napredak $($m.fullAdvanced) s"

# 5) LIVE
$l = $u.live
Check "5: LIVE crven i pulsira samo dok radio svira (mini 1)" ($l.notLiveBefore -and $l.classicVisible -and $l.classicPulsing) "vidljiv $($l.classicVisible), pulsira $($l.classicPulsing), snimka $($l.screenshotClassic)"
Check "5: LIVE u svim izgledima mini playera" ($l.stripVisible -and $l.cardVisible -and $l.ufoVisible -and $l.sideVisible) "traka $($l.stripVisible), kartica $($l.cardVisible), NLO $($l.ufoVisible), bočna $($l.sideVisible)"
Check "5: LIVE u punom playeru i na Home ekranu" ($l.overlayVisible -and $l.audioPanelVisible -and $l.homeVisible -and $l.homeStillPlaying -and $l.homeSource -match "RADIO") "kontrole $($l.overlayVisible), ploča $($l.audioPanelVisible), Home $($l.homeVisible), izvor '$($l.homeSource)'"
Check "5: LIVE nestaje na pauzu i Stop, vraća se uz nastavak" (-not $l.afterPause -and $l.afterResume -and -not $l.afterStop) "pauza $($l.afterPause), nastavak $($l.afterResume), stop $($l.afterStop)"

# 10) Home izgled
$hl = $u.homeLook
Check "10: font, veličina i mjesto svakog elementa" ($hl.timeFont -match "Georgia" -and [double]$hl.timeSize -eq 48 -and $hl.timePos -eq "tr" -and $hl.dateFont -match "Consolas" -and [double]$hl.dateSize -eq 20 -and $hl.datePos -eq "bl" -and $hl.weatherPos -eq "br") "vrijeme $($hl.timeFont) $($hl.timeSize) $($hl.timePos), datum $($hl.dateFont) $($hl.dateSize) $($hl.datePos), snimka $($hl.screenshot)"
$hr = $u.homeLookReset
Check "10: vraćanje na zadano" ([double]$hr.timeSize -eq 30 -and [double]$hr.dateSize -eq 13 -and $hr.timePos -eq "tl" -and $hr.datePos -eq "tl" -and $hr.weatherPos -eq "tl") "vrijeme $($hr.timeSize) $($hr.timePos), datum $($hr.dateSize) $($hr.datePos)"
$sl = $u.settingsLook
Check "10: pregled u postavkama" ($sl.open -and $sl.hasPreview -and $sl.previewTime -match "\d") "pregled '$($sl.previewTime)', snimka $($sl.screenshot)"

# --- 4) Pokretanje: jedan prozor ---------------------------------------------------------------------------------------
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WinProbe {
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    public class Win { public IntPtr H; public string Title; public int L, T, R, B; public bool Visible; }
    public static List<Win> Of(uint pid) {
        var list = new List<Win>();
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
            RECT r; GetWindowRect(h, out r);
            list.Add(new Win { H = h, Title = sb.ToString(), L = r.L, T = r.T, R = r.R, B = r.B, Visible = IsWindowVisible(h) });
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
"@

function Test-Startup([string]$Name, [string]$SettingsJson, [string[]]$Files, [int]$Seconds = 7) {
    $dir = Join-Path $root "testdata\ui217\startup-$Name"
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $SettingsJson | Set-Content -LiteralPath (Join-Path $dir "settings.json") -Encoding UTF8
    $env:JEBAITED_DATA_DIR = $dir
    $argList = "--new-window"
    foreach ($f in $Files) { $argList += " `"$f`"" }
    $app = Start-Process -FilePath $script:Exe -ArgumentList $argList -PassThru
    Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $mainOnScreen = $false; $miniOnScreen = $false; $maxBoth = 0
    while ($sw.Elapsed.TotalSeconds -lt $Seconds -and -not $app.HasExited) {
        $wins = @([WinProbe]::Of([uint32]$app.Id) | Where-Object { $_.Visible -and $_.R -gt $_.L -and $_.L -gt -20000 -and $_.B -gt 0 })
        $main = @($wins | Where-Object { $_.Title -eq "jebaited_player" -or $_.Title -like "*– jebaited_player" }).Count -gt 0
        $mini = @($wins | Where-Object { $_.Title -eq "jebaited_player mini" }).Count -gt 0
        if ($main) { $mainOnScreen = $true }
        if ($mini) { $miniOnScreen = $true }
        if ($main -and $mini) { $maxBoth++ }
        Start-Sleep -Milliseconds 25
    }
    # Uredno zatvaranje: mini prozor (×) ili glavni; postavke se spremaju pri zatvaranju.
    $all = @([WinProbe]::Of([uint32]$app.Id) | Where-Object { $_.Visible -and $_.L -gt -20000 })
    $target = $all | Where-Object { $_.Title -eq "jebaited_player mini" } | Select-Object -First 1
    if (-not $target) { $target = $all | Where-Object { $_.Title -eq "jebaited_player" } | Select-Object -First 1 }
    if ($target) { [void][WinProbe]::PostMessage($target.H, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }
    if (-not $app.WaitForExit(15000)) { $app.Kill(); $closed = $false } else { $closed = $true }
    $saved = Get-Content -LiteralPath (Join-Path $dir "settings.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    [pscustomobject]@{ Main = $mainOnScreen; Mini = $miniOnScreen; Both = $maxBoth; Closed = $closed; WasOpen = [bool]$saved.MiniPlayer.WasOpen; Dir = $dir }
}

$base = '"Language": "hr", "AutoUpdate": false, "OnlineWizardOffered": true'
$s1 = Test-Startup "mini" "{ $base, `"MiniPlayer`": { `"WasOpen`": true } }" @()
Check "4: zadnji prikaz mini → pokreće se samo mini player" ($s1.Mini -and -not $s1.Main -and $s1.Both -eq 0) "mini $($s1.Mini), veliki prozor ikad vidljiv $($s1.Main), oba istodobno $($s1.Both)"
Check "4: zadnji prikaz se sprema pri zatvaranju" ($s1.Closed -and $s1.WasOpen) "WasOpen u postavkama: $($s1.WasOpen)"
# Ponovno pokretanje s postavkama koje je spremila prethodna sesija.
$dir1 = $s1.Dir
$env:JEBAITED_DATA_DIR = $dir1
$app = Start-Process -FilePath $script:Exe -ArgumentList "--new-window" -PassThru
Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue
Start-Sleep -Seconds 5
$wins = @([WinProbe]::Of([uint32]$app.Id) | Where-Object { $_.Visible -and $_.L -gt -20000 })
$mini2 = @($wins | Where-Object { $_.Title -eq "jebaited_player mini" }).Count -gt 0
$main2 = @($wins | Where-Object { $_.Title -eq "jebaited_player" }).Count -gt 0
$t2 = $wins | Where-Object { $_.Title -eq "jebaited_player mini" } | Select-Object -First 1
if ($t2) { [void][WinProbe]::PostMessage($t2.H, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }
if (-not $app.WaitForExit(15000)) { $app.Kill() }
Check "4: ponovno pokretanje otvara isti prikaz" ($mini2 -and -not $main2) "mini $mini2, veliki $main2"

# 2.1.8: prije je maksimiziran glavni prozor + pokretanje u mini playeru rušilo Show() (ShowActivated=false uz Maximized) i
# player je radio bez ijednog prozora.
$s1m = Test-Startup "mini-maximized" "{ $base, `"WindowMaximized`": true, `"MiniPlayer`": { `"WasOpen`": true } }" @()
Check "4: mini pri pokretanju radi i kad je glavni prozor bio maksimiziran" ($s1m.Mini -and -not $s1m.Main -and $s1m.Closed) "mini $($s1m.Mini), veliki $($s1m.Main), uredno zatvoren $($s1m.Closed)"
$savedMax = (Get-Content -LiteralPath (Join-Path $s1m.Dir "settings.json") -Raw -Encoding UTF8 | ConvertFrom-Json).WindowMaximized
Check "4: maksimizirano stanje glavnog prozora se ne gubi" ([bool]$savedMax) "WindowMaximized u postavkama: $savedMax"

$s2 = Test-Startup "normal" "{ $base, `"MiniPlayer`": { `"WasOpen`": false } }" @()
Check "4: zadnji prikaz normalni → pokreće se samo veliki prozor" ($s2.Main -and -not $s2.Mini -and $s2.Both -eq 0) "veliki $($s2.Main), mini $($s2.Mini)"
$s3 = Test-Startup "music" "{ $base }" @((Join-Path $album "01 Prva.mp3"))
Check "4: glazba iz naredbenog retka → samo mini player" ($s3.Mini -and -not $s3.Main -and $s3.Both -eq 0) "mini $($s3.Mini), veliki $($s3.Main), oba $($s3.Both)"
if (Test-Path $clip) {
    $s4 = Test-Startup "video" "{ $base }" @($clip)
    Check "4: film iz naredbenog retka → samo veliki prozor" ($s4.Main -and -not $s4.Mini -and $s4.Both -eq 0) "veliki $($s4.Main), mini $($s4.Mini)"
}

$results | Format-Table -AutoSize -Wrap
$failed = @($results | Where-Object { $_.Rezultat -ne "OK" }).Count
Write-Host ("Ukupno {0}, uspješno {1}, grešaka {2}" -f $results.Count, ($results.Count - $failed), $failed)
if ($failed -gt 0) { exit 1 }
