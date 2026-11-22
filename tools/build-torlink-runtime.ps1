<#
.SYNOPSIS
  Gradi Torlink runtime paket za jebaited_player (Node + Torlink, bez node-datachannel) i zapise MANIFEST.json sa SHA-256 svake datoteke.

.DESCRIPTION
  Isti skript koristi lokalni razvoj i GitHub Actions (.github/workflows/torlink-runtime.yml).
  Koraci:
   1. Node (zakucana verzija) se preuzima s nodejs.org i provjerava prema sluzbenom SHASUMS256.txt.
   2. Izvorni kod Torlinka se klonira na tocnu oznaku (commit se zapisuje), `npm ci --ignore-scripts` iz NJIHOVOG
      package-lock.json (nijedna install skripta se ne izvrsava), `npm run build`, `npm prune --omit=dev`.
   3. Uklanjaju se node-datachannel (WebRTC je trajno iskljucen) i prebuilds drugih platformi.
   4. Smoke test: `--version` i `serve` + GET /health, na pravom paketu (Node bez node-datachannel, TORLINK_NO_WEBRTC=1).
   5. Raspored: node.exe, torlink\dist, torlink\node_modules, torlink\package.json, licenses\, BUILD-INFO.txt, MANIFEST.json.
   6. Zip (kose crte u imenima) + SHA256SUMS.txt u izlaznoj mapi; npm audit izvjestaj se sprema uz njih.
  Ime paketa: torlink-runtime-<torlink>-r<revizija>-win-x64.zip (oznaka izdanja: torlink-runtime-<torlink>-r<revizija>).
.PARAMETER UpstreamTag
  Oznaka u github.com/baairon/torlink, npr. v1.11.1.
.PARAMETER Revision
  Nasa revizija paketa za istu Torlink verziju (1, 2, ...).
.PARAMETER NodeVersion
  Zakucana verzija Nodea (>= 22.15 zbog zamjene za WebRTC).
.PARAMETER OutDir
  Izlazna mapa (zadano publish\torlink-runtime).
.PARAMETER SourceDir
  Vec preuzet izvorni kod Torlinka (preskace git clone; za testiranje bez mreze prema GitHubu).
#>
param(
    [Parameter(Mandatory = $true)][string]$UpstreamTag,
    [int]$Revision = 1,
    [string]$NodeVersion = '22.23.5',
    [string]$OutDir,
    [string]$SourceDir,
    [string]$Work
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutDir) { $OutDir = Join-Path (Join-Path $root 'publish') 'torlink-runtime' }
if (-not $Work) { $Work = Join-Path ([IO.Path]::GetTempPath()) ('torlink-runtime-' + [Guid]::NewGuid().ToString('N')) }
if ($UpstreamTag -notmatch '^v(\d+\.\d+\.\d+)$') { throw "Neispravna oznaka: $UpstreamTag (ocekivano vX.Y.Z)." }
$torlinkVersion = $Matches[1]
if ([version]$NodeVersion -lt [version]'22.15.0') { throw 'Node mora biti >= 22.15.0.' }
New-Item -ItemType Directory -Force -Path $Work, $OutDir | Out-Null
function Step($m) { Write-Host ('== ' + $m) }
function Native($file, $argList, $cwd) {
    # Windows PowerShell 5.1 stderr nativnih naredbi (npr. git savjeti) pretvara u greske; ovdje je to samo tekst, a uspjeh se mjeri izlaznim kodom.
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    Push-Location $cwd
    try {
        & $file @argList 2>&1 | ForEach-Object { Write-Host ([string]$_) }
        if ($LASTEXITCODE -ne 0) { throw ("Naredba nije uspjela ($LASTEXITCODE): $file " + ($argList -join ' ')) }
    } finally { Pop-Location; $ErrorActionPreference = $prev }
}

# ---------- 1. Node ----------
Step "Node $NodeVersion (SHA-256 prema SHASUMS256.txt s nodejs.org)"
$nodeBase = "https://nodejs.org/dist/v$NodeVersion"
$nodeZipName = "node-v$NodeVersion-win-x64.zip"
$nodeZip = Join-Path $Work $nodeZipName
Invoke-WebRequest -Uri "$nodeBase/SHASUMS256.txt" -OutFile (Join-Path $Work 'SHASUMS256.txt') -UseBasicParsing
Invoke-WebRequest -Uri "$nodeBase/$nodeZipName" -OutFile $nodeZip -UseBasicParsing
$expected = ((Get-Content (Join-Path $Work 'SHASUMS256.txt')) | Where-Object { $_ -match ('  ' + [regex]::Escape($nodeZipName) + '$') } | Select-Object -First 1).Split(' ')[0]
if (-not $expected) { throw 'SHASUMS256.txt ne sadrzi trazeni zip.' }
$actual = (Get-FileHash $nodeZip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $expected.ToLowerInvariant()) { throw "SHA-256 Nodea se ne podudara ($actual != $expected)." }
Expand-Archive -Path $nodeZip -DestinationPath (Join-Path $Work 'node') -Force
$nodeRoot = Join-Path (Join-Path $Work 'node') "node-v$NodeVersion-win-x64"
$nodeExe = Join-Path $nodeRoot 'node.exe'
$npm = Join-Path $nodeRoot 'npm.cmd'
$env:PATH = "$nodeRoot;$env:PATH"

