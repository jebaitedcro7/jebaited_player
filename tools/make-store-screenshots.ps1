# Snimke za Microsoft Store: slike iz manual\images\<jezik> na platnu 1920x1080 (Store trazi najmanje 1366x768).
# Slike se ne povecavaju (ostaju ostre), nego se centriraju na tamnu pozadinu playera.
#   .\tools\make-store-screenshots.ps1   -> Releases\msix\screenshots\<en|de|hr>\NN-ime.png
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$names = 'home', 'playback', 'playlist', 'search', 'sync', 'settings'
foreach ($lang in 'en', 'de', 'hr') {
    $out = Join-Path $root "Releases\msix\screenshots\$lang"
    New-Item -ItemType Directory -Force $out | Out-Null
    $n = 0
    foreach ($name in $names) {
        $src = Get-ChildItem (Join-Path $root "manual\images\$lang") -File | Where-Object { $_.BaseName -eq $name } | Select-Object -First 1
        if (-not $src) { throw "Nema slike $lang/$name." }
        $n++
        $img = [System.Drawing.Image]::FromFile($src.FullName)
        $bmp = New-Object System.Drawing.Bitmap 1920, 1080
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.Clear([System.Drawing.Color]::FromArgb(255, 13, 16, 32))
        $scale = [Math]::Min(1.0, [Math]::Min(1800 / $img.Width, 1000 / $img.Height))
        $w = [int]($img.Width * $scale); $h = [int]($img.Height * $scale)
        $g.DrawImage($img, [int]((1920 - $w) / 2), [int]((1080 - $h) / 2), $w, $h)
        $g.Dispose(); $img.Dispose()
        $bmp.Save((Join-Path $out ('{0:D2}-{1}.png' -f $n, $name)), [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    }
    Write-Output "$lang`: $n snimki"
}

# Logotipi za Store (bez njih Store koristi male slicice iz paketa): iz assets\logo.png (1024x1024).
$logoOut = Join-Path $root 'Releases\msix\store-logos'
New-Item -ItemType Directory -Force $logoOut | Out-Null
$icon = [System.Drawing.Image]::FromFile((Join-Path $root 'assets\icon-512.png'))
$logo = [System.Drawing.Image]::FromFile((Join-Path $root 'assets\logo.png'))
try {
    # Pločica = ikona aplikacije (prozirno). Box art i poster = brand logo na svijetloj pozadini (tamni natpis JEBAITED_CRO ostaje citljiv).
    foreach ($spec in @(@('AppTileIcon_300x300', 300, 300, 300, $icon), @('BoxArt_1080x1080', 1080, 1080, 900, $logo), @('PosterArt_720x1080', 720, 1080, 620, $logo))) {
        $w = $spec[1]; $h = $spec[2]; $s = $spec[3]; $img = $spec[4]
        $bmp = New-Object System.Drawing.Bitmap $w, $h
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        if ($img -eq $icon) { $g.Clear([System.Drawing.Color]::Transparent) } else { $g.Clear([System.Drawing.Color]::FromArgb(255, 232, 232, 232)) }
        $y = if ($h -gt $w) { [int](($h - $s) / 2) - 80 } else { [int](($h - $s) / 2) }
        $g.DrawImage($img, [int](($w - $s) / 2), $y, $s, $s)
        if ($h -gt $w) {
            # Poster: naziv ispod loga
            $font = New-Object System.Drawing.Font 'Segoe UI Semibold', 46
            $fmt = New-Object System.Drawing.StringFormat
            $fmt.Alignment = [System.Drawing.StringAlignment]::Center
            $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 30, 30, 30))
            $g.DrawString('jebaited_player', $font, $brush, (New-Object System.Drawing.RectangleF 0, ($y + $s + 10), $w, 90), $fmt)
            $brush.Dispose(); $font.Dispose()
        }
        $g.Dispose()
        $bmp.Save((Join-Path $logoOut "$($spec[0]).png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    }
}
finally {
    $icon.Dispose(); $logo.Dispose()
}
Write-Output "Logotipi: $logoOut"
