# Microsoft Store (MSIX) paket playera.
#
#   .\tools\make-msix.ps1             -> Releases\msix\jebaited_player_<verzija>_x64.msix (nepotpisan; Store ga potpisuje)
#   .\tools\make-msix.ps1 -Register   -> uz to lokalno registrira paket iz mape publish\msix-layout (treba Developer Mode)
#
# Isti exe kao GitHub inacica; u paketu ga AppPackage.IsStore prepoznaje (nadogradnje radi Store, bez Velopacka).
# Alati: dotnet i MakeAppx iz Windows SDK-a (Windows Kits\10). Nista se ne instalira.
param(
    [switch]$Register
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$props = Get-Content (Join-Path $root 'Directory.Build.props') -Raw
$version = [regex]::Match($props, '<Version>([^<]+)</Version>').Groups[1].Value
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Verzija '$version' nije oblika x.y.z." }
$msixVersion = "$version.0"

$layout = Join-Path $root 'publish\msix-layout'
$outDir = Join-Path $root 'Releases\msix'
$package = Join-Path $outDir "jebaited_player_${msixVersion}_x64.msix"

# 1. Izrada (isto kao izdanje na GitHubu: self-contained win-x64).
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
dotnet publish (Join-Path $root 'src\JebaitedPlayer.App\JebaitedPlayer.App.csproj') -c Release -r win-x64 --self-contained true `
    -p:Version=$version -p:InformationalVersion=$version -o $layout --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish nije uspio.' }
Get-ChildItem $layout -Filter *.pdb | Remove-Item

# 2. Manifest s verzijom.
$manifest = (Get-Content (Join-Path $root 'packaging\msix\AppxManifest.xml') -Raw -Encoding UTF8).Replace('$VERSION$', $msixVersion)
[IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding $false))

# 3. Ikone paketa iz assets\icon-512.png (tocne velicine iz manifesta, prozirna pozadina).
Add-Type -AssemblyName System.Drawing
$assets = Join-Path $layout 'Assets'
New-Item -ItemType Directory -Force $assets | Out-Null
$source = [System.Drawing.Image]::FromFile((Join-Path $root 'assets\icon-512.png'))
try {
    foreach ($logo in @(@('Square44x44Logo', 44, 44), @('Square150x150Logo', 150, 150), @('StoreLogo', 50, 50), @('Wide310x150Logo', 310, 150))) {
        $w = $logo[1]; $h = $logo[2]
        $bmp = New-Object System.Drawing.Bitmap $w, $h
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)
        $size = [Math]::Min($w, $h)
        $g.DrawImage($source, [int](($w - $size) / 2), [int](($h - $size) / 2), $size, $size)
        $g.Dispose()
        $bmp.Save((Join-Path $assets "$($logo[0]).png"), [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    }
}
finally {
    $source.Dispose()
}

# 4. Paket.
$makeappx = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like '*\x64\*' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) { throw 'MakeAppx nije pronadjen (Windows SDK).' }
New-Item -ItemType Directory -Force $outDir | Out-Null
& $makeappx.FullName pack /d $layout /p $package /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx nije uspio.' }
Write-Output ("Paket: {0} ({1:N1} MB)" -f $package, ((Get-Item $package).Length / 1MB))

# 5. Lokalna registracija za test (Developer Mode; paket nije potpisan, pa se registrira mapa).
if ($Register) {
    Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml') -ForceApplicationShutdown
    $pkg = Get-AppxPackage -Name 'jebaitedplayer.jebaitedplayer'
    Write-Output "Registrirano: $($pkg.PackageFullName)"
}
