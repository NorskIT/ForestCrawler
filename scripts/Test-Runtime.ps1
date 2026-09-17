param([string]$Name = ('runtime-' + (Get-Date -Format 'yyyyMMdd-HHmmss')), [switch]$AssetsOnly, [switch]$MusicOnly, [switch]$NativeOnly)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($Name -notmatch '^[a-zA-Z0-9-]+$') { throw 'Invalid run name.' }
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw 'Close Valheim before running the isolated smoke fixture.' }
$run = Join-Path $root "artifacts/$Name"
if (Test-Path -LiteralPath $run) { throw 'Use a new run name.' }
& dotnet build (Join-Path $root 'tests/ForestCrawler.RuntimeSmoke/ForestCrawler.RuntimeSmoke.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Smoke compilation failed.' }
$bep = Join-Path $env:APPDATA 'com.kesomannen.gale/valheim/profiles/Development/BepInEx'
New-Item -ItemType Directory -Force "$run/BepInEx/core", "$run/BepInEx/plugins/ForestCrawler", "$run/saves" | Out-Null
Copy-Item -Path (Join-Path $bep 'core/*') -Destination "$run/BepInEx/core"
Copy-Item -LiteralPath (Join-Path $root 'src/ForestCrawler/bin/Release/net481/ForestCrawler.dll') -Destination "$run/BepInEx/plugins/ForestCrawler"
Copy-Item -LiteralPath (Join-Path $root 'artifacts/bundle/forestcrawler.assets') -Destination "$run/BepInEx/plugins/ForestCrawler"
Copy-Item -LiteralPath (Join-Path $root 'tests/ForestCrawler.RuntimeSmoke/bin/Release/net481/ForestCrawler.RuntimeSmoke.dll') -Destination "$run/BepInEx/plugins/ForestCrawler"
$priorDoorstop = $env:DOORSTOP_TARGET_ASSEMBLY
$priorOutput = $env:FORESTCRAWLER_SMOKE_OUTPUT
$priorAssets = $env:FORESTCRAWLER_SMOKE_ASSETS_ONLY
$priorNative = $env:FORESTCRAWLER_SMOKE_NATIVE_ONLY
$priorMusic = $env:FORESTCRAWLER_SMOKE_MUSIC_ONLY
try {
    $env:DOORSTOP_TARGET_ASSEMBLY = "$run/BepInEx/core/BepInEx.Preloader.dll"
    $env:FORESTCRAWLER_SMOKE_OUTPUT = $run
    $env:FORESTCRAWLER_SMOKE_ASSETS_ONLY = if ($AssetsOnly) { "1" } else { "0" }
    $env:FORESTCRAWLER_SMOKE_NATIVE_ONLY = if ($NativeOnly) { "1" } else { "0" }
    $env:FORESTCRAWLER_SMOKE_MUSIC_ONLY = if ($MusicOnly) { "1" } else { "0" }
    $game = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim.exe'
    $arguments = @('-batchmode','-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-savedir',('"' + "$run/saves" + '"'),'-logFile',('"' + "$run/unity.log" + '"'),'--doorstop-enabled','true','--doorstop-target-assembly',('"' + "$run/BepInEx/core/BepInEx.Preloader.dll" + '"'))
    $process = Start-Process -FilePath $game -ArgumentList $arguments -WorkingDirectory $run -WindowStyle Hidden -PassThru
    $process.Id | Set-Content "$run/process-id.txt"
    Write-Output "Isolated Valheim process $($process.Id), logs: $run"
} finally { $env:DOORSTOP_TARGET_ASSEMBLY = $priorDoorstop; $env:FORESTCRAWLER_SMOKE_OUTPUT = $priorOutput; $env:FORESTCRAWLER_SMOKE_ASSETS_ONLY = $priorAssets; $env:FORESTCRAWLER_SMOKE_MUSIC_ONLY = $priorMusic; $env:FORESTCRAWLER_SMOKE_NATIVE_ONLY = $priorNative }
