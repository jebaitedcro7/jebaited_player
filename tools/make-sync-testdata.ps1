<#
.SYNOPSIS
  Testni film za automatsku sinkronizaciju (M5): sintetski govor (Windows TTS, glasovi David i Zira),
  glazba i šum (FFmpeg), te titlovi s točno poznatim pomakom, brzinom, krivim filmom i drugom montažom.

  Izlaz (testdata\m5):
    sync_film.mp4      H.264 + AAC stereo (govor + glazba + šum)
    sync_film_51.mkv   isti video + AC3 5.1, govor samo u središnjem kanalu
    sync_*.srt         varijante titlova; expected.json = što se očekuje za svaku
#>
param(
    [string]$FFmpeg = "ffmpeg",
    [string]$OutDir = (Join-Path $PSScriptRoot "..\testdata\m5"),
    [double]$Minutes = 25,
    [int]$Seed = 7
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$work = Join-Path $OutDir "work"
New-Item -ItemType Directory -Force -Path $work | Out-Null

Add-Type -AssemblyName System.Speech
if (-not ("SyncAudio" -as [type])) {
Add-Type -TypeDefinition @"
using System;
using System.IO;
public static class SyncAudio
{
    // Uklanja tišinu s početka i kraja TTS isječka (prag 1,5 % punog raspona).
    public static short[] Trim(byte[] pcm)
    {
        int n = pcm.Length / 2;
        short[] s = new short[n];
        Buffer.BlockCopy(pcm, 0, s, 0, n * 2);
        int first = 0, last = n - 1;
        while (first < n && Math.Abs((int)s[first]) < 500) first++;
        while (last > first && Math.Abs((int)s[last]) < 500) last--;
        if (last <= first) return new short[0];
        short[] r = new short[last - first + 1];
        Array.Copy(s, first, r, 0, r.Length);
        return r;
    }

    public static void Place(short[] buffer, short[] clip, int at)
    {
        for (int i = 0; i < clip.Length && at + i < buffer.Length; i++)
        {
            int v = buffer[at + i] + clip[i];
            buffer[at + i] = (short)Math.Max(-32768, Math.Min(32767, v));
        }
    }

    public static void WriteWav(string path, short[] samples, int rate)
    {
        using (var w = new BinaryWriter(File.Create(path)))
        {
            int bytes = samples.Length * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes);
            w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16);
            w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
            byte[] raw = new byte[bytes];
            Buffer.BlockCopy(samples, 0, raw, 0, bytes);
            w.Write(raw);
        }
    }
}
"@
}

$sentences = @(
    "Where were you last night?", "I told you, I was at the office until late.", "That is not what your sister said.",
    "Listen to me. We do not have much time.", "The train leaves at nine.", "Did you bring the keys?",
    "No. I thought you had them.", "Then we have a problem.", "Wait. Somebody is coming.", "Stay behind me and do not say a word.",
    "Good evening, officers. Can I help you?", "We are looking for a man in a grey coat.", "Nobody like that has been here all day.",
    "Are you sure about that?", "Absolutely. I would remember.", "Alright. If you see him, call this number.",
    "That was close.", "Too close. We need to move tonight.", "What about the money?", "Forget the money. Think about your family.",
    "I have been thinking about nothing else for three years.", "Then you understand why I am asking.", "Give me one good reason to trust you.",
    "Because I am the only one who came back for you.", "Mother always said you were the clever one.", "She also said you were the brave one.",
    "Look at the map. The bridge is here, and the road ends there.", "So we cross the river on foot?", "Only if the ice holds.",
    "And if it does not?", "Then we swim.", "You are joking.", "I never joke about rivers.", "Fine. Let us go before the sun comes up.",
    "Hello? Is anybody home?", "In the kitchen! Come in.", "Something smells wonderful.", "It is grandmother's recipe. Sit down.",
    "How was the trip?", "Long. The car broke down twice.", "You should have taken the bus.", "I know that now, believe me.",
    "Tell me about the new job.", "It is fine. The people are nice. The coffee is terrible.", "Some things never change.",
    "Do you remember the summer at the lake?", "How could I forget? You fell out of the boat.", "I was pushed!",
    "That is not how I remember it.", "We should go back there one day.", "Maybe next year, when things calm down.",
    "Doctor, what is the result?", "The tests look good. Better than last time.", "So I can go home?", "Tomorrow morning, if you rest tonight.",
    "Thank you. Really, thank you for everything.", "Just doing my job. Get some sleep.", "Captain, the engine is losing pressure.",
    "How long do we have?", "Twenty minutes, maybe less.", "Then get everyone to the upper deck. Now!", "Yes, sir.",
    "I am sorry. I did not mean any of it.", "I know. I am sorry too.", "Can we start again?", "We can try."
)

