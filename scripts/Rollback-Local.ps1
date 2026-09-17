param([Parameter(Mandatory)][string]$BackupDirectory, [ValidateSet('Development','Prod-v1')][string]$Profile = 'Prod-v1')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$allowed = [IO.Path]::GetFullPath((Join-Path $root 'artifacts/backups')).TrimEnd('\') + '\'
$backup = [IO.Path]::GetFullPath($BackupDirectory)
if (!$backup.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Backup must be inside this project artifacts/backups directory.' }
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw 'Close Valheim before rollback.' }
$source = Join-Path $backup 'ForestCrawler'
$destination = Join-Path $env:APPDATA "com.kesomannen.gale/valheim/profiles/$Profile/BepInEx/plugins/ForestCrawler"
foreach ($name in @('ForestCrawler.dll','forestcrawler.assets')) { if (!(Test-Path -LiteralPath (Join-Path $source $name))) { throw "Backup missing $name" } }
New-Item -ItemType Directory -Force $destination | Out-Null
foreach ($name in @('ForestCrawler.dll','forestcrawler.assets')) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination $destination -Force }
Write-Output "Restored ForestCrawler runtime files to $Profile. Persistent cooldown/config files were retained."
