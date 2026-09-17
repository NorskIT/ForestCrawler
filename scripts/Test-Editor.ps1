param([string]$UnityPath = 'C:\Program Files\Unity 6000.0.75f1\Editor\Unity.exe')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$generated = Join-Path $root 'unity/Assets/Editor/Generated'
New-Item -ItemType Directory -Force $generated | Out-Null
# Unity 6's C# compiler uses block namespaces; test exactly the production solver body.
$source = Get-Content -LiteralPath (Join-Path $root 'src/ForestCrawler/FootSolver.cs') -Raw
$source = $source.Replace('namespace ForestCrawler;', 'namespace ForestCrawler {') + [Environment]::NewLine + '}'
[IO.File]::WriteAllText((Join-Path $generated 'FootSolver.cs'), $source)
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
$log = Join-Path $root 'artifacts/unity-preview.log'
$arguments = @('-batchmode','-quit','-projectPath',('"' + (Join-Path $root 'unity') + '"'),'-executeMethod','CrawlerPreview.Validate','-logFile',('"' + $log + '"'))
$process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0 -or !(Select-String -LiteralPath $log -Pattern 'FORESTCRAWLER_PREVIEW_OK' -Quiet)) { throw "Editor preview tests failed. Inspect $log" }
Write-Output 'Production traversal and foot solver tested in Unity on 0/15/30/45/60/85-degree slopes.'
