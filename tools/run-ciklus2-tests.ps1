<#
.SYNOPSIS
  Testovi ciklusa 2 (M1a, M1b) u pravom playeru, nad testnom mapom podataka (JEBAITED_DATA_DIR), nikad nad tvojom:
  - dnevna kopija pri pokretanju (settings.json + library.db), stanje u Postavke → Općenito;
  - izvoz bez lozinki, ključeva i položaja prozora;
  - uvoz koji se odmah primijeni (tema, glasnoća, korak, equalizer u mpv-u), kopija prije uvoza, prozor se ponovno otvori;
  - kriva datoteka se odbija i ništa ne mijenja.

.EXAMPLE
  .\tools\run-ciklus2-tests.ps1
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$clip = Join-Path $root "testdata\ciklus1\Kratki.Film.2026.mkv"
if (-not (Test-Path $clip)) { throw "Nema testnog klipa: pokreni prvo tools\run-ciklus1-tests.ps1 (stvara ga)." }

# Testna mapa podataka: postavke kao kod korisnika 1.3.0 i mala baza povijesti.
$data = Join-Path $root "testdata\ciklus2\data"
if (Test-Path $data) { Remove-Item $data -Recurse -Force }
New-Item -ItemType Directory -Force -Path $data | Out-Null
'{ "Volume": 50, "Theme": "dark", "Language": "hr", "NormalizeAudio": true, "SeekStepSeconds": 5, "WindowLeft": 10, "Online": { "OpenSubtitlesApiKey": "test-kljuc" } }' |
    Set-Content -LiteralPath (Join-Path $data "settings.json") -Encoding UTF8
$seedDb = Join-Path $script:DiagDir "ciklus1-watched.db"
if (Test-Path $seedDb) { Copy-Item $seedDb (Join-Path $data "library.db") }

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Test = $Name; Rezultat = $(if ($Ok) { "OK" } else { "GREŠKA" }); Detalji = $Detail })
}

$env:JEBAITED_DATA_DIR = $data
try {
    $summary = Invoke-Diag -Name "ciklus2_backup" -File $clip -Seconds 1 -ExtraArgs "--diag-ui-test backup" -TimeoutSeconds 180
} finally {
    Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue
}
$json = Join-Path $script:DiagDir "ciklus2_backup.json"
$u = (Get-Content -LiteralPath $json -Raw -Encoding UTF8 | ConvertFrom-Json).uiTest
if (-not $u -or $u.exception -or $u.error) { throw "backup: $($u.exception)$($u.error) ($($summary.Error))" }

$sb = @($u.startBackups)
Check "M1a: dnevna kopija pri pokretanju" ($sb.Count -eq 1 -and $sb[0].reason -eq "start" -and ($sb[0].files -join ",") -match "library.db" -and ($sb[0].files -join ",") -match "settings.json") "kopija: $($sb[0].reason), datoteke $($sb[0].files -join ', ')"
Check "M1a: stanje u postavkama" ($u.statusText -match "\d" -and $u.statusText -notmatch "još nema") "'$($u.statusText)'"
Check "M1b: izvoz" ($u.exported -and $u.exportHasEqualizer -and -not $u.exportHasSecretsOrWindow) "izvezeno $($u.exported), EQ $($u.exportHasEqualizer), tajne/prozor $($u.exportHasSecretsOrWindow)"
$a = $u.afterImport
Check "M1b: uvoz se odmah primijeni" ($u.imported -and $a.theme -eq "light" -and [double]$a.volume -eq 20 -and [int]$a.seekStep -eq 15 -and $a.eqPreset -eq "rock" -and $a.af -match "equalizer=" -and -not $a.paused) "tema $($a.theme), glasnoća $($a.volume), korak $($a.seekStep), EQ $($a.eqPreset), af=$($a.af)"
Check "M1b: kopija prije uvoza" ([int]$a.backupsAfter -eq [int]$a.backupsBefore + 1 -and $a.newestReason -eq "import") "kopija $($a.backupsBefore) → $($a.backupsAfter), zadnja '$($a.newestReason)'"
Check "M1b: prozor postavki ponovno otvoren" ([bool]$a.windowReopened) "$($a.windowReopened)"
$b = $u.afterBad
Check "M1b: kriva datoteka odbijena, ništa promijenjeno" (-not $u.badImported -and $b.theme -eq "light" -and [double]$b.volume -eq 20 -and $b.eqPreset -eq "rock") "uvezeno $($u.badImported), tema $($b.theme), glasnoća $($b.volume)"
Check "M1b: uvoz čuva ključeve i položaj prozora" ($a.apiKey -eq "test-kljuc" -and [double]$a.windowLeft -eq 10) "ključ '$($a.apiKey)', WindowLeft $($a.windowLeft)"

# --- V1 Mini Music Player (testni album: 3 pjesme, mp3 / flac / m4a, s oznakama) ---------------------------------
$ffmpeg = if ($env:FFMPEG) { $env:FFMPEG } else { "ffmpeg" }
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

