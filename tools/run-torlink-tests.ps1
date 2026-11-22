<#
.SYNOPSIS
  Stvarni testovi Torlinka u pravom playeru (nad testnom mapom podataka, nikad nad tvojom), s pravim runtimeom (Node + Torlink),
  pravom mrezom i legalnim otvorenim filmovima (Blender Foundation: Sintel, Big Buck Bunny).

.DESCRIPTION
  Scenarij `torlink` (MainWindow.TorlinkTests.cs) vodi cijeli tok kroz pravi UI i pravi miš:
  ukljucivanje s upozorenjem i instalacijom iz "GitHuba" (lokalni poslužitelj izdanja), klik na "Pretrazi online", pretraga, preuzimanje,
  pauza/nastavak, povratak na Home (preuzimanje se ne prekida), cip, Esc, duplikat, pretraga bez interneta, iskljucivanje tijekom
  preuzimanja i ponovno ukljucivanje, pad procesa, azuriranje (uspjesno r2 i neispravno r3 s automatskim vracanjem), GitHub nedostupan,
  rucno vracanje. Zatim se player zatvori i ponovno pokrene (scenarij `torlinkresume`): nedovrseno preuzimanje se nastavlja samo.
  Nakon zatvaranja ne smije ostati nijedan Torlink proces.

.PARAMETER RuntimeZip
  Zip iz tools\build-torlink-runtime.ps1. Ako se ne zada, gradi se (traži mrežu: Node i GitHub).

.EXAMPLE
  .\tools\run-torlink-tests.ps1 -RuntimeZip "$env:TEMP\torlink-runtime-out\torlink-runtime-1.11.1-r1-win-x64.zip"
#>
param([string]$RuntimeZip, [switch]$ReuseResults)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$clip = Join-Path $root "testdata\ciklus1\Kratki.Film.2026.mkv"
if (-not (Test-Path $clip)) { throw "Nema testnog klipa: pokreni prvo tools\run-ciklus1-tests.ps1." }
if (-not (Test-Path $script:Exe)) { throw "Nema exe-a: $script:Exe (dotnet build -c Release)" }

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [object]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Test = $Name; Rezultat = $(if ([bool]$Ok) { "OK" } else { "GRESKA" }); Detalji = $Detail })
}
function Read-Ui([string]$Name) {
    $u = (Get-Content -LiteralPath (Join-Path $script:DiagDir "$Name.json") -Raw -Encoding UTF8 | ConvertFrom-Json).uiTest
    if (-not $u -or $u.exception -or $u.error) { throw "$Name`: $($u.exception)$($u.error)" }
    return $u
}

if (-not $ReuseResults) {
# ---------- runtime paketi: r1 (pravi), r2 (ispravna nova revizija), r3 (neispravan node.exe) ----------
if (-not $RuntimeZip) {
    $out = Join-Path $root "publish\torlink-runtime"
    & (Join-Path $PSScriptRoot "build-torlink-runtime.ps1") -UpstreamTag v1.11.1 -Revision 1 -OutDir $out
    $RuntimeZip = Join-Path $out "torlink-runtime-1.11.1-r1-win-x64.zip"
}
$RuntimeZip = (Resolve-Path $RuntimeZip).Path
$work = Join-Path $root "testdata\torlink"
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $work | Out-Null
$zips = Join-Path $work "zips"
New-Item -ItemType Directory -Force -Path $zips | Out-Null
$r1 = Join-Path $zips (Split-Path $RuntimeZip -Leaf)
Copy-Item $RuntimeZip $r1

Add-Type -AssemblyName System.IO.Compression.FileSystem
function New-Variant([int]$Revision, [scriptblock]$Mutate) {
    $dir = Join-Path $work ("variant-r$Revision")
    [IO.Compression.ZipFile]::ExtractToDirectory($r1, $dir)
    $manifestFile = Join-Path $dir "MANIFEST.json"
    $m = Get-Content $manifestFile -Raw | ConvertFrom-Json
    & $Mutate $dir $m
    $m.revision = $Revision
    [IO.File]::WriteAllText($manifestFile, ($m | ConvertTo-Json -Depth 6), (New-Object System.Text.UTF8Encoding($false)))
    $name = "torlink-runtime-$($m.torlinkVersion)-r$Revision-win-x64.zip"
    $zipPath = Join-Path $zips $name
    $zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
    try {
        Get-ChildItem $dir -Recurse -File | ForEach-Object {
            $entry = $_.FullName.Substring($dir.Length).TrimStart('\', '/').Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entry, 'Optimal')
        }
    } finally { $zip.Dispose() }
    Remove-Item $dir -Recurse -Force
    return $zipPath
}
$r2 = New-Variant 2 { param($dir, $m)
    Add-Content (Join-Path $dir "BUILD-INFO.txt") "r2 test revision"
    $m.files.'BUILD-INFO.txt' = (Get-FileHash (Join-Path $dir "BUILD-INFO.txt") -Algorithm SHA256).Hash.ToLowerInvariant()
}
$r3 = New-Variant 3 { param($dir, $m)
    [IO.File]::WriteAllText((Join-Path $dir "node.exe"), "this is not an executable")
    $m.files.'node.exe' = (Get-FileHash (Join-Path $dir "node.exe") -Algorithm SHA256).Hash.ToLowerInvariant()
}

# ---------- 1. glavni scenarij ----------
$data = Join-Path $work "data"
$dl = Join-Path $work "dl"
New-Item -ItemType Directory -Force -Path $data, $dl | Out-Null
$settings = @{ Language = "hr"; AutoUpdate = $false; OnlineWizardOffered = $true; Theme = "dark"; Torlink = @{ DownloadDir = $dl; MaxDownloads = 3 } } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $data "settings.json"), $settings, (New-Object System.Text.UTF8Encoding($false)))