# --- 1) TTS isječci (oba glasa, različite brzine govora) ---------------------------------------------------
$sw = [Diagnostics.Stopwatch]::StartNew()
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$format = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
$voices = @("Microsoft David Desktop", "Microsoft Zira Desktop")
$rng = New-Object System.Random $Seed
$clips = New-Object System.Collections.Generic.List[object]
foreach ($text in $sentences) {
    foreach ($voice in $voices) {
        $synth.SelectVoice($voice)
        $synth.Rate = $rng.Next(-2, 3)
        $ms = New-Object System.IO.MemoryStream
        $synth.SetOutputToAudioStream($ms, $format)
        $synth.Speak($text)
        $synth.SetOutputToNull()
        $samples = [SyncAudio]::Trim($ms.ToArray())
        if ($samples.Length -gt 1600) { $clips.Add([pscustomobject]@{ Text = $text; Samples = $samples }) }
    }
}
$synth.Dispose()
"TTS: {0} isječaka za {1:N1} s" -f $clips.Count, $sw.Elapsed.TotalSeconds

# --- 2) Raspored govora ------------------------------------------------------------------------------------
function New-Timeline([int]$seed, [double]$durationSec) {
    $r = New-Object System.Random $seed
    $items = New-Object System.Collections.Generic.List[object]
    $t = 6.0 + $r.NextDouble() * 10
    while ($true) {
        $clip = $clips[$r.Next($clips.Count)]
        $len = $clip.Samples.Length / 16000.0
        if ($t + $len -gt $durationSec - 8) { break }
        $items.Add([pscustomobject]@{ Start = $t; End = $t + $len; Clip = $clip })
        $p = $r.NextDouble()
        $gap = if ($p -lt 0.72) { 0.3 + $r.NextDouble() * 2.2 } elseif ($p -lt 0.95) { 2.5 + $r.NextDouble() * 6 } else { 15 + $r.NextDouble() * 25 }
        $t += $len + $gap
    }
    return $items
}

function New-Cues($timeline, [int]$seed) {
    # Kao pravi titlovi: početak oko početka govora, kraj 0,2–0,6 s nakon govora, najmanje 1 s,
    # ponekad dvije kratke replike u istom titlu.
    $r = New-Object System.Random $seed
    $cues = New-Object System.Collections.Generic.List[object]
    $i = 0
    while ($i -lt $timeline.Count) {
        $a = $timeline[$i]
        $text = $a.Clip.Text
        $endSpeech = $a.End
        if ($i + 1 -lt $timeline.Count -and ($timeline[$i + 1].Start - $a.End) -lt 0.8 -and $r.NextDouble() -lt 0.15) {
            $b = $timeline[$i + 1]
            $text = "- " + $a.Clip.Text + "`r`n- " + $b.Clip.Text
            $endSpeech = $b.End
            $i++
        }
        $start = $a.Start + ($r.NextDouble() * 0.17 - 0.12)
        $end = [Math]::Max($endSpeech + 0.2 + $r.NextDouble() * 0.4, $start + 1.0)
        $cues.Add([pscustomobject]@{ Start = [Math]::Max(0, $start); End = $end; Text = $text })
        $i++
    }
    for ($k = 0; $k -lt $cues.Count - 1; $k++) {
        if ($cues[$k].End -gt $cues[$k + 1].Start - 0.08) { $cues[$k].End = $cues[$k + 1].Start - 0.08 }
    }
    return $cues
}

