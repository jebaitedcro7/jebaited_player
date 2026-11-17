<#
.SYNOPSIS
  Testni titlovi za M2 (testdata\m2): encodinzi, formati, sinkronizacija i automatsko učitavanje istog naziva.
#>
param([string]$Root = (Join-Path $PSScriptRoot ".."))

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path $Root).Path
$dir = Join-Path $Root "testdata\m2"
$auto = Join-Path $dir "auto"
New-Item -ItemType Directory -Force -Path $dir, $auto | Out-Null
$video = Join-Path $Root "testdata\m1\1080p_h264_aac.mp4"

$utf8 = New-Object Text.UTF8Encoding $false
$utf8Bom = New-Object Text.UTF8Encoding $true
function Save([string]$Name, [string]$Text, [Text.Encoding]$Enc, [byte[]]$Prefix = @()) {
    $path = Join-Path $dir $Name
    [IO.File]::WriteAllBytes($path, [byte[]]($Prefix + $Enc.GetBytes($Text)))
}

$hrLine = "Čovječe, što ćeš? Đurđa žđ ŠŽĆČ"
$srt = "1`r`n00:00:01,000 --> 00:00:05,000`r`n$hrLine`r`n`r`n2`r`n00:00:06,000 --> 00:00:08,000`r`nŽELIM ČAŠU VODE, ĆAO ĐURO!`r`n"

# --- Encodinzi (isti tekst) ---
Save "enc_cp1250.srt" $srt ([Text.Encoding]::GetEncoding(1250))
Save "enc_iso88592.srt" $srt ([Text.Encoding]::GetEncoding(28592))
Save "enc_utf8bom.srt" $srt $utf8Bom ([byte[]](0xEF, 0xBB, 0xBF))
Save "enc_utf16le.srt" $srt ([Text.Encoding]::Unicode) ([byte[]](0xFF, 0xFE))

# --- Sinkronizacija: "Replika k" od 2k do 2k+1,5 s ---
$sb = New-Object Text.StringBuilder
for ($k = 1; $k -le 14; $k++) {
    $s = [TimeSpan]::FromSeconds(2 * $k); $e = [TimeSpan]::FromSeconds(2 * $k + 1.5)
    $cue = "$k`r`n{0:hh\:mm\:ss\,fff} --> {1:hh\:mm\:ss\,fff}`r`nReplika $k`r`n`r`n" -f $s, $e
    [void]$sb.Append($cue)
}
Save "sync.srt" $sb.ToString() $utf8

# --- Formati (tekst prikazan od 2 do 5 s) ---
Save "format_microdvd.sub" "{1}{1}25`r`n{50}{125}MicroDVD: čćžšđ`r`n" ([Text.Encoding]::GetEncoding(1250))
Save "format_ass.ass" ("[Script Info]`r`nScriptType: v4.00+`r`n`r`n[V4+ Styles]`r`nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding`r`n" +
    "Style: Default,Arial,48,&H00FFFFFF,&H000000FF,&H00000000,&H64000000,0,0,0,0,100,100,0,0,1,2,1,2,20,20,40,1`r`n`r`n[Events]`r`nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text`r`n" +
    "Dialogue: 0,0:00:02.00,0:00:05.00,Default,,0,0,0,,ASS: čćžšđ {\i1}kurziv{\i0}`r`n") ([Text.Encoding]::GetEncoding(1250))
Save "format_vtt.vtt" "WEBVTT`r`n`r`n00:02.000 --> 00:05.000`r`n<v Ana>VTT: čćžšđ</v>`r`n" $utf8
Save "format_ttml.ttml" ("<?xml version=`"1.0`" encoding=`"utf-8`"?>`r`n<tt xmlns=`"http://www.w3.org/ns/ttml`"><body><div>" +
    "<p begin=`"2s`" end=`"5s`">TTML: čćžšđ</p></div></body></tt>`r`n") $utf8
Save "format_subviewer.sub" "[INFORMATION]`r`n[TITLE]Test`r`n[END INFORMATION]`r`n[SUBTITLE]`r`n00:00:02.00,00:00:05.00`r`nSubViewer: čćžšđ`r`n" ([Text.Encoding]::GetEncoding(28592))

# --- Automatsko učitavanje: video + titlovi istog naziva ---
$autoVideo = Join-Path $auto "Test.Film.2026.1080p.mp4"
if (-not (Test-Path -LiteralPath $autoVideo)) { Copy-Item -LiteralPath $video -Destination $autoVideo }
[IO.File]::WriteAllBytes((Join-Path $auto "Test.Film.2026.1080p.en.srt"), $utf8.GetBytes("1`r`n00:00:01,000 --> 00:00:05,000`r`nEnglish line`r`n"))
[IO.File]::WriteAllBytes((Join-Path $auto "Test.Film.2026.1080p.hr.srt"), [Text.Encoding]::GetEncoding(1250).GetBytes($srt))

Get-ChildItem $dir -Recurse -File | Select-Object @{n = 'Datoteka'; e = { $_.FullName.Substring($dir.Length + 1) } }, Length
