param([string]$BlenderPath, [string]$UnityPath, [switch]$SkipBlender)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$generated = Join-Path $root 'unity/Assets/Editor/Generated'
New-Item -ItemType Directory -Force $generated | Out-Null
$solver = Get-Content -LiteralPath (Join-Path $root 'src/ForestCrawler/FootSolver.cs') -Raw
[IO.File]::WriteAllText((Join-Path $generated 'FootSolver.cs'), $solver.Replace('namespace ForestCrawler;', 'namespace ForestCrawler {') + [Environment]::NewLine + '}')
$gaze = [IO.File]::ReadAllText((Join-Path $root 'src/ForestCrawler/GazeProbe.cs')).Replace('namespace ForestCrawler;', 'namespace ForestCrawler {') + [Environment]::NewLine + '}'
[IO.File]::WriteAllText((Join-Path $generated 'GazeProbe.cs'), $gaze)
$approach = [IO.File]::ReadAllText((Join-Path $root 'src/ForestCrawler/ApproachPath.cs')).Replace('namespace ForestCrawler;', 'namespace ForestCrawler {') + [Environment]::NewLine + '}'
[IO.File]::WriteAllText((Join-Path $generated 'ApproachPath.cs'), $approach)
$traversal = [IO.File]::ReadAllText((Join-Path $root 'src/ForestCrawler/Traversal.cs')).Replace('namespace ForestCrawler;', 'namespace ForestCrawler {') + [Environment]::NewLine + '}'
[IO.File]::WriteAllText((Join-Path $generated 'Traversal.cs'), $traversal)
$surface = [IO.File]::ReadAllText((Join-Path $root 'src/ForestCrawler/SurfacePath.cs')).Replace('namespace ForestCrawler;', 'namespace ForestCrawler {') + [Environment]::NewLine + '}'
[IO.File]::WriteAllText((Join-Path $generated 'SurfacePath.cs'), $surface)
$music = [IO.File]::ReadAllText((Join-Path $root 'src/ForestCrawler/MusicSilence.cs')).Replace('namespace ForestCrawler;', 'namespace ForestCrawler {') + [Environment]::NewLine + '}'
[IO.File]::WriteAllText((Join-Path $generated 'MusicSilence.cs'), $music)
if (!$BlenderPath) { $BlenderPath = Join-Path $root 'tools/blender-4.5.3-windows-x64/blender.exe' }
if (!$SkipBlender) {
    if (!(Test-Path -LiteralPath $BlenderPath)) { throw 'Blender 4.5.3 is missing; pass -BlenderPath.' }
    foreach ($script in @('Prepare-Model.py','Animate-Model.py','Validate-Model.py','Closeup-Model.py')) {
        & $BlenderPath --background --python (Join-Path $PSScriptRoot $script)
        if ($LASTEXITCODE -ne 0) { throw "Blender failed: $script" }
    }
}
if (!$UnityPath) {
    $candidates = @('C:\Program Files\Unity\Hub\Editor\6000.0.75f1\Editor\Unity.exe','C:\Program Files\Unity 6000.0.75f1\Editor\Unity.exe','C:\Program Files\Unity\Editor\Unity.exe','C:\dev\tools\Unity6000.0.75f1\Editor\Unity.exe')
    $UnityPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$UnityPath) { throw 'Unity Editor 6000.0.75f1 is missing. Install tools/UnitySetup64-6000.0.75f1.exe, activate the Editor license, then pass -UnityPath.' }
$log = Join-Path $root 'artifacts/unity-build.log'
$arguments = @('-batchmode','-nographics','-quit','-projectPath',('"' + (Join-Path $root 'unity') + '"'),'-executeMethod','CrawlerBuild.Build','-logFile',('"' + $log + '"'))
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'FORESTCRAWLER_BUILD_OK' -Quiet)) { throw "Unity build/activation failed; inspect $log" }
Write-Output 'AssetBundle built and editor-validated.'
