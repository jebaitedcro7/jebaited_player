<#
.SYNOPSIS
  Integracijski testovi titlova u pravom playeru (M2): encodinzi, formati, automatsko učitavanje i sinkronizacija.
  Za svaki slučaj player zaustavi video na zadanoj sekundi i pročita prikazani titl.
#>
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "run-diag.ps1")
$root = $script:Root
$m2 = Join-Path $root "testdata\m2"
$video = Join-Path $root "testdata\m1\1080p_h264_aac.mp4"
$autoVideo = Join-Path $m2 "auto\Test.Film.2026.1080p.mp4"
$hr = "Čovječe, što ćeš? Đurđa žđ ŠŽĆČ"

$cases = @(
    @{ Name = "Auto: isti naziv (hr cp1250 + en)"; Files = @($autoVideo); Probe = 2; Expect = $hr; Encoding = "Windows-1250" },
    @{ Name = "Encoding Windows-1250";  Files = @($video, "$m2\enc_cp1250.srt");   Probe = 2; Expect = $hr; Encoding = "Windows-1250" },
    @{ Name = "Encoding ISO-8859-2";    Files = @($video, "$m2\enc_iso88592.srt"); Probe = 2; Expect = $hr; Encoding = "ISO-8859-2" },
    @{ Name = "Encoding UTF-8 BOM";     Files = @($video, "$m2\enc_utf8bom.srt");  Probe = 2; Expect = $hr; Encoding = "UTF-8" },
    @{ Name = "Encoding UTF-16 LE";     Files = @($video, "$m2\enc_utf16le.srt");  Probe = 2; Expect = $hr; Encoding = "UTF-16" },
    @{ Name = "Rucni encoding (krivi 1252)"; Files = @($video, "$m2\enc_cp1250.srt"); Probe = 2; Extra = "--diag-encoding 1252"; Expect = "Èovjeèe, što æeš? Ðurða žð ŠŽÆÈ"; Encoding = "ručno" },
    @{ Name = "Format MicroDVD (25 fps)"; Files = @($video, "$m2\format_microdvd.sub");  Probe = 3; Expect = "MicroDVD: čćžšđ" },
    @{ Name = "Format ASS";             Files = @($video, "$m2\format_ass.ass");       Probe = 3; Expect = "ASS: čćžšđ kurziv" },
    @{ Name = "Format WebVTT";          Files = @($video, "$m2\format_vtt.vtt");       Probe = 3; Expect = "VTT: čćžšđ" },
    @{ Name = "Format TTML";            Files = @($video, "$m2\format_ttml.ttml");     Probe = 3; Expect = "TTML: čćžšđ" },
    @{ Name = "Format SubViewer";       Files = @($video, "$m2\format_subviewer.sub"); Probe = 3; Expect = "SubViewer: čćžšđ" },
    @{ Name = "Sync bez korekcije";     Files = @($video, "$m2\sync.srt"); Probe = 6.5;  Expect = "Replika 3" },
    @{ Name = "Sync pomak -2000 ms";    Files = @($video, "$m2\sync.srt"); Probe = 6.5;  Extra = "--diag-sub-delay -2000"; Expect = "Replika 4" },
    @{ Name = "Sync brzina 1,25";       Files = @($video, "$m2\sync.srt"); Probe = 10.2; Extra = "--diag-sub-speed 1.25"; Expect = "Replika 4" },
    @{ Name = "Sync dvije tocke";       Files = @($video, "$m2\sync.srt"); Probe = 16;   Extra = "--diag-sync-points 4000:5000;24000:26000"; Expect = "Replika 7" },
    @{ Name = "Spremanje (-2000 ms)";   Files = @($video, "$m2\sync.srt"); Probe = 6.5;  Extra = "--diag-sub-delay -2000 --diag-save `"$m2\saved\sync.hr.sync.srt`""; Expect = "Replika 4"; SavedCheck = "$m2\saved\sync.hr.sync.srt" },
    @{ Name = "Ugrađeni: zh(zadani)+en+hr"; Files = @("$root\testdata\m1\subs_zh-default_en_hr.mkv"); Probe = 2; Expect = "Hrvatski titl: čćžšđ ČĆŽŠĐ" },
    @{ Name = "Ugrađeni: zh(zadani)+en";    Files = @("$root\testdata\m1\subs_zh-default_en.mkv");    Probe = 2; Expect = "English subtitle" },
    @{ Name = "Ugrađeni: samo zh";          Files = @("$root\testdata\m1\subs_zh-only.mkv");          Probe = 2; Expect = "" }
)
New-Item -ItemType Directory -Force -Path "$m2\saved" | Out-Null
Remove-Item "$m2\saved\*" -ErrorAction SilentlyContinue

$rows = foreach ($c in $cases) {
    $extra = "--diag-probe $($c.Probe)"
    if ($c.Extra) { $extra += " $($c.Extra)" }
    $null = Invoke-Diag -Name ("SUB " + $c.Name) -File $c.Files -Seconds 1 -ExtraArgs $extra -TimeoutSeconds 60
    $json = Join-Path $script:DiagDir ((("SUB " + $c.Name) -replace '[^\w\-]+', '_') + ".json")
    $p = $null
    if (Test-Path -LiteralPath $json) { $p = (Get-Content -LiteralPath $json -Raw -Encoding UTF8 | ConvertFrom-Json).subtitleProbe }
    $text = if ($p) { ([string]$p.'sub-text' -replace "`r?`n", " ").Trim() } else { $null }
    $ok = $p -and $text -eq $c.Expect
    if ($ok -and $c.Encoding) { $ok = [bool]($p.selectedEncoding -like "*$($c.Encoding)*") }
    if ($ok -and $c.SavedCheck) {
        # Spremljena kopija mora imati sve titlove pomaknute za -2 s (Replika 1: 2 s -> 0 s), u UTF-8 s BOM-om.
        $bytes = [IO.File]::ReadAllBytes($c.SavedCheck)
        $saved = [Text.Encoding]::UTF8.GetString($bytes)
        $ok = ($bytes[0] -eq 0xEF) -and ($saved -match "00:00:00,000 --> 00:00:01,500\r\nReplika 1") -and ($saved -match "00:00:26,000 --> 00:00:27,500\r\nReplika 14")
        $text += " | spremljeno: " + $(if ($ok) { "pomak točan" } else { "NETOČNO" })
    }
    if (-not $ok -and (Test-Path -LiteralPath $json)) {
        # Sačuvaj sve podatke o grešci (sljedeći krug bi ih prebrisao).
        $failDir = Join-Path $script:DiagDir "failures"
        New-Item -ItemType Directory -Force -Path $failDir | Out-Null
        Copy-Item -LiteralPath $json -Destination (Join-Path $failDir ("{0:yyyyMMdd-HHmmss}_{1}" -f (Get-Date), (Split-Path $json -Leaf)))
        Copy-Item -LiteralPath "$env:LOCALAPPDATA\jebaited_player\logs\app.log" -Destination (Join-Path $failDir ("{0:yyyyMMdd-HHmmss}_app.log" -f (Get-Date))) -ErrorAction SilentlyContinue
    }
    [pscustomobject]@{
        Rezultat = if ($ok) { "OK" } else { "GREŠKA" }
        Test     = $c.Name
        Prikazano = $text
        Encoding = if ($p) { $p.selectedEncoding } else { $null }
        Jezik    = if ($p) { $p.selectedLanguage } else { $null }
        Pomak    = if ($p) { $p.'sub-delay' } else { $null }
        Brzina   = if ($p) { $p.'sub-speed' } else { $null }
    }
}
$rows
"{0} / {1} prošlo" -f @($rows | Where-Object Rezultat -eq "OK").Count, $rows.Count
