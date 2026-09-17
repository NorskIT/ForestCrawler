$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$downloads = [Environment]::ExpandEnvironmentVariables((Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders').'{374DE290-123F-4565-9164-39C4925E467B}')
if (!$downloads -or !(Test-Path -LiteralPath $downloads)) { throw 'Windows Downloads known folder could not be resolved.' }
$destination = Join-Path $root 'assets/source'
New-Item -ItemType Directory -Force $destination | Out-Null
$names = @('the-smile-rigged.zip','the_smile__rigged.glb','sound_scary_idle_female_whisper.mp3','sound_scary_idle_woo_woo.mp3','sound_scary_idle_i_see_you.mp3','sound_scary_attack_scream.mp3','sound_scary_heartbeat_single.mp3','sound_scary_chase_very_close.mp3','sound_scary_attack_caught_player_1.mp3','sound_scary_attack_caught_player_2.mp3')
$hashes = foreach ($name in $names) {
    $source = Join-Path $downloads $name
    if (!(Test-Path -LiteralPath $source)) { throw "Missing source asset: $source" }
    Copy-Item -LiteralPath $source -Destination $destination -Force
    [pscustomobject]@{ Name = $name; SHA256 = (Get-FileHash -LiteralPath $source).Hash }
}
$hashes | ConvertTo-Json | Set-Content (Join-Path $destination 'hashes.json')
Expand-Archive -LiteralPath (Join-Path $destination 'the-smile-rigged.zip') -DestinationPath (Join-Path $destination 'unpacked') -Force
Expand-Archive -LiteralPath (Join-Path $destination 'unpacked/source/Smiley.zip') -DestinationPath (Join-Path $destination 'fbx') -Force
Write-Output 'Copied source assets and extracted the nested FBX archive; downloads were not modified.'