$db = Join-Path $script:DiagDir "ciklus2-mini.db"
Remove-Item "$db*" -ErrorAction SilentlyContinue
$summary = Invoke-Diag -Name "ciklus2_mini" -File (Join-Path $album "01 Prva.mp3") -Seconds 1 -ExtraArgs "--diag-ui-test mini --diag-upnext 2 --diag-db `"$db`"" -TimeoutSeconds 240
$m = (Get-Content -LiteralPath (Join-Path $script:DiagDir "ciklus2_mini.json") -Raw -Encoding UTF8 | ConvertFrom-Json).uiTest
if (-not $m -or $m.exception -or $m.error) { throw "mini: $($m.exception)$($m.error) ($($summary.Error))" }

Check "V1: jedna pjesma otvara cijeli album" (@($m.playlist).Count -eq 3) "popis: $($m.playlist -join ' | ')"
$sw = $m.switch
Check "V1: prebacivanje bez prekida glazbe" (-not $sw.mainVisible -and $sw.miniVisible -and -not $sw.overlayVisible -and [double]$sw.advanced -gt 1.0 -and -not $sw.paused) "glavni $($sw.mainVisible), mini $($sw.miniVisible), kontrole $($sw.overlayVisible), napredak $($sw.advanced) s"
Check "V1: naslov iz oznaka (zadano samo izvođač i pjesma, od 2.1.7 bez kvalitete)" ($sw.title -match "jebaited_cro — Prva pjesma" -and $sw.title -notmatch "kHz") "'$($sw.title)'"
Check "V1: VU metar mjeri stvarnu razinu" ($sw.af -match "astats" -and [double]$sw.vu[0] -gt -40 -and [double]$sw.vu[1] -gt -40) "L $($sw.vu[0]) dB, D $($sw.vu[1]) dB, af=$($sw.af), mpv: $($sw.vuRaw)"
$p = $m.panels
Check "V1: ploče EQ i popis" ([int]$p.eqColumns -eq 11 -and [int]$p.playlistRows -eq 3 -and [double]$p.size[1] -gt [double]$sw.size[1] + 150) "EQ stupaca $($p.eqColumns), redaka $($p.playlistRows), visina $($sw.size[1]) → $($p.size[1])"
$a = $m.autoNext
Check "V1: tipka sljedeća" ($m.afterNext -match "02") "'$($m.afterNext)'"
Check "V1: kraj pjesme = odmah sljedeća (bez kartice)" ($a.current -match "03" -and [double]$a.secondsAfterSeek -lt 4 -and -not $a.upNextCard -and $null -eq $a.resumeOffer) "'$($a.current)' nakon $($a.secondsAfterSeek) s, kartica '$($a.upNextCard)'"
Check "V1: uska traka (dizajn 3)" ([double]$m.compact.size[1] -le 50 -and [double]$m.compact.size[0] -ge 540 -and $m.compact.title -match "Treca") "veličina $($m.compact.size -join ' × '), '$($m.compact.title)'"
Check "V1: ručna veličina (skaliranje i ograničenje)" ([double]$m.scaled.scale -eq 1.5 -and [math]::Abs([double]$m.scaled.widthRatio - 1.5) -le 0.03 -and [double]$m.scaled.saved -eq 1.5 -and [double]$m.maxScale -eq 2.5) "1,5× → širina ×$($m.scaled.widthRatio), najviše $($m.maxScale)"
$bk = $m.back
Check "V1: Ctrl+M natrag na puni player" ($bk.handled -and $bk.mainVisible -and -not $bk.miniVisible -and [double]$bk.advanced -gt 0.8 -and -not $bk.paused -and $bk.af -notmatch "astats") "glavni $($bk.mainVisible), napredak $($bk.advanced) s, af='$($bk.af)'"
Check "V1: CPU (puni player / mini player)" ([double]$m.cpuMiniPlayer -le [double]$m.cpuFullPlayer + 3) ("puni {0} %, mini {1} % jedne jezgre" -f $m.cpuFullPlayer, $m.cpuMiniPlayer)

# --- 1.4.1: dva equalizera, font i VU, automatsko prebacivanje, položaj ---------------------------------------
$vs = $m.vuStyles
Check "1.4.1: VU izgledi (trake, LED, linije) mjere, isključen ne mjeri" ($vs.bars.af -match "astats" -and $vs.led.af -match "astats" -and $vs.lines.af -match "astats" -and [double]$vs.led.level -gt -40 -and $vs.off.af -notmatch "astats") "trake $($vs.bars.level) dB, LED $($vs.led.level) dB, linije $($vs.lines.level) dB, isključen af='$($vs.off.af)'"
Check "1.4.1: font zaslona" ($m.font -match "Bahnschrift") "'$($m.font)', snimka $($m.ledScreenshot)"
$me = $m.musicEq
Check "1.4.1: glazba ima svoj equalizer" ($me.musicMode -and $me.af -match "equalizer=f=31:t=o:w=1:g=7" -and $me.panelBoundToMusic -and $me.title -match "glazba|music") "af=$($me.af), ploča glazba $($me.panelBoundToMusic), '$($me.title)'"
$ov = $m.openVideo
Check "1.4.1: film otvara puni player i filmski EQ" ($ov.mainVisible -and -not $ov.miniVisible -and -not $ov.musicMode -and $ov.af -notmatch "equalizer") "glavni $($ov.mainVisible), mini $($ov.miniVisible), af='$($ov.af)'"
$om = $m.openMusic
Check "1.4.1: glazba otvara mini player i glazbeni EQ" (-not $om.mainVisible -and $om.miniVisible -and $om.musicMode -and $om.af -match "equalizer=f=31:t=o:w=1:g=7" -and [int]$om.playlist -eq 3) "glavni $($om.mainVisible), mini $($om.miniVisible), popis $($om.playlist)"
$po = $m.position
Check "1.4.1: mini player se otvara na zapamćenom mjestu" ([double]$po.reopened[0] -eq 210 -and [double]$po.reopened[1] -eq 160 -and [double]$po.saved[0] -eq 210) "spremljeno $($po.saved -join ', '), novi prozor $($po.reopened -join ', ')"

$results | Format-Table -AutoSize -Wrap
$failed = @($results | Where-Object { $_.Rezultat -ne "OK" }).Count
Write-Host ("Ukupno {0}, uspješno {1}, grešaka {2}" -f $results.Count, ($results.Count - $failed), $failed)
if ($failed -gt 0) { exit 1 }
