<#
.SYNOPSIS
  Test nadogradnje bez promjena u sustavu: prijenosna verzija 0.99.0 se iz lokalne mape s izdanjima nadogradi na
  0.99.1, a testne postavke i podaci (zasebna mapa, JEBAITED_DATA_DIR) moraju ostati netaknuti.

.DESCRIPTION
  1. tools\release.ps1 napravi izdanja 0.99.0 i 0.99.1 u testdata\update\releases (0.99.1 uz paket razlika).
  2. Prijenosna 0.99.0 se raspakira u testdata\update\app (bez instalacije, prečaca i registra).
  3. Player 0.99.0 pronađe 0.99.1, preuzme ga i pri zatvaranju ga Velopack instalira.
  4. Ponovno pokretanje mora biti 0.99.1, bez nove nadogradnje, s istim postavkama i podacima.
#>
param([switch]$SkipBuild)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$work = Join-Path $root "testdata\update"
$releases = Join-Path $work "releases"
$app = Join-Path $work "app"
$data = Join-Path $work "data"
$video = Join-Path $root "testdata\m1\1080p_h264_aac.mp4"
if (-not (Test-Path $video)) { throw "Nema testnog videa $video (pokreni make-testdata.ps1)." }

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Rezultat = $(if ($Ok) { "OK" } else { "GREŠKA" }); Test = $Name; Detalji = $Detail })
}

if (Get-Process jebaited_player -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($work, [StringComparison]::OrdinalIgnoreCase) }) {
    throw "Testni player iz $work je otvoren."
}

if (-not $SkipBuild) {
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $releases | Out-Null
    & (Join-Path $PSScriptRoot "release.ps1") -Version 0.99.0 -OutputDir $releases -SkipGitHubDownload | Select-Object -Last 1
    Copy-Item (Join-Path $releases "JebaitedPlayer-win-Portable.zip") (Join-Path $work "portable-0.99.0.zip")
    & (Join-Path $PSScriptRoot "release.ps1") -Version 0.99.1 -OutputDir $releases -SkipGitHubDownload | Select-Object -Last 1
}

$delta = Get-ChildItem $releases -Filter "*0.99.1-delta.nupkg" -ErrorAction SilentlyContinue
$full = Get-ChildItem $releases -Filter "*0.99.1-full.nupkg" -ErrorAction SilentlyContinue
Check "Izdanje: instalacija, prijenosna, paketi" ((Test-Path (Join-Path $releases "JebaitedPlayer-win-Setup.exe")) -and $full -and $delta) `
    ("Setup.exe {0:N0} MB, puni paket {1:N0} MB, paket razlika {2:N1} MB" -f ((Get-Item (Join-Path $releases "JebaitedPlayer-win-Setup.exe")).Length / 1MB), ($full.Length / 1MB), ($delta.Length / 1MB))

# Prijenosna 0.99.0 i testni podaci.
if (Test-Path $app) { Remove-Item $app -Recurse -Force }
if (Test-Path $data) { Remove-Item $data -Recurse -Force }
Expand-Archive (Join-Path $work "portable-0.99.0.zip") -DestinationPath $app
New-Item -ItemType Directory -Force -Path (Join-Path $data "cache") | Out-Null
'{ "Volume": 37, "Theme": "dark", "SubtitleLanguages": ["hr", "en"] }' | Set-Content (Join-Path $data "settings.json") -Encoding UTF8
"podaci koji moraju ostati" | Set-Content (Join-Path $data "cache\marker.txt") -Encoding UTF8
$hashBefore = Get-ChildItem $data -Recurse -File | Sort-Object FullName | ForEach-Object { "{0} {1}" -f $_.FullName.Substring($data.Length), (Get-FileHash $_.FullName).Hash }

$exe = Join-Path $app "jebaited_player.exe"
$currentExe = Join-Path $app "current\jebaited_player.exe"
function Run([string]$json) {
    Remove-Item $json -ErrorAction SilentlyContinue
    $env:JEBAITED_DATA_DIR = $data
    try {
        $p = Start-Process -FilePath $exe -PassThru -ArgumentList ("--diag 1 --diag-warmup 1500 --diag-out `"{0}`" --diag-update-source `"{1}`" `"{2}`"" -f $json, $releases, $video)
    }
    finally {
        Remove-Item Env:\JEBAITED_DATA_DIR
    }
    if (-not $p.WaitForExit(120000)) { $p.Kill(); throw "Player nije završio." }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path $json) -and $sw.Elapsed.TotalSeconds -lt 20) { Start-Sleep -Milliseconds 200 }
    return Get-Content $json -Raw -Encoding UTF8 | ConvertFrom-Json
}
function ExeVersion { if (Test-Path $currentExe) { (Get-Item $currentExe).VersionInfo.ProductVersion } else { "(nema $currentExe)" } }

$before = ExeVersion
$r1 = Run (Join-Path $work "r1.json")
$u = $r1.update
Check "0.99.0 pronalazi i preuzima 0.99.1" ($u.installed -and $u.portable -and $u.current -eq "0.99.0" -and $u.ready -eq "0.99.1" -and $u.state -eq "Ready") `
    ("exe {0}; Velopack: instalirano {1}, prijenosna {2}, trenutna {3}, spremna {4} ({5})" -f $before, $u.installed, $u.portable, $u.current, $u.ready, $u.status)