# ---------- 2. Torlink izvor, build ----------
if ($SourceDir) {
    $src = (Resolve-Path $SourceDir).Path
    Step "Izvor (lokalni): $src"
    $commit = 'local'
} else {
    Step "Klon baairon/torlink $UpstreamTag"
    $src = Join-Path $Work 'src'
    Native 'git' @('-c', 'advice.detachedHead=false', 'clone', '--quiet', '--depth', '1', '--branch', $UpstreamTag, 'https://github.com/baairon/torlink.git', $src) $Work
    $commit = (& git -C $src rev-parse HEAD).Trim()
}
$pkg = Get-Content (Join-Path $src 'package.json') -Raw | ConvertFrom-Json
if ($pkg.version -ne $torlinkVersion) { throw "package.json ima verziju $($pkg.version), a oznaka je $UpstreamTag." }
Step 'npm ci --ignore-scripts (iz njihovog package-lock.json)'
Native $npm @('ci', '--ignore-scripts', '--no-audit', '--no-fund') $src
Step 'npm run build'
Native $npm @('run', 'build') $src
if (-not (Test-Path (Join-Path $src 'dist\cli.cjs'))) { throw 'dist\cli.cjs nije izgraden.' }
Step 'npm audit (izvjestaj, ne blokira)'
Push-Location $src
try { & $npm audit --omit=dev --json 2>$null | Set-Content (Join-Path $OutDir 'npm-audit.json') -Encoding UTF8 } catch { } finally { Pop-Location }
$global:LASTEXITCODE = 0
Native $npm @('prune', '--omit=dev', '--ignore-scripts', '--no-audit', '--no-fund') $src

# ---------- 3. skracivanje ----------
Step 'Uklanjanje node-datachannel i prebuilds drugih platformi'
foreach ($p in @('node_modules\node-datachannel', 'node_modules\@node-datachannel')) {
    $full = Join-Path $src $p
    if (Test-Path $full) { Remove-Item $full -Recurse -Force }
}
Get-ChildItem (Join-Path $src 'node_modules') -Recurse -Directory -Filter prebuilds -ErrorAction SilentlyContinue | ForEach-Object {
    Get-ChildItem $_.FullName -Directory | Where-Object { $_.Name -ne 'win32-x64' } | Remove-Item -Recurse -Force
}

# ---------- 4. raspored ----------
Step 'Raspored paketa'
$stage = Join-Path $Work 'runtime'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'torlink'), (Join-Path $stage 'licenses\npm') | Out-Null
Copy-Item $nodeExe (Join-Path $stage 'node.exe')
Copy-Item (Join-Path $src 'dist') (Join-Path $stage 'torlink\dist') -Recurse
Copy-Item (Join-Path $src 'node_modules') (Join-Path $stage 'torlink\node_modules') -Recurse
Copy-Item (Join-Path $src 'package.json') (Join-Path $stage 'torlink\package.json')
Copy-Item (Join-Path $nodeRoot 'LICENSE') (Join-Path $stage 'licenses\node-LICENSE.txt')
Copy-Item (Join-Path $src 'LICENSE') (Join-Path $stage 'licenses\torlink-LICENSE.txt')
$index = New-Object System.Collections.Generic.List[string]
$dirs = @(Get-ChildItem (Join-Path $src 'node_modules') -Directory | Where-Object { $_.Name -notlike '@*' })
$dirs += @(Get-ChildItem (Join-Path $src 'node_modules') -Directory -Filter '@*' | ForEach-Object { Get-ChildItem $_.FullName -Directory })
foreach ($d in $dirs) {
    $pj = Join-Path $d.FullName 'package.json'
    if (-not (Test-Path $pj)) { continue }
    $j = Get-Content $pj -Raw | ConvertFrom-Json
    $lic = if ($j.license -is [string]) { $j.license } elseif ($j.license) { $j.license.type } elseif ($j.licenses) { ($j.licenses | ForEach-Object { $_.type }) -join ' OR ' } else { 'UNKNOWN' }
    $index.Add(('{0}@{1}  {2}' -f $j.name, $j.version, $lic))
    $dest = Join-Path (Join-Path $stage 'licenses\npm') ($j.name -replace '[\\/:*?"<>|]', '_')
    $files = @(Get-ChildItem $d.FullName -File | Where-Object { $_.Name -match '^(LICEN[SC]E|COPYING|NOTICE)' })
    if ($files.Count -gt 0) {
        New-Item -ItemType Directory -Force -Path $dest | Out-Null
        $files | ForEach-Object { Copy-Item $_.FullName (Join-Path $dest $_.Name) }
    }
}
$index | Sort-Object | Set-Content (Join-Path $stage 'licenses\NPM-PACKAGES.txt') -Encoding UTF8
if (($index | Where-Object { $_ -match '  UNKNOWN$' }).Count -gt 0) { Write-Host 'UPOZORENJE: paketi bez licence u package.json:'; $index | Where-Object { $_ -match '  UNKNOWN$' } | ForEach-Object { Write-Host "  $_" } }

