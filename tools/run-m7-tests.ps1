<#
.SYNOPSIS
  Testovi M7 (Windows integracija) u pravom playeru, bez promjena u sustavu:
  - registracija formata i "Otvori pomoću" u zasebnom testnom ključu registra (HKCU\Software\jebaited_player_m7test),
  - jedna instanca s pravim procesima (vlastito testno ime, ne dira tvoj otvoreni player):
    drugo pokretanje dok je player otvoren, više datoteka pokrenutih istodobno (kao Explorer), pokretanje bez datoteke,
    dijagnostika bez imena instance radi zasebno.
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$exe = $script:Exe
$m1 = Join-Path $root "testdata\m1"
$m3 = Join-Path $root "testdata\m3"
$film = Join-Path $m3 "Filmovi\Film.Za.Test.2026.1080p.BluRay.x264-GRP.mp4"
$season = Join-Path $m3 "Serija.Test\Season 01"
$episodes = @(Get-ChildItem -LiteralPath $season -Filter *.mp4 | Sort-Object Name | Select-Object -ExpandProperty FullName)
$video1080 = Join-Path $m1 "1080p_h264_aac.mp4"
$mp3 = Join-Path $m1 "audio_only.mp3"
foreach ($f in @($film, $video1080, $mp3) + $episodes) { if (-not (Test-Path -LiteralPath $f)) { throw "Nema testne datoteke: $f (pokreni make-testdata.ps1 i make-library-testdata.ps1)" } }
if ($episodes.Count -lt 3) { throw "Trebaju barem 3 testne epizode u $season" }

$db = Join-Path $script:DiagDir "m7-test.db"
$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Rezultat = $(if ($Ok) { "OK" } else { "GREŠKA" }); Test = $Name; Detalji = $Detail })
}
function Quote([string]$p) { "`"$p`"" }
function ReadJson([string]$path, [int]$timeoutSec = 90) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
        if (Test-Path -LiteralPath $path) {
            Start-Sleep -Milliseconds 300
            try { return Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json } catch { }
        }
        Start-Sleep -Milliseconds 300
    }
    return $null
}
function WaitWindow($proc) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 30) {
        $proc.Refresh()
        if ($proc.HasExited -or $proc.MainWindowHandle -ne [IntPtr]::Zero) { return }
        Start-Sleep -Milliseconds 100
    }
}
function WaitExit($proc, [int]$timeoutSec) {
    if (-not $proc.WaitForExit($timeoutSec * 1000)) { try { $proc.Kill() } catch { }; return $null }
    return $proc.ExitCode
}

# --- 1) Registracija u testnom ključu --------------------------------------------------------------------
$regRoot = "Software\jebaited_player_m7test"
$hkcu = [Microsoft.Win32.Registry]::CurrentUser
$hkcu.DeleteSubKeyTree($regRoot, $false)
$p = Start-Process -FilePath $exe -ArgumentList "--register --diag-registry-root $regRoot" -PassThru -Wait
$cmd = $hkcu.OpenSubKey("$regRoot\Classes\jebaited_player.mkv\shell\open\command")
$cmdValue = if ($cmd) { $cmd.GetValue("") } else { $null }
$openWith = $hkcu.OpenSubKey("$regRoot\Classes\.mp4\OpenWithProgids")
$hasOpenWith = $openWith -and ($openWith.GetValueNames() -contains "jebaited_player.mp4")
$caps = $hkcu.OpenSubKey("$regRoot\jebaited_player\Capabilities\FileAssociations")
$capCount = if ($caps) { $caps.ValueCount } else { 0 }
$ok = ($p.ExitCode -eq 0) -and ($cmdValue -eq "`"$exe`" `"%1`"") -and $hasOpenWith -and ($capCount -gt 10)
Check "Registracija formata (testni ključ)" $ok ("izlaz {0}; naredba {1}; Otvori pomoću .mp4: {2}; Zadane aplikacije: {3} formata" -f $p.ExitCode, $cmdValue, $hasOpenWith, $capCount)

$p = Start-Process -FilePath $exe -ArgumentList "--unregister --diag-registry-root $regRoot" -PassThru -Wait
$left = $hkcu.OpenSubKey("$regRoot\Classes\jebaited_player.mkv")
$openWith = $hkcu.OpenSubKey("$regRoot\Classes\.mp4\OpenWithProgids")
$leftOpenWith = $openWith -and ($openWith.GetValueNames() -contains "jebaited_player.mp4")
$leftApp = $hkcu.OpenSubKey("$regRoot\jebaited_player")
Check "Uklanjanje registracije" (($p.ExitCode -eq 0) -and -not $left -and -not $leftOpenWith -and -not $leftApp) ("izlaz {0}; ostalo: ProgID {1}, Otvori pomoću {2}, aplikacija {3}" -f $p.ExitCode, [bool]$left, $leftOpenWith, [bool]$leftApp)
$hkcu.DeleteSubKeyTree($regRoot, $false)

$p = Start-Process -FilePath $exe -ArgumentList "--register --diag-registry-root SYSTEM\x" -PassThru -Wait
Check "Registracija odbija ključ izvan Software" ($p.ExitCode -eq 2) "izlaz $($p.ExitCode)"