# Velopack instalira nakon zatvaranja playera (Update.exe čeka da proces završi).
$sw = [Diagnostics.Stopwatch]::StartNew()
while ((ExeVersion) -ne "0.99.1" -and $sw.Elapsed.TotalSeconds -lt 120) { Start-Sleep -Milliseconds 500 }
$applySeconds = [Math]::Round($sw.Elapsed.TotalSeconds, 1)
Check "Nadogradnja se instalira pri zatvaranju" ((ExeVersion) -eq "0.99.1") ("verzija exe-a nakon zatvaranja: {0} (za {1} s)" -f (ExeVersion), $applySeconds)

# Velopack nakon zamjene još pokrene novu verziju s "--veloapp-updated" (kuka); pričekaj da sve završi.
$sw.Restart()
while ($sw.Elapsed.TotalSeconds -lt 60 -and (Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($app, [StringComparison]::OrdinalIgnoreCase) })) {
    Start-Sleep -Milliseconds 300
}
Start-Sleep -Seconds 1

# Prijenosna verzija ne smije ništa registrirati u Windowsu (ni nakon nadogradnje).
$reg = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Software\jebaited_player")
$regExe = if ($reg) { $reg.GetValue("ExePath") } else { $null }
$leaked = $regExe -and $regExe.StartsWith($work, [StringComparison]::OrdinalIgnoreCase)
$log = Get-Content (Join-Path $data "logs\app.log") -Raw -Encoding UTF8
Check "Prijenosna verzija ne mijenja ništa u Windowsu" (-not $leaked -and $log -match "preskočena \(prijenosna verzija\)") `
    ("registracija u Windowsu: {0}" -f $(if ($regExe) { $regExe } else { "nema" }))
if ($leaked) { Start-Process -FilePath $currentExe -ArgumentList "--unregister" -Wait }

$r2 = Run (Join-Path $work "r2.json")
$u = $r2.update
Check "Nova verzija radi i nema dalje nadogradnje" ($u.current -eq "0.99.1" -and -not $u.ready -and $u.state -eq "UpToDate" -and $r2.hasMedia) `
    ("trenutna {0}, stanje {1}, video reproduciran: {2}" -f $u.current, $u.state, $r2.hasMedia)

# Automatske kopije (M1a, ciklus 2: pri pokretanju i prije nadogradnje) su nove datoteke po pravilu, pa se ne uspoređuju.
$hashAfter = Get-ChildItem $data -Recurse -File | Sort-Object FullName | Where-Object { $_.FullName -notmatch '\\logs\\|\\cache\\(shaders|speech|subtitles|search|downloads)\\|\\backups\\' } |
    ForEach-Object { "{0} {1}" -f $_.FullName.Substring($data.Length), (Get-FileHash $_.FullName).Hash }
$same = (Compare-Object $hashBefore $hashAfter | Measure-Object).Count -eq 0
Check "Postavke i podaci ostaju" ($same -and $u.settingsVolume -eq 37 -and $u.dataDir -eq $data) `
    ("isti sadržaj: {0}; glasnoća iz postavki nakon nadogradnje: {1}; mapa podataka: {2}" -f $same, $u.settingsVolume, $u.dataDir)

# M1a / M3a: prije nadogradnje nastaje kopija postavki, jednaka postavkama prije nadogradnje.
$updateBackup = Get-ChildItem (Join-Path $data "backups") -Directory -ErrorAction SilentlyContinue | Where-Object Name -like "*_update-0.99.1" | Select-Object -First 1
$backupSame = $updateBackup -and (Get-FileHash (Join-Path $updateBackup.FullName "settings.json")).Hash -eq (Get-FileHash (Join-Path $data "settings.json")).Hash
Check "Kopija postavki prije nadogradnje" ([bool]$backupSame) ("kopija: {0}" -f $(if ($updateBackup) { $updateBackup.Name } else { "nema" }))

$results | Format-Table -AutoSize -Wrap
$failed = @($results | Where-Object Rezultat -ne "OK").Count
"Nadogradnja: {0}/{1} OK" -f ($results.Count - $failed), $results.Count
if ($failed -gt 0) { exit 1 }
