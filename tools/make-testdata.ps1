<#
.SYNOPSIS
  Generira testne video/audio datoteke za jebaited_player u testdata\m1 (FFmpeg + NVENC).
  Sadržaj je sintetički (testsrc2 + ton 440 Hz), služi za provjeru formata i mjerenje resursa.
#>
param(
    [string]$FFmpeg = "ffmpeg",
    [string]$OutDir = (Join-Path $PSScriptRoot "..\testdata\m1")
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

function Make([string]$Name, [string[]]$FfArgs) {
    $out = Join-Path $OutDir $Name
    if (Test-Path -LiteralPath $out) { Write-Host "postoji: $Name"; return }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    & $FFmpeg -hide_banner -loglevel error -y @FfArgs $out
    if ($LASTEXITCODE -ne 0) { throw "FFmpeg nije uspio: $Name" }
    "{0,-46} {1,8:N1} MB {2,6:N1} s" -f $Name, ((Get-Item -LiteralPath $out).Length / 1MB), $sw.Elapsed.TotalSeconds
}

$v1080 = @("-f", "lavfi", "-i", "testsrc2=size=1920x1080:rate=24000/1001")
$v720  = @("-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=25")
$v4k   = @("-f", "lavfi", "-i", "testsrc2=size=3840x2160:rate=24000/1001")
$v8k   = @("-f", "lavfi", "-i", "testsrc2=size=7680x4320:rate=24")
$tone  = @("-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000")

# --- Glavni set za mjerenje (30 s) ---
Make "1080p_h264_aac.mp4"        ($v1080 + $tone + @("-t", "30", "-c:v", "h264_nvenc", "-pix_fmt", "yuv420p", "-b:v", "8M", "-c:a", "aac", "-b:a", "160k"))
Make "4k_hevc10_sdr_ac3-51.mkv"  ($v4k + $tone + @("-t", "30", "-c:v", "hevc_nvenc", "-profile:v", "main10", "-pix_fmt", "p010le", "-b:v", "25M", "-c:a", "ac3", "-ac", "6", "-b:a", "448k"))
Make "4k_hevc10_hdr10_eac3.mkv"  ($v4k + $tone + @("-t", "30", "-c:v", "hevc_nvenc", "-profile:v", "main10", "-pix_fmt", "p010le", "-b:v", "25M",
                                   "-color_primaries", "bt2020", "-color_trc", "smpte2084", "-colorspace", "bt2020nc", "-color_range", "tv",
                                   # NVENC ne upisuje sve oznake boja; hevc_metadata ih upisuje izravno u HEVC VUI.
                                   "-bsf:v", "hevc_metadata=colour_primaries=9:transfer_characteristics=16:matrix_coefficients=9",
                                   "-c:a", "eac3", "-ac", "6", "-b:a", "640k"))
Make "4k_av1_10bit_opus.mkv"     ($v4k + $tone + @("-t", "30", "-c:v", "av1_nvenc", "-pix_fmt", "p010le", "-b:v", "20M", "-c:a", "libopus", "-b:a", "160k"))
Make "8k_hevc_aac.mkv"           ($v8k + $tone + @("-t", "20", "-c:v", "hevc_nvenc", "-b:v", "60M", "-c:a", "aac", "-b:a", "160k"))

# --- Kompatibilnost formata (10 s, 720p) ---
Make "fmt_avi_mpeg4_mp3.avi"     ($v720 + $tone + @("-t", "10", "-c:v", "mpeg4", "-q:v", "4", "-c:a", "libmp3lame", "-b:a", "192k"))
Make "fmt_wmv_wmv2_wma.wmv"      ($v720 + $tone + @("-t", "10", "-c:v", "wmv2", "-b:v", "4M", "-c:a", "wmav2", "-b:a", "192k"))
Make "fmt_mov_h264_aac.mov"      ($v720 + $tone + @("-t", "10", "-c:v", "libx264", "-preset", "veryfast", "-c:a", "aac"))
Make "fmt_ts_mpeg2_mp2.ts"       ($v720 + $tone + @("-t", "10", "-c:v", "mpeg2video", "-b:v", "6M", "-c:a", "mp2", "-b:a", "192k"))
Make "fmt_mpg_mpeg2_mp2.mpg"     ($v720 + $tone + @("-t", "10", "-c:v", "mpeg2video", "-b:v", "6M", "-c:a", "mp2", "-b:a", "192k", "-f", "vob"))
Make "fmt_flv_h264_aac.flv"      ($v720 + $tone + @("-t", "10", "-c:v", "libx264", "-preset", "veryfast", "-c:a", "aac"))
Make "fmt_webm_vp9_opus.webm"    ($v720 + $tone + @("-t", "10", "-c:v", "libvpx-vp9", "-deadline", "realtime", "-cpu-used", "8", "-row-mt", "1", "-b:v", "3M", "-c:a", "libopus"))
Make "fmt_m4v_h264_aac.m4v"      ($v720 + $tone + @("-t", "10", "-c:v", "libx264", "-preset", "veryfast", "-c:a", "aac", "-f", "mp4"))

# --- Posebni slučajevi ---
Make "audio_only.flac"           ($tone + @("-t", "20", "-c:a", "flac"))
Make "audio_only.mp3"            ($tone + @("-t", "20", "-c:a", "libmp3lame", "-b:a", "192k"))

$hr = Join-Path $OutDir "Testni film – čćžšđ ČĆŽŠĐ.mp4"
if (-not (Test-Path -LiteralPath $hr)) {
    Copy-Item -LiteralPath (Join-Path $OutDir "1080p_h264_aac.mp4") -Destination $hr
    "kopija: Testni film – čćžšđ ČĆŽŠĐ.mp4"
}

$broken = Join-Path $OutDir "broken.mkv"
if (-not (Test-Path -LiteralPath $broken)) {
    $bytes = New-Object byte[] (1MB)
    (New-Object Random 42).NextBytes($bytes)
    [IO.File]::WriteAllBytes($broken, $bytes)
    "nasumični bajtovi: broken.mkv"
}
