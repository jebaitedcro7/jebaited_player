<#
.SYNOPSIS
  Pravi testne MKV datoteke s vise ugradenih titlova (razliciti jezici i zastavica "default"),
  za provjeru automatskog odabira jezika titlova.
#>
param(
    [string]$FFmpeg = "ffmpeg",
    [string]$Dir = (Join-Path $PSScriptRoot "..\testdata\m1")
)

$ErrorActionPreference = "Stop"
$Dir = (Resolve-Path $Dir).Path
$video = Join-Path $Dir "1080p_h264_aac.mp4"
$utf8 = New-Object Text.UTF8Encoding $false

function Write-Srt([string]$Path, [string]$Text) {
    $srt = "1`r`n00:00:00,500 --> 00:00:29,000`r`n$Text`r`n"
    [IO.File]::WriteAllText($Path, $srt, $utf8)
}

$zh = Join-Path $Dir "sub_zh.srt"; Write-Srt $zh (-join @([char]0x4F60, [char]0x597D, [char]0x4E16, [char]0x754C))
$en = Join-Path $Dir "sub_en.srt"; Write-Srt $en "English subtitle"
$hrText = "Hrvatski titl: " + (-join @([char]0x10D, [char]0x107, [char]0x17E, [char]0x161, [char]0x111, ' ', [char]0x10C, [char]0x106, [char]0x17D, [char]0x160, [char]0x110))
$hr = Join-Path $Dir "sub_hr.srt"; Write-Srt $hr $hrText

# 1) Kineski (zadani) + engleski + hrvatski -> ocekujemo hrvatski
& $FFmpeg -hide_banner -loglevel error -y -i $video -i $zh -i $en -i $hr -map 0 -map 1 -map 2 -map 3 -c copy -c:s srt `
    -metadata:s:s:0 language=chi -disposition:s:0 default `
    -metadata:s:s:1 language=eng -disposition:s:1 0 `
    -metadata:s:s:2 language=hrv -disposition:s:2 0 `
    (Join-Path $Dir "subs_zh-default_en_hr.mkv")

# 2) Kineski (zadani) + engleski -> ocekujemo engleski
& $FFmpeg -hide_banner -loglevel error -y -i $video -i $zh -i $en -map 0 -map 1 -map 2 -c copy -c:s srt `
    -metadata:s:s:0 language=chi -disposition:s:0 default `
    -metadata:s:s:1 language=eng -disposition:s:1 0 `
    (Join-Path $Dir "subs_zh-default_en.mkv")

# 3) Samo kineski (zadani) -> ocekujemo bez titlova
& $FFmpeg -hide_banner -loglevel error -y -i $video -i $zh -map 0 -map 1 -c copy -c:s srt `
    -metadata:s:s:0 language=chi -disposition:s:0 default `
    (Join-Path $Dir "subs_zh-only.mkv")

Get-ChildItem $Dir -Filter "subs_*.mkv" | Select-Object Name, @{n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
