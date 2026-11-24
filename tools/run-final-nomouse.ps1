<#
.SYNOPSIS
  Završni test cijelog playera BEZ diranja miša i samo na monitoru 1440x2560 (okomiti).
  - JEBAITED_DIAG_NO_MOUSE=1: testovi ne miču kursor ni ne "klikaju" (korisnik radi na drugom monitoru).
  - JEBAITED_DIAG_MONITOR: prozor playera se otvara i maksimizira na tom monitoru.
  Izostavljeni su testovi koji NUŽNO traže pravi miš: ui217 (hover/kotačić), upgrade3 (klikovi na traku vremena), torlink
  (pravi klik); oni se pokreću zasebno kad korisnik nije za računalom.
  Rezultat: testdata\final-test-nomouse.log
.EXAMPLE
  .\tools\run-final-nomouse.ps1
#>
$ErrorActionPreference = "Continue"
Set-Location (Split-Path $PSScriptRoot -Parent)
Add-Type -AssemblyName System.Windows.Forms
$screen = [System.Windows.Forms.Screen]::AllScreens | Where-Object { $_.Bounds.Width -eq 1440 -and $_.Bounds.Height -eq 2560 } | Select-Object -First 1
if (-not $screen) { throw "Nema monitora 1440x2560." }
$b = $screen.Bounds
$env:JEBAITED_DIAG_MONITOR = "$($b.X),$($b.Y),$($b.Width),$($b.Height)"
$env:JEBAITED_DIAG_NO_MOUSE = "1"

$log = "testdata\final-test-nomouse.log"
"" | Set-Content $log
function Log($t) { $t | Add-Content $log -Encoding UTF8 }
function Run($name, $cmd) {
    Log "=== $name  ($(Get-Date -Format HH:mm:ss))"
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -Command $cmd 2>&1 | Out-String -Width 300
    $lines = $out -split "`r?`n" | Where-Object { $_ -match 'Ukupno|UPGRADE 3:|GRE|Passed!|Failed!|error|Error\(s\)|Warning\(s\)|Provjera:|Paket:|SHA|uspje|OK$|"(connected|playing|recovered|skipped|real[A-Za-z]*Skipped|fullscreenOn|homeStopVisible|iptvPanelClosedAfterStop|seekBarFull|searchOpens[A-Za-z]*|widerPanelWidth|panelWidthRestored|categoryWidthRestored|expandedWidth|collapsedAgain|labelsHiddenAgain|rapidEndsCollapsed|settingsHasNoPlainPassword|logsHaveNo[A-Za-z]*)"' } | Select-Object -Last 16
    $lines | ForEach-Object { Log $_.Substring(0, [Math]::Min(260, $_.Length)) }
}
Log "Monitor: $env:JEBAITED_DIAG_MONITOR, bez miša"
Run "ciste izgradnja (Release, cijelo rjesenje)" "dotnet build jebaited_player.slnx -c Release 2>&1"
Run "unit testovi (Release)" "dotnet test jebaited_player.slnx -c Release --no-build 2>&1"
foreach ($t in 'iptvresize', 'homerail', 'iptv', 'iptvdrag', 'ciklus1', 'ciklus2', 'ciklus3', 'ciklus4', 'ciklus5', 'ciklus6', 'v2', 'v2112', 'discord', 'news', 'library') {
    Run $t ".\tools\run-$t-tests.ps1 2>&1"
}
Run "test nadogradnje (Velopack)" ".\tools\run-update-test.ps1 2>&1"
Run "Store paket bez Torlinka" ".\tools\make-msix.ps1 2>&1"
Log "=== KRAJ ($(Get-Date -Format HH:mm:ss))"
