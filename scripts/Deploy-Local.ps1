param([ValidateSet('Development','Prod-v1')][string]$Profile = 'Development')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw 'Close Valheim before replacing the runtime package.' }
$bep = Join-Path $env:APPDATA "com.kesomannen.gale/valheim/profiles/$Profile/BepInEx"
if (!(Test-Path -LiteralPath (Join-Path $bep 'core/BepInEx.dll'))) { throw "Missing profile BepInEx: $bep" }
$source = Join-Path $root 'artifacts/ForestCrawler-0.2.5/BepInEx/plugins/ForestCrawler'
foreach ($name in @('ForestCrawler.dll','forestcrawler.assets')) { if (!(Test-Path -LiteralPath (Join-Path $source $name))) { throw "Incomplete package: $name. Build-Package.ps1 must succeed first." } }
$destination = Join-Path $bep 'plugins/ForestCrawler'
$backup = Join-Path $root ('artifacts/backups/' + $Profile + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $backup | Out-Null
if (Test-Path -LiteralPath $destination) {
    Copy-Item -LiteralPath $destination -Destination $backup -Recurse
}
New-Item -ItemType Directory -Force $destination | Out-Null
foreach ($name in @('ForestCrawler.dll','forestcrawler.assets')) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination $destination -Force }
# Migrate only the previous default to the explicitly requested 20% baseline.
$config = Join-Path $bep 'config/norskit.ForestCrawler.cfg'
if ($Profile -eq 'Development' -and (Test-Path -LiteralPath $config)) {
    $text = [IO.File]::ReadAllText($config)
    $updated = [regex]::Replace($text, '(?m)^BaselineRatePerEligibleHour = 3[.,]1608(?=\r?$)', 'BaselineRatePerEligibleHour = 6.6943065')
    if ($updated -ne $text) {
        Copy-Item -LiteralPath $config -Destination $backup
        [IO.File]::WriteAllText($config, $updated)
    }
}
# Console.SetConsoleEnabledForThisSession uses the installed game's supported API.
# It works through Gale's existing launch route without rewriting any arguments.
Get-ChildItem -LiteralPath $destination -File | Get-FileHash | Format-Table
Write-Output "Installed into Gale $Profile. Launch that profile, enter a world, press F5 and run crawler_spawn."
