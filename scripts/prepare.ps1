$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$game = Join-Path $root 'build/game'
$libs = Join-Path $game 'libraries'
New-Item -ItemType Directory -Force $game, $libs | Out-Null
$mc = Join-Path $env:APPDATA '.minecraft'
function Hash-Sha1($path) {
    $stream = [IO.File]::OpenRead($path)
    $hash = [Security.Cryptography.SHA1]::Create()
    try { return [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
    finally { $hash.Dispose(); $stream.Dispose() }
}
function Fetch-Verified($url, $path, $sha, $cached) {
    if ((Test-Path -LiteralPath $path) -and ((Hash-Sha1 $path) -eq $sha)) { return }
    if ($cached -and (Test-Path -LiteralPath $cached) -and ((Hash-Sha1 $cached) -eq $sha)) {
        Copy-Item -LiteralPath $cached -Destination $path
    } else { Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $path }
    if ((Hash-Sha1 $path) -ne $sha) { throw "Download checksum mismatch: $path" }
}
$manifest = Invoke-RestMethod 'https://piston-meta.mojang.com/mc/game/version_manifest_v2.json'
$entry = $manifest.versions | Where-Object id -eq '1.21.11'
Fetch-Verified $entry.url (Join-Path $game 'version.json') $entry.sha1 (Join-Path $mc 'versions/1.21.11/1.21.11.json')
$v = Get-Content -Raw (Join-Path $game 'version.json') | ConvertFrom-Json
Fetch-Verified $v.downloads.client.url (Join-Path $game 'client.jar') $v.downloads.client.sha1 (Join-Path $mc 'versions/1.21.11/1.21.11.jar')
Fetch-Verified $v.downloads.client_mappings.url (Join-Path $game 'mappings.txt') $v.downloads.client_mappings.sha1 $null
foreach ($lib in $v.libraries) {
    $a = $lib.downloads.artifact
    if (-not $a) { continue }
    $leaf = ($lib.name -replace '[:/]', '_') + '.jar'
    Fetch-Verified $a.url (Join-Path $libs $leaf) $a.sha1 (Join-Path (Join-Path $mc 'libraries') $a.path)
}
Write-Host 'Minecraft 1.21.11 build inputs verified.'