function Format-Time([double]$s) {
    if ($s -lt 0) { $s = 0 }
    $ts = [TimeSpan]::FromMilliseconds([Math]::Round($s * 1000))
    return "{0:00}:{1:00}:{2:00},{3:000}" -f [int][Math]::Floor($ts.TotalHours), $ts.Minutes, $ts.Seconds, $ts.Milliseconds
}

# Datoteka titla u kojoj je ispravno vrijeme t = izvorno × speed + offset (dakle izvorno = (t − offset) / speed).
function Write-Srt([string]$Name, $cues, [double]$Speed = 1, [double]$Offset = 0, [scriptblock]$Shift = $null) {
    $sb = New-Object System.Text.StringBuilder
    $n = 0
    foreach ($c in $cues) {
        $s = $c.Start; $e = $c.End
        if ($Shift) { $d = & $Shift $c; $s += $d; $e += $d }
        $s = ($s - $Offset) / $Speed; $e = ($e - $Offset) / $Speed
        if ($s -lt 0) { continue }
        $n++
        [void]$sb.Append("$n`r`n$(Format-Time $s) --> $(Format-Time $e)`r`n$($c.Text)`r`n`r`n")
    }
    [IO.File]::WriteAllText((Join-Path $OutDir $Name), $sb.ToString(), (New-Object Text.UTF8Encoding $false))
}

$duration = $Minutes * 60
$timeline = New-Timeline $Seed $duration
$cues = New-Cues $timeline ($Seed + 1)
$speechSec = ($timeline | ForEach-Object { $_.End - $_.Start } | Measure-Object -Sum).Sum
"Govor: {0} rečenica, {1:N0} s od {2:N0} s ({3:P0}); titlova: {4}" -f $timeline.Count, $speechSec, $duration, ($speechSec / $duration), $cues.Count

# --- 3) Zvuk govora -----------------------------------------------------------------------------------------
$buffer = New-Object 'System.Int16[]' ([int]($duration * 16000))
foreach ($it in $timeline) { [SyncAudio]::Place($buffer, $it.Clip.Samples, [int]($it.Start * 16000)) }
$speechWav = Join-Path $work "speech.wav"
[SyncAudio]::WriteWav($speechWav, $buffer, 16000)

# Glazba: 8 dijelova po 25–80 s (i ispod dijaloga i u "akcijskim" stankama).
$mr = New-Object System.Random ($Seed + 2)
$music = @()
for ($m = 0; $m -lt 8; $m++) { $ms0 = [Math]::Round($mr.NextDouble() * ($duration - 90), 1); $music += "between(t,$ms0,$($ms0 + 25 + [Math]::Round($mr.NextDouble() * 55, 1)))" }
$musicOn = ($music -join "+")
$chord = "0.045*(sin(2*PI*196*(1+0.26*gte(mod(t,8),4))*t)+sin(2*PI*246.9*(1+0.26*gte(mod(t,8),4))*t)+sin(2*PI*293.7*(1+0.26*gte(mod(t,8),4))*t)+0.6*sin(2*PI*98*t))*(0.6+0.4*sin(2*PI*2*t))"

$mp4 = Join-Path $OutDir "sync_film.mp4"
$mkv = Join-Path $OutDir "sync_film_51.mkv"
$sw.Restart()
& $FFmpeg -hide_banner -loglevel error -y `
    -i $speechWav `
    -f lavfi -i "anoisesrc=color=pink:amplitude=0.025:sample_rate=48000:duration=$duration" `
    -f lavfi -i "aevalsrc=exprs='$chord':s=48000:d=$duration" `
    -f lavfi -i "testsrc2=size=640x360:rate=24000/1001:duration=$duration" `
    -filter_complex "[0:a]aresample=48000[sp];[2:a]volume=0:enable='not($musicOn)'[mu];[sp][1:a][mu]amix=inputs=3:normalize=0,alimiter=limit=0.95[mix]" `
    -map 3:v -map "[mix]" -c:v libx264 -preset ultrafast -crf 34 -pix_fmt yuv420p -c:a aac -b:a 128k -ac 2 -shortest $mp4