# ---------- 5. smoke test na pravom rasporedu ----------
Step 'Smoke test (--version, serve + /health)'
$stateDir = Join-Path $Work 'smoke-state'
$env:TORLINK_STATE_DIR = $stateDir; $env:TORLINK_NO_UPDATE_CHECK = '1'; $env:TORLINK_NO_WEBRTC = '1'
$cli = Join-Path $stage 'torlink\dist\cli.cjs'
$prevEap = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
$ver = (& (Join-Path $stage 'node.exe') $cli --version 2>&1 | ForEach-Object { [string]$_ } | Where-Object { $_ -notmatch 'WebRTC' } | Select-Object -First 1)
$ErrorActionPreference = $prevEap
if ($ver -notmatch [regex]::Escape($torlinkVersion)) { throw "--version vratio '$ver', ocekivano $torlinkVersion." }
$listener = New-Object System.Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0); $listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop()
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = (Join-Path $stage 'node.exe'); $psi.Arguments = "`"$cli`" serve --port $port --to `"$Work\smoke-dl`""
$psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
$psi.EnvironmentVariables['TORLINK_STATE_DIR'] = $stateDir
$proc = [System.Diagnostics.Process]::Start($psi)
$ok = $false
try {
    for ($i = 0; $i -lt 40 -and -not $ok; $i++) {
        Start-Sleep -Milliseconds 250
        try { $r = Invoke-RestMethod -Uri "http://127.0.0.1:$port/health" -TimeoutSec 2; if ($r.ok) { $ok = $true } } catch { }
    }
} finally { if (-not $proc.HasExited) { $proc.Kill() } }
if (-not $ok) { throw 'serve nije odgovorio na /health.' }
Remove-Item Env:\TORLINK_STATE_DIR, Env:\TORLINK_NO_UPDATE_CHECK, Env:\TORLINK_NO_WEBRTC -ErrorAction SilentlyContinue
Write-Host "   --version = $ver; /health OK"

# ---------- 6. BUILD-INFO, MANIFEST ----------
Step 'BUILD-INFO i MANIFEST.json (SHA-256 svake datoteke)'
$builtAt = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
@(
    "torlink $UpstreamTag (commit $commit) from https://github.com/baairon/torlink",
    "Node $NodeVersion win-x64 (SHA-256 verified against $nodeBase/SHASUMS256.txt)",
    'npm ci --ignore-scripts from the upstream package-lock.json; node-datachannel removed (WebRTC permanently disabled); non-win32-x64 prebuilds removed',
    "Revision r$Revision, built $builtAt",
    $(if ($env:GITHUB_RUN_ID) { "Workflow run: $($env:GITHUB_SERVER_URL)/$($env:GITHUB_REPOSITORY)/actions/runs/$($env:GITHUB_RUN_ID)" } else { 'Local build' })
) | Set-Content (Join-Path $stage 'BUILD-INFO.txt') -Encoding UTF8

$files = [ordered]@{}
Get-ChildItem $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
    $rel = $_.FullName.Substring($stage.Length).TrimStart('\', '/').Replace('\', '/')
    $files[$rel] = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
$manifest = [ordered]@{
    schema = 1; torlinkVersion = $torlinkVersion; revision = $Revision; nodeVersion = $NodeVersion
    upstreamCommit = $commit; builtAt = $builtAt; files = $files
}
[IO.File]::WriteAllText((Join-Path $stage 'MANIFEST.json'), ($manifest | ConvertTo-Json -Depth 5), (New-Object System.Text.UTF8Encoding($false)))

# ---------- 7. zip + SHA256SUMS ----------
Step 'Zip i SHA256SUMS.txt'
$name = "torlink-runtime-$torlinkVersion-r$Revision-win-x64.zip"
$zipPath = Join-Path $OutDir $name
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($zipPath, 'Create')
try {
    Get-ChildItem $stage -Recurse -File | ForEach-Object {
        $entry = $_.FullName.Substring($stage.Length).TrimStart('\', '/').Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $_.FullName, $entry, 'Optimal')
    }
} finally { $zip.Dispose() }
$zipSha = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $OutDir 'SHA256SUMS.txt'), "$zipSha  $name`n", (New-Object System.Text.UTF8Encoding($false)))
Copy-Item (Join-Path $stage 'MANIFEST.json') (Join-Path $OutDir 'MANIFEST.json') -Force
Write-Host ''
Write-Host "Paket:   $zipPath ($([math]::Round((Get-Item $zipPath).Length / 1MB, 1)) MB, $($files.Count) datoteka)"
Write-Host "SHA-256: $zipSha"
Write-Host "Oznaka izdanja: torlink-runtime-$torlinkVersion-r$Revision"
if ($env:GITHUB_STEP_SUMMARY) {
    "### Torlink runtime $torlinkVersion r$Revision`n`n``````n$zipSha  $name`ncommit $commit`n``````" | Add-Content $env:GITHUB_STEP_SUMMARY
}
$global:LASTEXITCODE = 0
exit 0
