param([switch]$ServerOnly)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
& dotnet build (Join-Path $root 'src/ForestCrawler/ForestCrawler.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Release compilation failed.' }
& dotnet run --project (Join-Path $root 'tests/ForestCrawler.Tests/ForestCrawler.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
$bundle = Join-Path $root 'artifacts/bundle/forestcrawler.assets'
if (!$ServerOnly -and (!(Test-Path -LiteralPath $bundle) -or !(Test-Path -LiteralPath (Join-Path $root 'artifacts/bundle/validation.txt')))) { throw 'Client package requires a successfully built and validated AssetBundle. Run Build-Assets.ps1 first.' }
$manifestPath = Join-Path $root 'packaging/manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$version = ([xml](Get-Content -LiteralPath (Join-Path $root 'src/ForestCrawler/ForestCrawler.csproj') -Raw)).Project.PropertyGroup.Version
if ($manifest.version_number -ne $version) { throw 'Manifest and assembly versions must match.' }
$name = if ($ServerOnly) { "ForestCrawler-server-$version" } else { "ForestCrawler-$version" }
$package = Join-Path $root "artifacts/$name"
$pluginDir = Join-Path $package 'BepInEx/plugins/ForestCrawler'
New-Item -ItemType Directory -Force $pluginDir | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'src/ForestCrawler/bin/Release/net481/ForestCrawler.dll') -Destination $pluginDir -Force
if (!$ServerOnly) { Copy-Item -LiteralPath $bundle -Destination $pluginDir -Force }
foreach ($file in @('README.md','DEVELOPMENT.md','ATTRIBUTION.md','VERIFICATION.md','REVIEW.md')) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $package -Force }
Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $package 'manifest.json') -Force
Copy-Item -LiteralPath (Join-Path $root 'packaging/icon.png') -Destination (Join-Path $package 'icon.png') -Force
Get-ChildItem -LiteralPath $package -Recurse -File | Where-Object { $_.Name -ne 'hashes.json' } | Get-FileHash | ForEach-Object {
    [PSCustomObject]@{ Path = $_.Path.Substring($package.Length + 1); SHA256 = $_.Hash }
} | ConvertTo-Json | Set-Content (Join-Path $package 'hashes.json')
Compress-Archive -Path (Join-Path $package '*') -DestinationPath ($package + '.zip') -Force
Write-Output "Package: $package.zip"