$env:JEBAITED_DATA_DIR = $data
$env:JEBAITED_TORLINK_API = "http://127.0.0.1:19333"
$env:JEBAITED_TORLINK_ZIP_R1 = $r1
$env:JEBAITED_TORLINK_ZIP_R2 = $r2
$env:JEBAITED_TORLINK_ZIP_R3 = $r3
try { $null = Invoke-Diag -Name "torlink_main" -File $clip -Seconds 1 -ExtraArgs "--diag-ui-test torlink" -TimeoutSeconds 1500 }
finally { Remove-Item Env:JEBAITED_DATA_DIR, Env:JEBAITED_TORLINK_API, Env:JEBAITED_TORLINK_ZIP_R1, Env:JEBAITED_TORLINK_ZIP_R2, Env:JEBAITED_TORLINK_ZIP_R3 -ErrorAction SilentlyContinue }
}
else { $work = Join-Path $root 'testdata\torlink'; $data = Join-Path $work "data" }
$t = Read-Ui "torlink_main"
Check "scenarij je dosao do kraja bez izuzetka" (-not $t.scenarioFailure -and $t.done) "$($t.scenarioFailure) $($t.failureState | ConvertTo-Json -Compress)"

$i = $t.initial
Check "0: iskljuceno je zadano: nema gumba, runtimea ni procesa" ((-not $i.enabled) -and $i.status -eq "Disabled" -and (-not $i.homeButtonVisible) -and (-not $i.installed) -and $null -eq $i.processId -and (-not $i.chipVisible)) "status $($i.status), gumb $($i.homeButtonVisible), instaliran $($i.installed), proces $($i.processId)"
Check "0: prečac Ctrl+Shift+T (Ctrl+T ostaje za titlove)" (@($i.bodyShortcut) -contains "Ctrl+Shift+T") "$($i.bodyShortcut -join ', ')"
Check "1: kartica Postavke -> Torlink postoji i otvara se" ($t.tabIsTorlink) "kartica $($t.tabIsTorlink)"
$d = $t.afterDecline
Check "1: odbijeno upozorenje ne ukljucuje i ne instalira nista" ((-not $d.enabled) -and (-not $d.consent) -and (-not $d.installed)) "ukljuceno $($d.enabled), prihvat $($d.consent), instalirano $($d.installed)"
$a = $t.afterAccept
Check "1: prihvat -> preuzimanje, provjera i instalacija runtimea" ($a.enabled -and $a.consent -and $a.installed -like "1.11.1*" -and -not $a.message) "'$($a.installed)' za $($a.seconds) s, poruka '$($a.message)'"
Check "1: ukljuceno je spremljeno u settings.json, a proces jos ne radi" ($a.savedInFile -and $a.noProcessYet -and $a.switchOn -and $a.bodyEnabled) "datoteka $($a.savedInFile), bez procesa $($a.noProcessYet), stanje '$($a.state)'"
Check "1: provjera azuriranja kad nema novijeg" ($t.checkNoUpdate.text -match "a.uran|up to date" -and -not $t.checkNoUpdate.updateButton) "'$($t.checkNoUpdate.text)'"
Check "2: nakon ukljucivanja gumb 'Pretrazi online' je na Homeu" ($t.homeButtonAfterEnable) "vidljiv $($t.homeButtonAfterEnable)"
$p = $t.pageOpen
Check "2: pravi klik otvara Torlink stranicu (Home skriven) i pokrece proces" ($p.view -and $p.pageVisible -and -not $p.welcomeVisible -and $p.status -eq "Running" -and $p.pid) "stranica $($p.pageVisible), Home $($p.welcomeVisible), $($p.status), pid $($p.pid), Node $($p.nodeMb) MB"
$s = $t.search
Check "3: pretraga vraca rezultate i status po izvoru" ([int]$s.results -gt 0 -and @($s.chips).Count -ge 2) "$($s.results) rezultata, izvori: $($s.chips -join ' | ')"
$w = $t.downloading
Check "4: preuzimanje: red, naziv, postotak, brzina, ETA, gumbi" ([int]$w.rows -ge 1 -and $w.name -match "Sintel" -and [double]$w.bar -gt 0 -and $w.line -match "%" -and $w.line -match "~" -and $w.pauseButton -and $w.cancelButton) "'$($w.name)' | $($w.status) | $($w.line)"
$pz = $t.paused
Check "4: pauza zaustavlja napredak" ($pz.state -eq "Paused" -and [math]::Abs([double]$pz.percentAfter3s - [double]$pz.percent) -le 1 -and $pz.resumeButton) "stanje $($pz.state), $($pz.percent) % -> $($pz.percentAfter3s) %"
Check "4: nastavak" ($t.resumed) "preuzima se $($t.resumed)"
$b = $t.backHome
Check "5: Home gumb vraca na Home; preuzimanje se NE prekida; cip i proces" ((-not $b.view) -and (-not $b.pageVisible) -and $b.welcomeVisible -and $b.processAlive -and $t.continuesAtHome) "Home $($b.welcomeVisible), proces $($b.processAlive), nastavlja $($t.continuesAtHome), cip '$($b.chipText)'"
Check "5: cip otvara stranicu na preuzimanjima" ($t.chipOpensPage -eq $true -or $t.chipOpensPage -like "*finished*") "$($t.chipOpensPage)"
$c = $t.completed
Check "4: preuzimanje zavrseno: datoteke na disku, u Zavrsenima, Reproduciraj/Mapa" ($c.found -and [int]$c.files -gt 0 -and $c.dirExists -and $c.playButton -and $c.folderButton -and $c.removeButton) "datoteka $($c.files), '$($c.line)'"
Check "4: dijeljenje je ZADANO ISKLJUCENO" ($c.found -and -not $c.seeding) "seeding $($c.seeding); '$($c.line)'"
Check "4: duplikat zavrsenog se ne dodaje" ($t.duplicateMessage -match "preuzeto|downloaded") "'$($t.duplicateMessage)'"
$o = $t.offlineSearch
Check "7: bez interneta: poruka, stranica ostaje odzivna, popis preuzimanja netaknut" (($o.message -match "izvor|source") -and $o.pageStillResponsive -and [double]$o.seconds -lt 40) "$($o.seconds) s, '$($o.message)', rezultata $($o.results)"
Check "5: Esc vraca na Home" ($t.escBack) "$($t.escBack)"
$x = $t.disabledDuring
Check "6: iskljucivanje tijekom preuzimanja: proces ugasen, red i postavke ostaju" ((-not $x.enabled) -and $x.status -eq "Disabled" -and $x.processGone -and $x.queueKept -and $x.settingsKept) "stanje $($x.status), proces ugasen $($x.processGone), red $($x.queueKept), postavke $($x.settingsKept)"
Check "6: iskljuceno = nema gumba ni cipa" ($t.homeButtonHiddenWhenOff -and $t.chipHiddenWhenOff) "gumb skriven $($t.homeButtonHiddenWhenOff), cip skriven $($t.chipHiddenWhenOff)"
Check "6: ponovno ukljucivanje bez novog upozorenja; preuzimanje se nastavlja" ($t.reEnabled.enabled -and $t.resumedAfterReEnable) "ukljuceno $($t.reEnabled.enabled), nastavljeno $($t.resumedAfterReEnable)"
$cr = $t.crash
Check "6: pad procesa usred preuzimanja: ponovno pokretanje i nastavak" ($cr.restarted -and $cr.status -eq "Running" -and $cr.downloading -and [double]$cr.percentAfter -ge [double]$cr.percentBefore - 3) "novi proces $($cr.restarted), $($cr.percentBefore) % -> $($cr.percentAfter) %"
if ($t.updateAvailable) {
    Check "8: azuriranje: nova revizija se nudi s prikazom verzija" ($t.updateAvailable.button -and $t.updateAvailable.text -match "r2|1\.11\.1") "'$($t.updateAvailable.text)'"
    $u = $t.updateOk
    Check "8: azuriranje r2: instalirano, provjereno, backup, stanje netaknuto" ($u.installed -match "r2" -and $u.rollbackButton -and $u.stateKept) "'$($u.installed)', '$($u.text)'"
    $rb = $t.updateBrokenRollback
    Check "8: neispravan r3: automatsko vracanje na r2, Torlink radi" ($rb.installed -match "r2" -and $rb.stillWorks -and $rb.healthy -and $rb.text -match "vrac|restor|prethodn|previous") "'$($rb.installed)', '$($rb.text)'"
    Check "8: GitHub nedostupan: poruka, Torlink radi dalje" ($t.githubDown.text -match "GitHub" -and $t.githubDown.stillRunning) "'$($t.githubDown.text)'"
    Check "8: rucno vracanje na prethodnu verziju" ($t.manualRollback.installed -match "1\.11\.1 \(r1|r1") "'$($t.manualRollback.installed)'"
}
$rs = $t.resources
Check "R: resursi (Node, player) izmjereni" ([double]$rs.nodeMb -gt 0) "Node $($rs.nodeMb) MB, player $($rs.playerMb) MB"