# --- 2) Drugo pokretanje dok je player otvoren -------------------------------------------------------------
$name = "m7test-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
$json = Join-Path $script:DiagDir "M7_drugo_pokretanje.json"
Remove-Item -LiteralPath $json -ErrorAction SilentlyContinue
$primary = Start-Process -FilePath $exe -PassThru -ArgumentList ("--diag 1 --diag-warmup 5000 --diag-db {0} --diag-instance-name {1} --diag-out {2} {3}" -f (Quote $db), $name, (Quote $json), (Quote $video1080))
# Pričekaj da se prozor pojavi, pa još dulje od razmaka u kojem se datoteke spajaju u jedan popis (1,5 s).
WaitWindow $primary
Start-Sleep -Milliseconds 2500
$sw = [Diagnostics.Stopwatch]::StartNew()
$second = Start-Process -FilePath $exe -PassThru -ArgumentList ("--diag-instance-name {0} {1}" -f $name, (Quote $film))
$code = WaitExit $second 15
$secondMs = $sw.ElapsedMilliseconds
$r = ReadJson $json
$null = WaitExit $primary 30
$ok = ($code -eq 0) -and $r -and ($r.currentFile -eq (Split-Path $film -Leaf)) -and ($r.externalOpens.Count -eq 1) -and ($r.processId -eq $primary.Id)
Check "Drugo pokretanje otvara datoteku u otvorenom playeru" $ok ("drugi proces: izlaz {0} za {1} ms; glavni prikazuje: {2}; primljeno: {3}" -f $code, $secondMs, $r.currentFile, (($r.externalOpens | ForEach-Object { $_ -join ", " }) -join " | "))

# --- 3) Više datoteka istodobno (kao Explorer: jedan proces po datoteci) -------------------------------
$name = "m7test-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
$json = Join-Path $script:DiagDir "M7_vise_datoteka.json"
Remove-Item -LiteralPath $json -ErrorAction SilentlyContinue
$files = @($episodes[2], $film, $episodes[0], $episodes[1])
$procs = foreach ($f in $files) {
    Start-Process -FilePath $exe -PassThru -ArgumentList ("--diag 1 --diag-warmup 4000 --diag-db {0} --diag-instance-name {1} --diag-out {2} {3}" -f (Quote $db), $name, (Quote $json), (Quote $f))
}
$r = ReadJson $json
$codes = $procs | ForEach-Object { WaitExit $_ 40 }
$mainProc = $procs | Where-Object { $_.Id -eq $r.processId }
$others = @($procs | Where-Object { $_.Id -ne $r.processId })
$othersOk = ($others.Count -eq 3) -and (@($others | Where-Object { $_.ExitCode -ne 0 }).Count -eq 0)
$playlist = @($r.library.playlist)
$ok = $r -and $mainProc -and $othersOk -and ($playlist.Count -eq 4) -and ($r.library.playlistTitle -eq "Odabrane datoteke")
Check "Četiri datoteke odjednom: jedan player, jedan popis" $ok ("glavni PID {0}; ostali izašli: {1}; popis ({2}): {3}; trenutno: {4}" -f $r.processId, $othersOk, $playlist.Count, ($playlist -join " / "), $r.currentFile)

# --- 4) Pokretanje bez datoteke dok je player otvoren: samo ga dovede naprijed ---------------------------
$name = "m7test-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
$json = Join-Path $script:DiagDir "M7_bez_datoteke.json"
Remove-Item -LiteralPath $json -ErrorAction SilentlyContinue
$primary = Start-Process -FilePath $exe -PassThru -ArgumentList ("--diag 1 --diag-warmup 4000 --diag-db {0} --diag-instance-name {1} --diag-out {2} {3}" -f (Quote $db), $name, (Quote $json), (Quote $mp3))
Start-Sleep -Seconds 3
$second = Start-Process -FilePath $exe -PassThru -ArgumentList "--diag-instance-name $name"
$code = WaitExit $second 15
$r = ReadJson $json
$null = WaitExit $primary 30
$ok = ($code -eq 0) -and $r -and ($r.currentFile -eq "audio_only.mp3") -and ($r.externalOpens.Count -eq 1) -and (@($r.externalOpens[0]).Count -eq 0)
Check "Pokretanje bez datoteke ne prekida reprodukciju" $ok ("drugi proces: izlaz {0}; i dalje svira: {1}; primljeno zahtjeva: {2}" -f $code, $r.currentFile, $r.externalOpens.Count)

# --- 5) Dijagnostika bez imena instance: dva zasebna playera ----------------------------------------------
$jsonA = Join-Path $script:DiagDir "M7_zasebno_A.json"
$jsonB = Join-Path $script:DiagDir "M7_zasebno_B.json"
Remove-Item -LiteralPath $jsonA, $jsonB -ErrorAction SilentlyContinue
$a = Start-Process -FilePath $exe -PassThru -ArgumentList ("--diag 1 --diag-warmup 1500 --diag-db {0} --diag-out {1} {2}" -f (Quote $db), (Quote $jsonA), (Quote $video1080))
$b = Start-Process -FilePath $exe -PassThru -ArgumentList ("--diag 1 --diag-warmup 1500 --diag-db {0} --diag-out {1} {2}" -f (Quote $db), (Quote $jsonB), (Quote $film))
$ra = ReadJson $jsonA; $rb = ReadJson $jsonB
$null = WaitExit $a 30; $null = WaitExit $b 30
$ok = $ra -and $rb -and ($ra.processId -ne $rb.processId) -and ($ra.currentFile -ne $rb.currentFile)
Check "Testovi ne šalju datoteke tvom otvorenom playeru" $ok ("A: {0} (PID {1}); B: {2} (PID {3})" -f $ra.currentFile, $ra.processId, $rb.currentFile, $rb.processId)

$results | Format-Table -AutoSize -Wrap
$failed = @($results | Where-Object Rezultat -ne "OK").Count
"M7: {0}/{1} OK" -f ($results.Count - $failed), $results.Count
if ($failed -gt 0) { exit 1 }
