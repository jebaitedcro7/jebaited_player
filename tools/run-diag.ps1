<#
.SYNOPSIS
  Pokrece jebaited_player u dijagnostickom nacinu (--diag) za zadane datoteke, paralelno biljezi
  opterecenje GPU-a i video dekodera (nvidia-smi) i vraca sazetak mjerenja.

.EXAMPLE
  . .\tools\run-diag.ps1
  Invoke-Diag -Name "4K HEVC" -File "testdata\m1\4k_hevc10_sdr_ac3-51.mkv" -Seconds 20 -Fullscreen
#>

$script:Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
# JEBAITED_EXE: drugi exe (npr. Debug build dok je Release otvoren i zaključan).
$script:Exe = if ($env:JEBAITED_EXE) { $env:JEBAITED_EXE } else { Join-Path $script:Root "src\JebaitedPlayer.App\bin\Release\net10.0-windows\jebaited_player.exe" }
$script:DiagDir = Join-Path $script:Root "testdata\diag"
New-Item -ItemType Directory -Force -Path $script:DiagDir | Out-Null

function Invoke-Diag {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [string[]]$File,
        [int]$Seconds = 20,
        [switch]$Fullscreen,
        [switch]$Controls,
        [string]$ExtraArgs,
        [int]$TimeoutSeconds = 120
    )

    $safe = ($Name -replace '[^\w\-]+', '_')
    $json = Join-Path $script:DiagDir "$safe.json"
    $gpuLog = Join-Path $script:DiagDir "$safe.gpu.csv"
    Remove-Item -LiteralPath $json, $gpuLog -ErrorAction SilentlyContinue

    $argList = "--diag $Seconds --diag-out `"$json`""
    if ($Fullscreen) { $argList += " --diag-fullscreen" }
    if ($Controls) { $argList += " --diag-controls" }
    if ($ExtraArgs) { $argList += " $ExtraArgs" }
    foreach ($f in @($File | Where-Object { $_ })) { $argList += " `"$((Resolve-Path -LiteralPath $f).Path)`"" }

    $app = Start-Process -FilePath $script:Exe -ArgumentList $argList -PassThru

    # GPU samo za proces playera (Windows brojaci "GPU Engine" po procesu, nisu lokalizirani u WMI-ju).
    $gpuJob = Start-Job -ArgumentList $app.Id, $gpuLog -ScriptBlock {
        param($procId, $outFile)
        $prefix = "pid_$($procId)_*"
        while ($true) {
            $rows = Get-CimInstance Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine |
                Where-Object { $_.Name -like $prefix }
            $d3 = ($rows | Where-Object { $_.Name -like '*engtype_3D' } | Measure-Object UtilizationPercentage -Sum).Sum
            $vd = ($rows | Where-Object { $_.Name -like '*engtype_VideoDecode' } | Measure-Object UtilizationPercentage -Sum).Sum
            "$([int]$d3),$([int]$vd)" | Add-Content -LiteralPath $outFile
            Start-Sleep -Milliseconds 1000
        }
    }

    $exited = $app.WaitForExit($TimeoutSeconds * 1000)
    if (-not $exited) { $app.Kill() }
    Stop-Job $gpuJob -ErrorAction SilentlyContinue; Remove-Job $gpuJob -Force -ErrorAction SilentlyContinue

    $r = $null
    if (Test-Path -LiteralPath $json) { $r = Get-Content -LiteralPath $json -Raw -Encoding UTF8 | ConvertFrom-Json }

    # GPU: zadnjih $Seconds uzoraka prije izlaza (otprilike period mjerenja u aplikaciji).
    $gpuAvg = $null; $decAvg = $null
    if (Test-Path -LiteralPath $gpuLog) {
        $rows = Get-Content -LiteralPath $gpuLog | Where-Object { $_ -match '^\d' } | ForEach-Object {
            $p = $_.Split(','); [pscustomobject]@{ Gpu = [double]$p[0]; Dec = [double]$p[1] }
        }
        $rows = @($rows | Select-Object -SkipLast 2 | Select-Object -Last ([Math]::Max($Seconds, 1)))
        if ($rows.Count -gt 0) {
            $gpuAvg = [Math]::Round(($rows | Measure-Object Gpu -Average).Average, 1)
            $decAvg = [Math]::Round(($rows | Measure-Object Dec -Average).Average, 1)
        }
    }

    $m = if ($r) { $r.mpv } else { $null }
    [pscustomobject]@{
        Test          = $Name
        Ok            = [bool]($exited -and $r -and -not $r.exception)
        StartupMs     = if ($r) { $r.startupMs } else { $null }
        MpvInitMs     = if ($r) { $r.mpvInitMs } else { $null }
        FirstFrameMs  = if ($r) { $r.firstFrameMs } else { $null }
        Codec         = if ($m) { $m.'video-codec' } else { $null }
        Res           = if ($m -and $m.width) { "$($m.width)x$($m.height)" } else { $null }
        PixFmt        = if ($m) { $m.'video-params/pixelformat' } else { $null }
        Gamma         = if ($m) { $m.'video-params/gamma' } else { $null }
        Hwdec         = if ($m) { $m.'hwdec-current' } else { $null }
        Audio         = if ($m) { "$($m.'audio-codec-name') $($m.'audio-params/channel-count')ch" } else { $null }
        CpuAvg        = if ($r) { $r.cpuAvgPercent } else { $null }
        CpuMax        = if ($r) { $r.cpuMaxPercent } else { $null }
        RamMB         = if ($r) { $r.workingSetAvgMB } else { $null }
        PrivateMB     = if ($r) { $r.privateAvgMB } else { $null }
        Gpu3D         = $gpuAvg
        GpuDecode     = $decAvg
        Drops         = if ($m) { "$($m.'frame-drop-count')/$($m.'decoder-frame-drop-count')" } else { $null }
        Fps           = if ($m) { "$($m.'estimated-vf-fps')" } else { $null }
        AudioOnly     = if ($r) { $r.isAudioOnly } else { $null }
        Routed        = if ($r) { $r.mouseRoutedToHost } else { $null }
        ChildDisabled = if ($r) { "$($r.videoChildWindowsDisabled)/$($r.videoChildWindows)" } else { $null }
        Error         = if ($r) { $r.errorMessage } else { "nema rezultata (timeout=$(-not $exited))" }
    }
}

function Save-WindowScreenshot {
    <# Snima glavni prozor procesa (fizicki pikseli) u PNG. #>
    param([Parameter(Mandatory)] [System.Diagnostics.Process]$Process, [Parameter(Mandatory)] [string]$Path)

    if (-not ("ShotNative" -as [type])) {
        Add-Type -AssemblyName System.Drawing
        Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class ShotNative {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int s);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
"@
        [ShotNative]::SetProcessDPIAware() | Out-Null
    }

    $Process.Refresh()
    $h = $Process.MainWindowHandle
    if ($h -eq [IntPtr]::Zero) { throw "Prozor nije pronaden." }
    $rect = New-Object ShotNative+RECT
    [ShotNative]::DwmGetWindowAttribute($h, 9, [ref]$rect, 16) | Out-Null   # DWMWA_EXTENDED_FRAME_BOUNDS
    $w = $rect.R - $rect.L; $hgt = $rect.B - $rect.T
    $bmp = New-Object System.Drawing.Bitmap $w, $hgt
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($rect.L, $rect.T, 0, 0, $bmp.Size)
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    "$Path ($w x $hgt)"
}
