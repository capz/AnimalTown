param([switch]$NativeInProcess)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$lock=Get-Content (Join-Path $root 'build/updater.lock.json') -Raw|ConvertFrom-Json
if($lock.commit -notmatch '^[0-9a-f]{40}$' -or $lock.repository -ne 'https://github.com/capz/AnimalTown.Updater.git'){throw 'Invalid updater source lock'}
function Run([string]$file,[string[]]$arguments){& $file @arguments;if($LASTEXITCODE){throw "$file updater build failed"}}
$source=$env:ANIMALTOWN_UPDATER_SOURCE
if(!$source){
    $source=Join-Path $root 'out/updater-source'
    if(!(Test-Path (Join-Path $source '.git'))){
        New-Item -ItemType Directory -Force $source|Out-Null
        Run git @('init',$source)
        Run git @('-C',$source,'remote','add','origin',$lock.repository)
    }
    $head=(& git -C $source rev-parse HEAD 2>$null)
    if($head -ne $lock.commit){
        if(& git -C $source status --porcelain --untracked-files=normal){throw 'Preserve local updater source changes before changing the lock'}
        Run git @('-C',$source,'fetch','--depth','1','origin',$lock.commit)
        Run git @('-C',$source,'checkout','--detach','FETCH_HEAD')
    }
}
if((& git -C $source rev-parse HEAD).Trim() -ne $lock.commit -or (& git -C $source status --porcelain --untracked-files=normal)){throw 'Updater source must be a clean checkout of the pinned commit'}
$build=Join-Path $source 'out/release/build.json';$dol=Join-Path $source 'out/release/animal-town-updater.dol'
$cached=$false
if((Test-Path $build) -and (Test-Path $dol)){
    $manifest=Get-Content $build -Raw|ConvertFrom-Json
    $sums=Get-Content (Join-Path $source 'out/release/SHA256SUMS.txt') -Raw
    $digest=(Get-FileHash $dol -Algorithm SHA256).Hash.ToLowerInvariant()
    $cached=$manifest.source -eq $lock.commit -and !$manifest.dirty -and $sums.Contains("$digest  animal-town-updater.dol")
}
if(!$cached){
    $backend=if($NativeInProcess){'Native'}else{'Docker'}
    $arguments=@((Join-Path $source 'tools/build.py'),'--backend',$backend)
    if($env:ANIMALTOWN_UPDATER_SDK_SOURCE){$arguments+=@('--sdk-source',$env:ANIMALTOWN_UPDATER_SDK_SOURCE)}
    Run python3 $arguments
}
$manifest=Get-Content $build -Raw|ConvertFrom-Json
if($manifest.source -ne $lock.commit -or $manifest.dirty){throw 'Updater must be built cleanly from its pinned source'}
New-Item -ItemType Directory -Force (Join-Path $root 'out/disc')|Out-Null
Copy-Item -LiteralPath $dol -Destination (Join-Path $root 'out/disc/animal-town-updater.dol') -Force
Write-Host "Packaged animal-town-updater.dol ($($lock.commit))"

# Ship the matching PC-side setup tool with the release tools archive.
Copy-Item -LiteralPath (Join-Path $source 'tools/provision.py') -Destination (Join-Path $root 'out/updater-provision.py') -Force
Copy-Item -LiteralPath (Join-Path $source 'README.md') -Destination (Join-Path $root 'out/updater-setup.md') -Force
