# Testovi loggera sa stvarnim playerom (faza 4). Svaki scenarij ima vlastitu mapu podataka (JEBAITED_DATA_DIR),
# pa ne dira tvoje postavke, povijest ni logove. Rezultati: testdata\logger\<scenarij>\ i testdata\logger\<scenarij>.json
param(
    [string]$Root = (Resolve-Path "$PSScriptRoot\..").Path,
    [string[]]$Only = @()
)

$ErrorActionPreference = "Stop"
$exe = Join-Path $Root "src\JebaitedPlayer.App\bin\Debug\net10.0-windows\jebaited_player.exe"
$out = Join-Path $Root "testdata\logger"
$td = Join-Path $Root "testdata"
New-Item -ItemType Directory -Force $out | Out-Null

function Run($name, [string[]]$argList, [bool]$loggerOn = $true, [int]$killAfterSec = 0, [string]$reuseDir = "") {
    if ($Only.Count -gt 0 -and $Only -notcontains $name) { return }
    $dir = Join-Path $out $(if ($reuseDir) { $reuseDir } else { $name })
    if (-not $reuseDir -and (Test-Path $dir)) { Remove-Item -Recurse -Force $dir }
    New-Item -ItemType Directory -Force "$dir\logs" | Out-Null
    if (-not $loggerOn) { '{ "Enabled": false }' | Set-Content -Encoding utf8 "$dir\logs\logger_config.json" }
    $env:JEBAITED_DATA_DIR = $dir
    $all = @($argList) + @("--diag-out", "$out\$name.json")
    $quoted = ($all | ForEach-Object { if ($_ -match "\s") { "`"$_`"" } else { $_ } }) -join " "
    $p = Start-Process -FilePath $exe -ArgumentList $quoted -PassThru
    if ($killAfterSec -gt 0) {
        Start-Sleep -Seconds $killAfterSec
        Stop-Process -Id $p.Id -Force      # samo ovaj testni proces
        $p.WaitForExit()
        "$name : prekinut (kill) nakon $killAfterSec s, pid $($p.Id)"
    } else {
        if (-not $p.WaitForExit(120000)) { Stop-Process -Id $p.Id -Force; "$name : TIMEOUT" } else { "$name : exit $($p.ExitCode)" }
    }
    Remove-Item Env:\JEBAITED_DATA_DIR
}

$v1080 = "$td\env_1080p_h264.mp4"
$v4k = "$td\env_4k_hevc10.mkv"
$keys = "Space;Space;Right*3;Left;Up*3;Down*2;M;M;Space;Space"

Run "A_off_1080p"   @("--diag", "20", "--diag-keys", $keys, $v1080) $false
Run "A_on_1080p"    @("--diag", "20", "--diag-keys", $keys, "--diag-log-inject", $v1080) $true
Run "B_off_4k"      @("--diag", "20", $v4k) $false
Run "B_on_4k"       @("--diag", "20", $v4k) $true
Run "C_sub_cp1250"  @("--diag", "8", "$td\m1\1080p_h264_aac.mp4", "$td\m2\enc_cp1250.srt") $true
Run "C_broken_file" @("--diag", "6", "$td\m1\broken.mkv") $true
Run "D_crash_kill"  @("--diag", "60", $v1080) $true 9
Run "D_after_crash" @("--diag", "4", $v1080) $true 0 "D_crash_kill"
Run "E_ui_click"    @("--diag", "8", "--diag-ui-test", "timeline", $v1080) $true
Run "E_ui_home"     @("--diag", "8", "--diag-ui-test", "home", $v1080) $true
