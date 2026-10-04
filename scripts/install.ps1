param([string]$MinecraftDirectory = (Join-Path $env:APPDATA '.minecraft'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$id = 'plutonium-1.21.11'
$jar = Join-Path $root "build/client/$id.jar"
$fabricMod = Join-Path $root 'fabric/build/libs/plutonium-client-fabric-1.2.0.jar'
if (-not (Test-Path -LiteralPath $jar)) { throw 'Build Plutonium first with BUILD.cmd.' }
if (-not (Test-Path -LiteralPath $fabricMod)) { throw 'Build the Fabric-compatible Plutonium mod first with BUILD.cmd.' }
$target = Join-Path $MinecraftDirectory "versions/$id"
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -LiteralPath $jar -Destination (Join-Path $target "$id.jar")
$v = Get-Content -Raw -LiteralPath (Join-Path $root 'build/game/version.json') | ConvertFrom-Json
$v.id = $id
$v.PSObject.Properties.Remove('downloads')
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $target "$id.json"), ($v | ConvertTo-Json -Depth 100), $utf8NoBom)
$loaderVersions = Invoke-RestMethod 'https://meta.fabricmc.net/v2/versions/loader/1.21.11'
$loader = $loaderVersions | Where-Object { $_.loader.stable } | Select-Object -First 1
if (-not $loader) { throw 'Fabric did not provide a stable loader version for Minecraft 1.21.11.' }
$fabricVersion = "fabric-loader-$($loader.loader.version)-1.21.11"
$fabricJson = Invoke-RestMethod "https://meta.fabricmc.net/v2/versions/loader/1.21.11/$($loader.loader.version)/profile/json"
if ($fabricJson.id -ne $fabricVersion -or $fabricJson.inheritsFrom -ne '1.21.11') {
    throw "Fabric returned unexpected launcher metadata for $fabricVersion."
}
$fabricVersionDirectory = Join-Path $MinecraftDirectory "versions/$fabricVersion"
New-Item -ItemType Directory -Force -Path $fabricVersionDirectory | Out-Null
[IO.File]::WriteAllText((Join-Path $fabricVersionDirectory "$fabricVersion.json"), ($fabricJson | ConvertTo-Json -Depth 100), $utf8NoBom)
$modsDirectory = Join-Path $MinecraftDirectory 'mods'
New-Item -ItemType Directory -Force -Path $modsDirectory | Out-Null
Copy-Item -LiteralPath $fabricMod -Destination (Join-Path $modsDirectory 'quirk-client-fabric.jar')
$profilePath = Join-Path $MinecraftDirectory 'launcher_profiles.json'
if (Get-Process -Name Minecraft,MinecraftLauncher -ErrorAction SilentlyContinue) {
    Write-Host 'Close Minecraft Launcher and any running Minecraft game to finish creating the Plutonium installation...'
    do { Start-Sleep -Seconds 2 } while (Get-Process -Name Minecraft,MinecraftLauncher -ErrorAction SilentlyContinue)
}
if (Test-Path -LiteralPath $profilePath) {
    $profiles = Get-Content -Raw -LiteralPath $profilePath | ConvertFrom-Json
    Copy-Item -LiteralPath $profilePath -Destination "$profilePath.quirk-backup-$(Get-Date -Format yyyyMMdd-HHmmss-fff)"
} else { $profiles = [pscustomobject]@{ profiles = [pscustomobject]@{}; version = 3 } }
$gameDirectory = Join-Path $MinecraftDirectory 'quirk-client'
New-Item -ItemType Directory -Force -Path $gameDirectory | Out-Null
$profile = [pscustomobject]@{
    name = 'Plutonium | 1.21.11'
    type = 'custom'
    lastVersionId = $id
    gameDir = $gameDirectory
    icon = 'Amethyst_Shard'
    javaArgs = '-Xmx4G -XX:+UseG1GC'
    created = [DateTime]::UtcNow.ToString('o')
}
if ($profiles.profiles.PSObject.Properties['quirk-client']) {
    $profiles.profiles.'quirk-client' = $profile
} else { $profiles.profiles | Add-Member -NotePropertyName 'quirk-client' -NotePropertyValue $profile }
$fabricProfile = [pscustomobject]@{
    name = 'Plutonium + Fabric | 1.21.11'
    type = 'custom'
    lastVersionId = $fabricVersion
    gameDir = $MinecraftDirectory
    icon = 'Amethyst_Shard'
    javaArgs = '-Xmx4G -XX:+UseG1GC'
    created = [DateTime]::UtcNow.ToString('o')
}
if ($profiles.profiles.PSObject.Properties['quirk-fabric']) {
    $profiles.profiles.'quirk-fabric' = $fabricProfile
} else { $profiles.profiles | Add-Member -NotePropertyName 'quirk-fabric' -NotePropertyValue $fabricProfile }
$temporary = "$profilePath.quirk-tmp"
[IO.File]::WriteAllText($temporary, ($profiles | ConvertTo-Json -Depth 100), $utf8NoBom)
Move-Item -LiteralPath $temporary -Destination $profilePath -Force
Write-Host "Installed Plutonium | 1.21.11 and Plutonium + Fabric | 1.21.11 in $MinecraftDirectory."
Write-Host "For Fabric mods, put 1.21.11-compatible Fabric mod jars in $modsDirectory and launch Plutonium + Fabric."

