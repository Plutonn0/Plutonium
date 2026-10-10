param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$standalone = Join-Path $root 'build/client/plutonium-1.21.11.jar'
$fabric = Join-Path $root 'fabric/build/libs/plutonium-client-fabric-2.0.61.jar'
$metadata = Join-Path $root 'build/game/version.json'
foreach ($required in @($standalone, $fabric, $metadata)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Required build artifact is missing: $required" }
}
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'dist' }
New-Item -ItemType Directory -Force -Path $output | Out-Null
& dotnet publish (Join-Path $root 'launcher/PlutoniumLauncher.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    --output $output
if ($LASTEXITCODE -ne 0) { throw 'Could not publish the Plutonium Client launcher.' }
$executable = Join-Path $output 'Plutonium Client.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'The self-contained Plutonium Client.exe was not produced.' }
Write-Output "Built $executable"
