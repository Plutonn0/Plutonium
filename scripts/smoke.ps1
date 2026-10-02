param([Parameter(Mandatory=$true)][string]$Java21)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = Join-Path $root 'run/smoke'
New-Item -ItemType Directory -Force -Path $run | Out-Null
$optionsPath=Join-Path $run 'options.txt'
$testOptions=if(Test-Path -LiteralPath $optionsPath){Get-Content -Raw -LiteralPath $optionsPath}else{"onboardAccessibility:false`npauseOnLostFocus:false`n"}
$testOptions=$testOptions.Replace('onboardAccessibility:true','onboardAccessibility:false').Replace('pauseOnLostFocus:true','pauseOnLostFocus:false')
[IO.File]::WriteAllText($optionsPath,$testOptions,(New-Object Text.UTF8Encoding($false)))
$resultPath = Join-Path $run 'smoke-result.txt'
if (Test-Path -LiteralPath $resultPath) { Remove-Item -LiteralPath $resultPath }
$version = Get-Content -Raw -LiteralPath (Join-Path $root 'build/game/version.json') | ConvertFrom-Json
$cp = @((Join-Path $root 'build/client/plutonium-1.21.11.jar')) + @(Get-ChildItem -LiteralPath (Join-Path $root 'build/game/libraries') -Filter '*.jar' | ForEach-Object FullName)
$arguments = @('-Xmx3G','-Dquirk.smoke=true','-Djava.awt.headless=true','-cp',($cp -join ';'),'net.minecraft.client.main.Main',
    '--username','PlutoniumSmoke','--uuid','00000000-0000-0000-0000-000000000042','--accessToken','0','--version','plutonium-1.21.11',
    '--gameDir',$run,'--assetsDir',(Join-Path $env:APPDATA '.minecraft/assets'),'--assetIndex',$version.assetIndex.id,'--width','2560','--height','1440')
$argFile = Join-Path $run 'launch.args'
$quoted = $arguments | ForEach-Object { '"' + $_.Replace('\','/').Replace('"','\"') + '"' }
[IO.File]::WriteAllLines($argFile,$quoted,(New-Object Text.UTF8Encoding($false)))
Start-Process -FilePath $Java21 -ArgumentList ('"@' + $argFile + '"') -WorkingDirectory $run -WindowStyle Hidden -RedirectStandardOutput (Join-Path $run 'stdout.log') -RedirectStandardError (Join-Path $run 'stderr.log') -PassThru | Select-Object Id
