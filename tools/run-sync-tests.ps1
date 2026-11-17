<#
.SYNOPSIS
  Testovi automatske sinkronizacije (M5) u pravom playeru, na testnom filmu iz make-sync-testdata.ps1.
  Za svaku varijantu titlova: presuda, primijenjeni pomak i brzina, te koji se titl stvarno prikazuje kasno u filmu.
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$m5 = Join-Path $root "testdata\m5"
if (-not (Test-Path (Join-Path $m5 "expected.json"))) { throw "Pokreni prvo tools\make-sync-testdata.ps1" }
$expected = Get-Content (Join-Path $m5 "expected.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$mkv = Join-Path $m5 "sync_film_51.mkv"
$mp4 = Join-Path $m5 "sync_film.mp4"
$diagSpeech = Join-Path ([IO.Path]::GetTempPath()) "jebaited_player_diag\speech"

# Titl iz ispravne datoteke koji je na ekranu kasno u filmu (jedan redak, barem 2 s): tamo se vidi i drift.
function Read-Srt([string]$path) {
    $blocks = ([IO.File]::ReadAllText($path, [Text.Encoding]::UTF8) -split "\r?\n\r?\n") | Where-Object { $_.Trim() }
    foreach ($b in $blocks) {
        $lines = $b -split "\r?\n"
        if ($lines[1] -match '(\d+):(\d+):(\d+),(\d+) --> (\d+):(\d+):(\d+),(\d+)') {
            $s = [int]$Matches[1] * 3600 + [int]$Matches[2] * 60 + [int]$Matches[3] + [int]$Matches[4] / 1000
            $e = [int]$Matches[5] * 3600 + [int]$Matches[6] * 60 + [int]$Matches[7] + [int]$Matches[8] / 1000
            [pscustomobject]@{ Start = $s; End = $e; Text = ($lines[2..($lines.Count - 1)] -join "`n") }
        }
    }
}
$truth = Read-Srt (Join-Path $m5 "sync_ok.srt")
$probeCue = $truth | Where-Object { $_.Start -gt 1380 -and ($_.End - $_.Start) -ge 2 -and $_.Text -notmatch "`n" } | Select-Object -First 1
$probeAt = [Math]::Round(($probeCue.Start + $probeCue.End) / 2, 2)
"Provjera prikaza u {0:N1} s: '{1}'" -f $probeAt, $probeCue.Text

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Rezultat = $(if ($Ok) { "OK" } else { "GREŠKA" }); Test = $Name; Detalji = $Detail })
}
function Run([string]$Name, [string[]]$Files, [string]$Extra, [int]$Seconds = 1) {
    $null = Invoke-Diag -Name $Name -File $Files -Seconds $Seconds -ExtraArgs $Extra -TimeoutSeconds 150
    $json = Join-Path $script:DiagDir (($Name -replace '[^\w\-]+', '_') + ".json")
    if (Test-Path -LiteralPath $json) { return Get-Content -LiteralPath $json -Raw -Encoding UTF8 | ConvertFrom-Json }
    return $null
}
# Najveća razlika primijenjene i ispravne korekcije na početku i kraju titlova (ms).
function TimingError($r, $exp) {
    $sa = [double]$r.autoSync.appliedSpeed; $da = [double]$r.autoSync.appliedDelayMs / 1000
    $se = [double]$exp.speed; $de = [double]$exp.offset
    $last = 1490 / $se
    return [Math]::Round(1000 * [Math]::Max([Math]::Abs(($sa - $se) * 10 + $da - $de), [Math]::Abs(($sa - $se) * $last + $da - $de)))
}

# 1) Sve varijante na 5.1 MKV-u. Prvo pokretanje analizira zvuk, ostala koriste spremljenu analizu.
if (Test-Path $diagSpeech) { Remove-Item $diagSpeech -Recurse -Force }
foreach ($p in $expected.PSObject.Properties) {
    $srt = Join-Path $m5 $p.Name
    $exp = $p.Value
    $r = Run ("SYNC " + $p.Name) @($mkv, $srt) "--diag-wait-sync 1 --diag-probe $probeAt"
    if (-not $r) { Check $p.Name $false "nema rezultata (player se nije zatvorio?)"; continue }
    $a = $r.autoSync
    $shown = $r.subtitleProbe.'sub-text'
    $detail = "{0}, pomak {1:N3} s, brzina {2:N6}, dijelovi {3}/{4}, analiza {5} ms, poravnanje {6} ms" -f $a.verdict, ($a.appliedDelayMs / 1000), $a.appliedSpeed, $a.agreeing, $a.windows, $a.analysisMs, $a.alignMs
    if ($exp.verdict -in @("InSync", "Corrected")) {
        $err = TimingError $r $exp
        $ok = ($a.verdict -eq $exp.verdict) -and ($err -lt 100) -and ($shown -eq $probeCue.Text)
        Check $p.Name $ok "$detail; greška najviše $err ms; prikazano '$shown'"
    }
    elseif ($exp.verdict -eq "CorrectedInParts") {
        # Druga montaža: ispravljeno po dijelovima, pa se i u drugoj polovici filma prikazuje pravi titl.
        $ok = ($a.verdict -eq $exp.verdict) -and ($shown -eq $probeCue.Text) -and ($a.segments.Count -ge 2)
        Check $p.Name $ok "$detail; dijelova $($a.segments.Count); $($a.timingSummary); prikazano '$shown'"
    }
    else {
        $ok = ($a.verdict -eq $exp.verdict) -and ([Math]::Abs([double]$a.appliedDelayMs) -lt 1) -and ([Math]::Abs([double]$a.appliedSpeed - 1) -lt 1e-9)
        Check $p.Name $ok "$detail; titlovi ostavljeni kako jesu"
    }
}