# ---------- 2. zatvaranje playera: nijedan Torlink proces ne smije ostati ----------
Start-Sleep -Seconds 2
$left = @(Get-CimInstance Win32_Process -Filter "Name='node.exe'" | Where-Object { $_.ExecutablePath -like "$work*" })
Check "9: nakon zatvaranja playera nema Torlink procesa" ($left.Count -eq 0) "preostalo $($left.Count)"
Check "9: stanje preuzimanja je ostalo na disku" (Test-Path (Join-Path $data "torlink\state\data\queue.json")) "queue.json"

# ---------- 3. ponovno pokretanje: nedovrseno preuzimanje se nastavlja samo ----------
if (-not $ReuseResults) {
$env:JEBAITED_DATA_DIR = $data
try { $null = Invoke-Diag -Name "torlink_resume" -File $clip -Seconds 1 -ExtraArgs "--diag-ui-test torlinkresume" -TimeoutSeconds 240 }
finally { Remove-Item Env:JEBAITED_DATA_DIR -ErrorAction SilentlyContinue }
}
$rr = Read-Ui "torlink_resume"
Check "9: ponovno pokretanje playera: Torlink se sam dize i nastavlja preuzimanje" ($rr.enabled -and $rr.autoStarted -and $rr.resumedDownloading -and -not $rr.pageOpened) "proces $($rr.pid), $($rr.percent) %, stranica nije otvorena"
Check "9: uz preuzimanje se vidi cip i gumb na Homeu" ($rr.chipVisible -and $rr.homeButtonVisible) "cip $($rr.chipVisible), gumb $($rr.homeButtonVisible)"
Start-Sleep -Seconds 2
$left = @(Get-CimInstance Win32_Process -Filter "Name='node.exe'" | Where-Object { $_.ExecutablePath -like "$work*" })
Check "9: ni nakon drugog zatvaranja nema Torlink procesa" ($left.Count -eq 0) "preostalo $($left.Count)"

$results | Format-Table -AutoSize -Wrap | Out-String -Width 250
$bad = @($results | Where-Object { $_.Rezultat -ne "OK" }).Count
"Ukupno: $($results.Count), greske: $bad"
if ($bad -gt 0) { exit 1 }
