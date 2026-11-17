<#
.SYNOPSIS
  Izdanje: samostalni paket (win-x64), instalacija (Setup.exe), prijenosna verzija i paketi za nadogradnju (Velopack).

.DESCRIPTION
  - Ništa ne šalje na GitHub. Objava izdanja je zaseban korak: tools\upload-release.ps1.
  - Prethodno izdanje s GitHuba se samo preuzima (javno, bez prijave), da Velopack napravi mali paket razlika (delta).
  - Rezultat je u mapi Releases (nije u gitu):
      JebaitedPlayer-win-Setup.exe     instalacija za korisnike
      JebaitedPlayer-win-Portable.zip  prijenosna verzija (bez instalacije)
      JebaitedPlayer-<verzija>-full.nupkg / -delta.nupkg, releases.win.json  za nadogradnje

.EXAMPLE
  .\tools\release.ps1 -Version 1.2.0     (bilješke izdanja iz History\update-history.json, tools\release-notes.ps1)
#>
param(
    [Parameter(Mandatory)] [string]$Version,
    [string]$OutputDir,
    [string]$Notes,
    [switch]$SkipGitHubDownload
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutputDir) { $OutputDir = Join-Path $root "Releases" }
$OutputDir = [IO.Path]::GetFullPath($OutputDir)

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') { throw "Verzija mora biti oblika 1.2.3 (ili 1.2.3-oznaka)." }

$props = Get-Content (Join-Path $root "Directory.Build.props") -Raw
$repoUrl = ([regex]::Match($props, '<UpdateRepositoryUrl>([^<]+)</UpdateRepositoryUrl>')).Groups[1].Value
if (-not $repoUrl) { throw "UpdateRepositoryUrl nije zadan u Directory.Build.props." }

$publishDir = Join-Path $root "publish\$Version"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Push-Location $root
try {
    dotnet tool restore | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore nije uspio (vpk)." }

    $publishArgs = @(
        "publish", (Join-Path $root "src\JebaitedPlayer.App\JebaitedPlayer.App.csproj"),
        "-c", "Release", "-r", "win-x64", "--self-contained", "true",
        "-p:Version=$Version", "-p:InformationalVersion=$Version",
        "-o", $publishDir, "--nologo", "-v", "q"
    )

    # Ključ aplikacije za OpenSubtitles (jedan za sve korisnike; svatko se prijavljuje svojim računom).
    # Nije u gitu: lokalna datoteka secrets\opensubtitles-app-key.txt. U exe ide prikriven (isti postupak kao AppSecrets.cs).
    $keyFile = Join-Path $root "secrets\opensubtitles-app-key.txt"
    if (Test-Path $keyFile) {
        $key = (Get-Content $keyFile -Raw).Trim()
        $mask = [Text.Encoding]::ASCII.GetBytes("jebaited_player/opensubtitles")
        $bytes = [Text.Encoding]::UTF8.GetBytes($key)
        for ($i = 0; $i -lt $bytes.Length; $i++) { $bytes[$i] = $bytes[$i] -bxor $mask[$i % $mask.Length] }
        $publishArgs += "-p:JebaitedOsAppKey=$([Convert]::ToBase64String($bytes))"
        $key = $null
        "OpenSubtitles: ključ aplikacije je ugrađen (korisnik upisuje samo korisničko ime i lozinku)."
    }
    else {
        "OpenSubtitles: nema secrets\opensubtitles-app-key.txt, pa korisnici upisuju i vlastiti API ključ."
    }
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish nije uspio." }

    # libmpv mora biti točno ona provjerena (SHA256 u third_party\libmpv\SOURCE.md).
    $mpv = (Get-FileHash (Join-Path $publishDir "libmpv-2.dll") -Algorithm SHA256).Hash.ToLowerInvariant()
    $expected = ([regex]::Match((Get-Content (Join-Path $root "third_party\libmpv\SOURCE.md") -Raw), 'libmpv-2\.dll` \| `([0-9a-f]{64})')).Groups[1].Value
    if ($mpv -ne $expected) { throw "libmpv-2.dll u paketu nema očekivani SHA256 ($mpv)." }

    $exeVersion = (Get-Item (Join-Path $publishDir "jebaited_player.exe")).VersionInfo.ProductVersion
    if ($exeVersion -ne $Version) { throw "Exe ima verziju '$exeVersion', a izdanje je $Version." }

    if (-not $SkipGitHubDownload) {
        # Prethodno izdanje (ako postoji) za pakete razlika. Prvo izdanje ga nema; to nije greška.
        & dotnet vpk download github --repoUrl $repoUrl -o $OutputDir 2>&1 | ForEach-Object { "  vpk: $_" }
    }

    $packArgs = @(
        "vpk", "pack",
        "--packId", "JebaitedPlayer",
        "--runtime", "win-x64",
        "--packVersion", $Version,
        "--packDir", $publishDir,
        "--mainExe", "jebaited_player.exe",
        "--packTitle", "jebaited_player",
        "--packAuthors", "jebaited_player",
        "--icon", (Join-Path $root "assets\jebaited_player.ico"),
        "--splashImage", (Join-Path $root "assets\icon-512.png"),
        "--shortcuts", "Desktop,StartMenuRoot",
        "--outputDir", $OutputDir
    )
    if (-not $Notes) {
        # Bilješke izdanja (UPGRADE 3): engleski, njemački i hrvatski iz povijesti nadogradnji (update-history.json),
        # s datumom i poveznicom na prethodnu verziju. Ista povijest je u playeru i u CHANGELOG*.md.
        # Testne verzije (npr. 0.99.x u run-update-test.ps1) nisu u povijesti i nemaju bilješke.
        $history = Get-Content (Join-Path $root "src\JebaitedPlayer.App\History\update-history.json") -Raw -Encoding UTF8
        if ($history -match ('"version":\s*"' + [regex]::Escape($Version) + '"')) {
            $Notes = Join-Path $OutputDir "notes-$Version.md"
            & (Join-Path $PSScriptRoot "release-notes.ps1") -Version $Version -Out $Notes | Out-Host
        }
        else {
            "Verzija $Version nije u update-history.json: izdanje bez bilješki."
        }
    }
    if ($Notes) { $packArgs += @("--releaseNotes", (Resolve-Path $Notes).Path) }
    & dotnet @packArgs
    if ($LASTEXITCODE -ne 0) { throw "vpk pack nije uspio." }
}
finally {
    Pop-Location
}

Get-ChildItem $OutputDir -File | Where-Object { $_.Name -match [regex]::Escape($Version) -or $_.Name -match '^(JebaitedPlayer-win-|releases\.|RELEASES|assets\.)' } |
    Sort-Object Name | ForEach-Object { "{0,-48} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
"Izdanje $Version je u $OutputDir (još nije objavljeno)."
