param(
    [Parameter(Mandatory=$true)][string]$PublicBaseUrl,
    [string]$ClientVersion = '2.0.61',
    [string]$LauncherVersion = '2.0.61'
)
$ErrorActionPreference = 'Stop'
$uri = [Uri]$PublicBaseUrl
if (-not $uri.IsAbsoluteUri -or $uri.Scheme -ne 'https') { throw 'Supply the HTTPS directory where release files will actually be hosted.' }
[void][Version]::Parse($ClientVersion)
[void][Version]::Parse($LauncherVersion)
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'dist/release'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$base = $PublicBaseUrl.TrimEnd('/')
function Add-Asset([string]$source, [string]$name) {
    $target = Join-Path $output $name
    Copy-Item -LiteralPath (Join-Path $root $source) -Destination $target -Force
    return @{ url = "$base/$([Uri]::EscapeDataString($name))"; sha256 = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$standalone = Add-Asset 'build/client/plutonium-1.21.11.jar' "plutonium-client-$ClientVersion.jar"
$fabric = Add-Asset 'fabric/build/libs/plutonium-client-fabric-2.0.61.jar' "plutonium-fabric-$ClientVersion.jar"
$exe = Add-Asset 'dist/Plutonium Client.exe' "Plutonium.Client.exe"
$client = @{version=$ClientVersion;standalone=$standalone;fabric=$fabric} | ConvertTo-Json -Depth 4
$launcher = @{version=$LauncherVersion;executable=$exe} | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $output 'client.json'), $client)
[IO.File]::WriteAllText((Join-Path $output 'launcher.json'), $launcher)
Write-Output "Release prepared in $output. Upload binaries first, then manifests. This script does not publish anything."