if ($LASTEXITCODE -ne 0) { throw "FFmpeg (mp4) nije uspio" }
"sync_film.mp4: {0:N1} MB, {1:N1} s" -f ((Get-Item $mp4).Length / 1MB), $sw.Elapsed.TotalSeconds

$sw.Restart()
& $FFmpeg -hide_banner -loglevel error -y `
    -i $mp4 -i $speechWav `
    -f lavfi -i "anoisesrc=color=pink:amplitude=0.025:sample_rate=48000:duration=$duration" `
    -f lavfi -i "aevalsrc=exprs='$chord':s=48000:d=$duration" `
    -filter_complex "[1:a]aresample=48000,aformat=sample_fmts=fltp:channel_layouts=mono[sp];[3:a]volume=0:enable='not($musicOn)'[mu];[2:a][mu]amix=inputs=2:normalize=0,aformat=sample_fmts=fltp:channel_layouts=mono[bg];[sp][bg]amerge=inputs=2,pan=5.1|FL=c1|FR=c1|FC=c0|LFE=0.3*c1|BL=0.6*c1|BR=0.6*c1[a51]" `
    -map 0:v -map "[a51]" -c:v copy -c:a ac3 -b:a 448k -shortest $mkv
if ($LASTEXITCODE -ne 0) { throw "FFmpeg (mkv) nije uspio" }
"sync_film_51.mkv: {0:N1} MB, {1:N1} s" -f ((Get-Item $mkv).Length / 1MB), $sw.Elapsed.TotalSeconds

# --- 4) Titlovi -----------------------------------------------------------------------------------------------
$ntsc = 24000.0 / 1001.0
$half = $duration / 2
$wrongCues = New-Cues (New-Timeline ($Seed + 50) $duration) ($Seed + 51)
Write-Srt "sync_ok.srt" $cues
Write-Srt "sync_kasni_2500.srt" $cues -Offset (-2.5)
Write-Srt "sync_rani_7300.srt" $cues -Offset 7.3
Write-Srt "sync_25fps.srt" $cues -Speed (25 / $ntsc) -Offset 1.2
Write-Srt "sync_drift.srt" $cues -Speed 1.0008 -Offset (-0.8)
Write-Srt "sync_krivi_film.srt" $wrongCues
Write-Srt "sync_druga_montaza.srt" $cues -Shift { param($c) if ($c.Start -gt $half) { 20 } else { 0 } }

$expected = [ordered]@{
    "sync_ok.srt"            = @{ verdict = "InSync"; speed = 1; offset = 0 }
    "sync_kasni_2500.srt"    = @{ verdict = "Corrected"; speed = 1; offset = -2.5 }
    "sync_rani_7300.srt"     = @{ verdict = "Corrected"; speed = 1; offset = 7.3 }
    "sync_25fps.srt"         = @{ verdict = "Corrected"; speed = 25 / $ntsc; offset = 1.2 }
    "sync_drift.srt"         = @{ verdict = "Corrected"; speed = 1.0008; offset = -0.8 }
    "sync_krivi_film.srt"    = @{ verdict = "Mismatch" }
    "sync_druga_montaza.srt" = @{ verdict = "CorrectedInParts" }
}
$expected | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutDir "expected.json") -Encoding UTF8
Remove-Item -LiteralPath $work -Recurse -Force
Get-ChildItem $OutDir | Select-Object Name, @{ n = "KB"; e = { [int]($_.Length / 1KB) } } | Format-Table -AutoSize
