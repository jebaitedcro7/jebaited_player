<#
.SYNOPSIS
  Testovi 2.1.12 u pravom playeru (nad testnim mapama podataka, nikad nad tvojom):
  - čuvar pokretanja: sigurno pokretanje nakon neuspjelog, prozori sakriveni nakon pokretanja, pad Show() kao u 2.1.7;
  - omot albuma (scenarij v2112: izgledi 2 i 4, Home);
  - glasnoća za Discord (scenarij v2112: razina poslana botu uz praćenje playera i bez njega).

.EXAMPLE
  .\tools\run-v2112-tests.ps1
  .\tools\run-v2112-tests.ps1 -Only startup
#>
param([ValidateSet("all", "startup", "scenario")] [string]$Only = "all")
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$ffmpeg = if ($env:FFMPEG) { $env:FFMPEG } else { "ffmpeg" }

$results = New-Object System.Collections.Generic.List[object]
function Check([string]$Name, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Test = $Name; Rezultat = $(if ($Ok) { "OK" } else { "GREŠKA" }); Detalji = $Detail })
}

Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WinProbe2112 {
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    public class Win { public IntPtr H; public string Title; public int L, T, R, B; public bool Visible; public bool Max; }
    public static List<Win> Of(uint pid) {
        var list = new List<Win>();
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
            RECT r; GetWindowRect(h, out r);
            list.Add(new Win { H = h, Title = sb.ToString(), L = r.L, T = r.T, R = r.R, B = r.B, Visible = IsWindowVisible(h), Max = IsZoomed(h) });
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
"@

$base = '"Language": "hr", "AutoUpdate": false, "OnlineWizardOffered": true'

function Get-OnScreen($app) {
    @([WinProbe2112]::Of([uint32]$app.Id) | Where-Object { $_.Visible -and $_.R -gt $_.L -and $_.L -gt -20000 -and $_.B -gt 0 })
}

function Get-LogText([string]$Dir) {
    $logs = Join-Path $Dir "logs"
    if (-not (Test-Path $logs)) { return "" }
    (Get-ChildItem $logs -Recurse -File | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8 }) -join "`n"
}

function Start-Player([string]$Name, [string]$SettingsJson, [string]$Fault, [switch]$LeaveMarker) {
    $dir = Join-Path $root "testdata\v2112\startup-$Name"
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $SettingsJson | Set-Content -LiteralPath (Join-Path $dir "settings.json") -Encoding UTF8
    if ($LeaveMarker) { '{"version":"2.1.11","startedUtc":"2026-11-22T00:00:00Z","pid":1}' | Set-Content -LiteralPath (Join-Path $dir "startup-pending.json") -Encoding UTF8 }
    $env:JEBAITED_DATA_DIR = $dir
    if ($Fault) { $env:JEBAITED_TEST_FAULT = $Fault }
    try {
        $app = Start-Process -FilePath $script:Exe -ArgumentList "--new-window" -PassThru
    } finally {
        Remove-Item Env:JEBAITED_DATA_DIR, Env:JEBAITED_TEST_FAULT -ErrorAction SilentlyContinue
    }
    [pscustomobject]@{ App = $app; Dir = $dir }
}

function Stop-Player($p) {
    # Najprije poruke (Information box), zatim glavni prozor; postavke se spremaju pri zatvaranju.
    foreach ($w in @([WinProbe2112]::Of([uint32]$p.App.Id) | Where-Object { $_.Visible -and $_.Title -like "*sigurno pokretanje*" })) {
        [void][WinProbe2112]::PostMessage($w.H, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
    }
    Start-Sleep -Milliseconds 400
    $all = @([WinProbe2112]::Of([uint32]$p.App.Id) | Where-Object { $_.Title -eq "jebaited_player mini" -or $_.Title -eq "jebaited_player" -or $_.Title -like "*– jebaited_player" })
    foreach ($w in $all) { [void][WinProbe2112]::PostMessage($w.H, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }
    if (-not $p.App.WaitForExit(15000)) { $p.App.Kill(); return $false }
    return $true
}

if ($Only -in "all", "startup") {
    if (-not (Test-Path $script:Exe)) { throw "Nema exe-a: $script:Exe (dotnet build -c Release)" }

    # 1) Obično pokretanje: oznaka nastane i nestane čim je prozor na zaslonu; nema poruke.
    $p = Start-Player "normal" "{ $base }"
    Start-Sleep -Seconds 5
    $wins = Get-OnScreen $p.App
    $marker = Test-Path (Join-Path $p.Dir "startup-pending.json")
    $box = @($wins | Where-Object { $_.Title -like "*sigurno pokretanje*" }).Count
    $closed = Stop-Player $p
    Check "1: obično pokretanje briše oznaku, bez poruke" (-not $marker -and $box -eq 0 -and @($wins | Where-Object { $_.Title -eq "jebaited_player" }).Count -eq 1 -and $closed) "oznaka ostala $marker, poruka $box, uredno zatvoren $closed"

    # 2) Ostatak oznake (prošlo pokretanje nije došlo do prozora) + maksimiziran + mini: sigurno pokretanje.
    $p = Start-Player "safe" "{ $base, `"WindowMaximized`": true, `"WindowLeft`": -32000, `"WindowTop`": -32000, `"WindowWidth`": 1000, `"WindowHeight`": 640, `"Volume`": 41, `"MiniPlayer`": { `"WasOpen`": true } }" -LeaveMarker
    Start-Sleep -Seconds 6
    $wins = Get-OnScreen $p.App
    $main = $wins | Where-Object { $_.Title -eq "jebaited_player" } | Select-Object -First 1
    $mini = @($wins | Where-Object { $_.Title -eq "jebaited_player mini" }).Count
    $box = @($wins | Where-Object { $_.Title -like "*sigurno pokretanje*" }).Count
    $marker = Test-Path (Join-Path $p.Dir "startup-pending.json")
    $log = Get-LogText $p.Dir
    $closed = Stop-Player $p
    $saved = Get-Content -LiteralPath (Join-Path $p.Dir "settings.json") -Raw -Encoding UTF8 | ConvertFrom-Json
    Check "1: sigurno pokretanje – normalan glavni prozor na zaslonu, bez mini playera" ($main -and -not $main.Max -and $main.L -gt -100 -and $mini -eq 0) "glavni $([bool]$main) (maks. $($main.Max), lijevo $($main.L)), mini $mini"
    Check "1: sigurno pokretanje – poruka korisniku i zapis u dnevniku" ($box -eq 1 -and $log -match "boot.safe_start") "poruka $box, dnevnik $($log -match 'boot.safe_start')"
    Check "1: sigurno pokretanje – oznaka obrisana, ostale postavke iste" (-not $marker -and [double]$saved.Volume -eq 41 -and $closed) "oznaka $marker, glasnoća $($saved.Volume), zatvoren $closed"

    # 3) Prozori sakriveni nakon pokretanja (simptom iz 2.1.7): čuvar vraća glavni prozor.
    $p = Start-Player "hidden" "{ $base }" -Fault "hide-windows"
    $sw = [Diagnostics.Stopwatch]::StartNew(); $sawNone = $false; $backAt = $null
    while ($sw.Elapsed.TotalSeconds -lt 15 -and -not $p.App.HasExited) {
        $main = @(Get-OnScreen $p.App | Where-Object { $_.Title -eq "jebaited_player" }).Count -gt 0
        if (-not $main -and $sw.Elapsed.TotalSeconds -gt 1) { $sawNone = $true }
        if ($main -and $sawNone) { $backAt = $sw.Elapsed.TotalSeconds; break }
        Start-Sleep -Milliseconds 200
    }
    $log = Get-LogText $p.Dir
    $closed = Stop-Player $p
    Check "1: bez prozora → čuvar vraća glavni prozor" ($sawNone -and $backAt -and $backAt -lt 12 -and $log -match "boot.no_window") "prozori nestali $sawNone, vraćen nakon $([math]::Round([double]$backAt,1)) s, dnevnik $($log -match 'boot.no_window'), zatvoren $closed"

    # 4) Show() pada kao u 2.1.7 (maksimiziran + ShowActivated=false): drugi pokušaj u sigurnom stanju.
    $p = Start-Player "showfails" "{ $base }" -Fault "show-fails"
    Start-Sleep -Seconds 5
    $main = Get-OnScreen $p.App | Where-Object { $_.Title -eq "jebaited_player" } | Select-Object -First 1
    $log = Get-LogText $p.Dir
    $closed = Stop-Player $p
    Check "1: pad Show() → prozor ipak na zaslonu" ($main -and $log -match "boot.show_failed" -and $closed) "glavni $([bool]$main), dnevnik $($log -match 'boot.show_failed'), zatvoren $closed"
}

if ($Only -in "all", "scenario") {
    # Testne pjesme: s ugrađenim omotom (MP3, FLAC, M4A), bez omota, i mapa sa slikom cover.jpg.
    $music = Join-Path $root "testdata\v2112\music"
    New-Item -ItemType Directory -Force -Path $music, (Join-Path $music "folder") | Out-Null
    $cover = Join-Path $music "cover-src.png"
    if (-not (Test-Path $cover)) {
        & $ffmpeg -hide_banner -loglevel error -y -f lavfi -i "color=c=0xC03030:s=300x300,drawbox=x=40:y=40:w=220:h=220:color=0x30C030:t=fill" -frames:v 1 $cover
    }
    $specs = @(
        @{ File = "cover.mp3"; Args = @("-i", $cover, "-map", "0:a", "-map", "1:v", "-c:a", "libmp3lame", "-b:a", "128k", "-c:v", "mjpeg", "-id3v2_version", "3", "-metadata:s:v", "title=Album cover", "-metadata:s:v", "comment=Cover (front)") },
        @{ File = "cover.flac"; Args = @("-i", $cover, "-map", "0:a", "-map", "1:v", "-c:a", "flac", "-c:v", "png", "-disposition:v", "attached_pic") },
        @{ File = "cover.m4a"; Args = @("-i", $cover, "-map", "0:a", "-map", "1:v", "-c:a", "aac", "-b:a", "128k", "-c:v", "mjpeg", "-disposition:v", "attached_pic") },
        @{ File = "plain.mp3"; Args = @("-c:a", "libmp3lame", "-b:a", "128k") },
        @{ File = "folder\song.mp3"; Args = @("-c:a", "libmp3lame", "-b:a", "128k") })
    foreach ($s in $specs) {
        $out = Join-Path $music $s.File
        if (-not (Test-Path $out)) {
            # Trajanje je u samom izvoru (sine=…:duration=30): -t ispred drugog ulaza (slike) vrijedio bi za sliku, a ton bi bio beskonačan.
            $ffArgs = @("-hide_banner", "-loglevel", "error", "-nostdin", "-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=30") + $s.Args +
                    @("-ac", "2", "-metadata", "artist=jebaited_cro", "-metadata", "title=Omot test", $out)
            & $ffmpeg @ffArgs
            if ($LASTEXITCODE -ne 0) { throw "FFmpeg nije uspio ($($s.File))" }
        }
    }
    Copy-Item $cover (Join-Path $music "folder\cover.png") -Force

    $data = Join-Path $root "testdata\v2112\data"
    if (Test-Path $data) { Remove-Item $data -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $data | Out-Null
    "{ $base }" | Set-Content -LiteralPath (Join-Path $data "settings.json") -Encoding UTF8
    $db = Join-Path $script:DiagDir "v2112.db"
    Remove-Item "$db*" -ErrorAction SilentlyContinue
    $env:JEBAITED_DATA_DIR = $data
    $env:JEBAITED_V2112_MUSIC = $music
    try {
        $summary = Invoke-Diag -Name "v2112" -File (Join-Path $music "cover.mp3") -Seconds 1 -ExtraArgs "--diag-ui-test v2112 --diag-db `"$db`"" -TimeoutSeconds 300
    } finally {
        Remove-Item Env:JEBAITED_DATA_DIR, Env:JEBAITED_V2112_MUSIC -ErrorAction SilentlyContinue
    }
    $u = (Get-Content -LiteralPath (Join-Path $script:DiagDir "v2112.json") -Raw -Encoding UTF8 | ConvertFrom-Json).uiTest
    if (-not $u -or $u.exception -or $u.error) { throw "v2112: $($u.exception)$($u.error) ($($summary.Error))" }

    foreach ($c in $u.covers) {
        Check "2: glazba s omotom ostaje glazba – $($c.file)" ($c.audioOnly -and $c.discordDecision -eq "Send" -and $c.homeKeptPlaying) "samo zvuk $($c.audioOnly), Discord $($c.discordDecision), Home svira dalje $($c.homeKeptPlaying), mpv albumart '$($c.mpvAlbumArt)'"
        $want = [bool]$c.expected
        Check "2: omot – $($c.file)" ($c.hasCover -eq $want -and ($want -eq $false -or ([int]$c.pixelWidth -gt 0 -and $c.cardShowsCover -and $c.sideShowsCover -and $c.homeShowsCover))) "omot $($c.hasCover) (očekivano $want), $($c.pixelWidth) px, kartica $($c.cardShowsCover), bočna $($c.sideShowsCover), Home $($c.homeShowsCover)"
    }
    Check "2: radio i pjesma bez omota → NLO logo" ($u.logoForPlain -and -not $u.homeCoverForPlain) "logo $($u.logoForPlain), Home sličica $($u.homeCoverForPlain)"

    $d = $u.discordVolume
    if ($d.skipped) {
        Check "5: glasnoća za Discord" $false "preskočeno: $($d.skipped)"
    } else {
        Check "5: uz praćenje playera razina pada s glasnoćom playera" ([double]$d.followDrop -gt 12) "player 20 → 10: $([math]::Round([double]$d.followDrop,1)) dB (mpv krivulja ≈ 18 dB)"
        Check "5: bez praćenja razina na Discordu ostaje ista" ([math]::Abs([double]$d.independentDrop50) -lt 1.5 -and [math]::Abs([double]$d.independentDrop25) -lt 2) "player 20 → 10: $([math]::Round([double]$d.independentDrop50,2)) dB, 20 → 7: $([math]::Round([double]$d.independentDrop25,2)) dB"
        Check "5: Discord glasnoća mijenja poslanu razinu" ([double]$d.botVolumeDrop -gt 12) "Discord 20 → 10: $([math]::Round([double]$d.botVolumeDrop,1)) dB"
        Check "5: format hvatanja" ($d.captureFormat -in @("float32", "pcm16")) "$($d.captureFormat)"
    }
}

$results | Format-Table -AutoSize -Wrap
$failed = @($results | Where-Object { $_.Rezultat -ne "OK" }).Count
Write-Host ("Ukupno {0}, uspješno {1}, grešaka {2}" -f $results.Count, ($results.Count - $failed), $failed)
if ($failed -gt 0) { exit 1 }