# 2) Zapamćeno: drugo otvaranje istog videa i titla ne analizira ponovno i odmah ima korekciju.
$db = Join-Path $script:DiagDir "m5-test.db"
Remove-Item $db -ErrorAction SilentlyContinue
$srt = Join-Path $m5 "sync_25fps.srt"
$r1 = Run "SYNC zapamti 1" @($mkv, $srt) "--diag-db `"$db`" --diag-wait-sync 1 --diag-probe $probeAt"
$r2 = Run "SYNC zapamti 2" @($mkv, $srt) "--diag-db `"$db`" --diag-probe $probeAt"
$ok = ($r1.autoSync.verdict -eq "Corrected") -and ($r2.autoSync.runs -eq 0) -and ([Math]::Abs([double]$r2.autoSync.appliedSpeed - [double]$r1.autoSync.appliedSpeed) -lt 1e-9) -and ($r2.subtitleProbe.'sub-text' -eq $probeCue.Text)
Check "Zapamćena korekcija (bez ponovne analize)" $ok ("1.: {0} brzina {1:N6}; 2.: provjera {2}×, brzina {3:N6}, prikazano '{4}'" -f $r1.autoSync.verdict, $r1.autoSync.appliedSpeed, $r2.autoSync.runs, $r2.autoSync.appliedSpeed, $r2.subtitleProbe.'sub-text')

# 3) Titlovi s interneta: krivi (više preuzimanja) se odbaci, pravi se učita i sinkronizira.
$saved = Join-Path $m5 "sync_film.hr.srt"
Remove-Item $saved -ErrorAction SilentlyContinue
$r = Run "SYNC internet" @($mp4) "--diag-fake-online m5 --diag-wait-sync 1 --diag-probe $probeAt"
$ok = ($r.online.lastAutoResult -like "auto:*Corrected*") -and (Test-Path $saved) -and ($r.autoSync.verdict -eq "Corrected") -and ([Math]::Abs([double]$r.autoSync.appliedDelayMs + 2500) -lt 100) -and ($r.subtitleProbe.'sub-text' -eq $probeCue.Text)
Check "Internet: krivi odbačen, pravi sinkroniziran" $ok ("online '{0}'; spremljeno: {1}; {2} pomak {3:N3} s; prikazano '{4}'" -f $r.online.lastAutoResult, (Test-Path $saved), $r.autoSync.verdict, ($r.autoSync.appliedDelayMs / 1000), $r.subtitleProbe.'sub-text')
Remove-Item $saved -ErrorAction SilentlyContinue

# 4) Opterećenje dok se zvuk analizira (bez spremljene analize), video se reproducira.
if (Test-Path $diagSpeech) { Remove-Item $diagSpeech -Recurse -Force }
$perf = Invoke-Diag -Name "SYNC opterecenje" -File @($mkv, (Join-Path $m5 "sync_kasni_2500.srt")) -Seconds 10 -ExtraArgs "--diag-warmup 100" -TimeoutSeconds 120
$pj = Get-Content (Join-Path $script:DiagDir "SYNC_opterecenje.json") -Raw -Encoding UTF8 | ConvertFrom-Json
$samples = ($pj.cpuSamples | ForEach-Object { "{0:N1}" -f $_ }) -join " "
Check "Reprodukcija tijekom analize" ($perf.Ok -and $perf.Drops -eq "0/0" -and $pj.autoSync.analysisMs -gt 0) ("analiza {0} ms; CPU po sekundi (% svih jezgri): {1}; maks. RAM {2:N0} MB; ispušteni kadrovi {3}" -f $pj.autoSync.analysisMs, $samples, $pj.workingSetMaxMB, $perf.Drops)

$results
"{0} / {1} prošlo" -f @($results | Where-Object Rezultat -eq "OK").Count, $results.Count
