$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
& (Join-Path $root 'gradlew.bat') installClient --console=plain
if ($LASTEXITCODE -ne 0) { throw 'Could not build and install Plutonium.' }
$candidates = @('C:\XboxGames\Minecraft Launcher\Content\Minecraft.exe', 'C:\Program Files (x86)\Minecraft Launcher\MinecraftLauncher.exe', 'C:\Program Files\Minecraft Launcher\MinecraftLauncher.exe')
foreach ($candidate in $candidates) {
    if (Test-Path -LiteralPath $candidate) { Start-Process -FilePath $candidate -WindowStyle Hidden; Write-Host 'Select Plutonium + Fabric | 1.21.11 to load Fabric mods, or Plutonium | 1.21.11 for the standalone profile.'; exit 0 }
}
Write-Host 'Open Minecraft Launcher and select Plutonium + Fabric | 1.21.11 to load Fabric mods.'
