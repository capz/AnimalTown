param([switch]$SkipAssets,[switch]$NativeInProcess,[string]$ValidationPlan='',[ValidateRange(0,7)][int]$RuntimeProfile=0)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Run([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed ($LASTEXITCODE)" }
}
$projectRoot = (Get-Location).Path
. ./tools/version.ps1
$identity=Get-BuildVersion $projectRoot
Write-VersionMetadata $identity $projectRoot
if(!$ValidationPlan){& ./tools/select-validation.ps1 -Full;$ValidationPlan='out/validation-plan.json'}
$validation=Get-Content -LiteralPath $ValidationPlan -Raw|ConvertFrom-Json
if($validation.schema -ne 2 -or $validation.commit -ne (& git rev-parse HEAD).Trim()){throw 'Validation plan must match this source commit'}
if($NativeInProcess -and !$IsLinux){throw 'NativeInProcess requires the Linux build image'}
. ./tools/resolve-packages.ps1
$nativeImage = 'ghcr.io/extremscorner/libogc2@sha256:13bb658d3f18903816617223f5d7c6f776d3db3ea806aae67f8d35a7a69d4c85'
function RunNative([string]$Directory,[string]$Command,[string[]]$Arguments){
    if($NativeInProcess){
        Push-Location (Join-Path $projectRoot $Directory)
        try{Run $Command $Arguments}finally{Pop-Location}
    }else{
        $nativeDirectory=if($Directory -eq '.'){'/project'}else{'/project/'+$Directory}
        Run docker (@('run','--rm','--init','--mount',"type=bind,src=$projectRoot,dst=/project",'-w',$nativeDirectory,$nativeImage,$Command)+$Arguments)
    }
}
Run dotnet @('build', 'tools/AnimalTown.Tools', '-c', 'Release', '-v', 'quiet')
if('repack' -in $validation.tooling){Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','repack-check',$projectRoot)}
if('terrain' -in $validation.tooling){Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','terrain-check',$projectRoot)}
if('dropped-item' -in $validation.tooling){Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','dropped-item-check',$projectRoot)}
if('nkit' -in $validation.tooling){Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','nkit-check',$projectRoot)}
if('depot' -in $validation.tooling){Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','depot-check',$projectRoot)}
Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','buildings',$projectRoot)
Run dotnet @('tool','restore')
Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','audit',$projectRoot)
if (!$SkipAssets) {
    $assetMap = [ordered]@{
        fox='villagers/Meshy_AI_Rigged_Fox_All_Animations.glb'
        bunny='villagers/villager_bunny.glb'
        giraffe='villagers/villager_giraffe.glb'
        cat='villagers/villager_mainecoon.glb'
        hamster='villagers/villager_hamster.glb'
        spaniel='villagers/villager_cockerspaniel.glb'
        building='world-stuctures/Meshy_AI_animal_crossing_konbi_1102122741_texture.glb'
        tent='world-stuctures/structure_tent.glb'
    }
    $buildingImports=Get-Content out/building-assets.json -Raw|ConvertFrom-Json -AsHashtable
    foreach($key in $buildingImports.Keys){$assetMap[$key]=$buildingImports[$key]}
    # Retire stale generated outputs from the previous species list without
    # touching artist sources. Never package both old and replacement villagers.
    foreach($retired in @('gecko','prairie','deciduous','palm','pine')){
        $old=Join-Path $projectRoot "out/disc/$retired.dndmodel"
        if(Test-Path -LiteralPath $old){
            $archive=Join-Path $projectRoot 'out/retired-models';New-Item -ItemType Directory -Force -Path $archive | Out-Null
            Move-Item -LiteralPath $old -Destination (Join-Path $archive "$retired.dndmodel") -Force
        }
    }
    foreach ($name in $assetMap.Keys) {
        if($buildingImports.ContainsKey($name)){
            # Building parts are authored in metres with hinge/floor pivots.
            # Actor normalization would invalidate their collision/socket data.
            New-Item -ItemType Directory -Force -Path out/prepared | Out-Null
            Copy-Item -LiteralPath $buildingImports[$name] -Destination "out/prepared/$name.glb" -Force
        }else{
            Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','prepare',$projectRoot,"assets/models/$($assetMap[$name])","out/prepared/$name.glb")
        }
        foreach($textureEdge in @(128,256,512,1024)){
        $suffix=if($textureEdge -eq 256){''}else{"-tex$textureEdge"}
        # DNMD retains its package and charges a decoded RGBA8 copy of each
        # texture per model, even when the native texture cache shares pixels.
        # Cottage detail uses 64px. The shop palette is flat swatches and needs
        # only 32px; keep both kits inside the same 768-KiB resident ceiling.
        $importEdge=if($buildingImports.ContainsKey($name) -and $textureEdge -eq 128){64}else{$textureEdge}
        if($buildingImports.ContainsKey($name) -and $name.StartsWith('shop-') -and $textureEdge -eq 128){$importEdge=32}
        $importArguments=@('tool','run','dnd-assets','--','model',"out/prepared/$name.glb","out/disc/$name$suffix.dndmodel",'--texture-limit',"$importEdge",'--mip-levels','2','--compile-gx-materials')
        if($assetMap[$name].StartsWith('villagers/villager_')){$importArguments+='--allow-material-approximation'}
        Run dotnet $importArguments
        Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','model-native',$projectRoot,"out/disc/$name$suffix.dndmodel")
        }
    }
}
$buildingNativeParts=@((Get-Content out/building-assets.json -Raw|ConvertFrom-Json -AsHashtable).Keys|Sort-Object|ForEach-Object{"out/disc/$($_)-tex128.dndmodel"})
RunNative '.' 'sh' (@('-c','cc -std=c11 -Wall -Wextra -Werror -ffunction-sections -fdata-sections -Iout/sdk-native/include tests/native-building-assets.c out/sdk-native/source/model.c out/sdk-native/source/model_animation.c out/sdk-native/source/verified_copy.c out/sdk-native/source/aram.c -Wl,--gc-sections -lm -o /tmp/animaltown-building-assets && /tmp/animaltown-building-assets "$@"','building-assets')+$buildingNativeParts)
# Exercise the opt-in static retention patch with the real cooked parts after
# AOT/native patching below; the pinned SDK test above remains independent.
$actorWindingParts=@('fox','bunny','giraffe','cat','hamster','spaniel'|ForEach-Object{"out/disc/$($_)-tex128.dndmodel"})
RunNative '.' 'sh' (@('-c','cc -std=c11 -Wall -Wextra -Werror -ffunction-sections -fdata-sections -Iout/sdk-native/include tests/native-model-winding.c out/sdk-native/source/model.c out/sdk-native/source/model_animation.c out/sdk-native/source/verified_copy.c out/sdk-native/source/aram.c -Wl,--gc-sections -lm -o /tmp/animaltown-model-winding && /tmp/animaltown-model-winding "$@"','model-winding')+$actorWindingParts)
New-Item -ItemType Directory -Force -Path out/disc|Out-Null
[IO.File]::WriteAllBytes((Join-Path $projectRoot 'out/disc/runtime.cfg'),[byte[]]@($RuntimeProfile))
& (Join-Path $PSScriptRoot 'build-updater.ps1') -NativeInProcess:$NativeInProcess
$updaterCommit=(Get-Content build/updater.lock.json -Raw|ConvertFrom-Json).commit
$updaterHash=(Get-FileHash out/disc/animal-town-updater.dol -Algorithm SHA256).Hash
$gameCommit = $identity.bb
$gameFullCommit = $identity.commit
$gameVersion = $identity.version
$workingChanges = & git status --porcelain --untracked-files=normal
if ($workingChanges) { $gameCommit += '-dirty'; $gameVersion += '-dirty' }
$packageLocks=(Get-FileHash game/AnimalTown.GameCube/packages.lock.json -Algorithm SHA256).Hash
$assetsVersion=(Get-Content (Join-Path $projectRoot '.config/dotnet-tools.json') -Raw|ConvertFrom-Json).tools.'dolphindotnet.assets'.version
$assetsContentHash=(Get-Content (Join-Path $packageCache "dolphindotnet.assets/$assetsVersion/dolphindotnet.assets.$assetsVersion.nupkg.sha512") -Raw).Trim()
$sourceManifest = foreach($sourceFile in (& git ls-files --cached --others --exclude-standard | Sort-Object)) {
    if(Test-Path -LiteralPath $sourceFile -PathType Leaf){ "$sourceFile $((Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash)" }
}
$sourceDigest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($sourceManifest -join "`n"))))
Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','content-identity',$projectRoot,$gameFullCommit,$gameVersion,$sourceDigest,"$RuntimeProfile",$gameCommit)
if($validation.rendering.Count){Run dotnet @('run','--project','tests/AnimalTown.RenderingChecks','-c','Release','--','--suites',($validation.rendering -join ','))}
if($validation.managed.Count){Run dotnet @('run','--project','tests/AnimalTown.Checks','-c','Release','--','--suites',($validation.managed -join ','))}
if('card' -in $validation.native){Run dotnet @('run','--project','tests/AnimalTown.CardChecks','-c','Release')}
if('card' -in $validation.native){Run dotnet @('run','--project','vendor/card-store-sdk/tests/CardRecordStoreTests','-c','Release')}
if('content' -in $validation.native){
    Run dotnet @('build','tests/AnimalTown.ContentSmoke','-c','Release','-p:RestoreLockedMode=true','-v','quiet')
    Run dotnet @("$sdkRoot/tools/compiler/DolphinDotNet.Compiler.dll",'--aot','tests/AnimalTown.ContentSmoke/bin/Release/net8.0/AnimalTown.ContentSmoke.dll','out/content-startup.c')
    RunNative '.' 'python3' @('tests/native-content-startup.py')
}
if('card' -in $validation.native){RunNative '.' 'sh' @('-c','cc -std=c11 -Wall -Wextra -Werror tests/native-card-read.c -o /tmp/animaltown-card-read && /tmp/animaltown-card-read && cc -std=c11 -Wall -Wextra -Werror tests/native-card-mount.c -o /tmp/animaltown-card-mount && /tmp/animaltown-card-mount && cc -std=c11 -Wall -Wextra -Werror -Ivendor/card-store-sdk/include vendor/card-store-sdk/tests/card_blocks_test.c -o /tmp/animaltown-card-blocks && /tmp/animaltown-card-blocks')}
if('uv' -in $validation.native){RunNative '.' 'sh' @('-c','cc -std=c11 -Wall -Wextra -Werror tests/native-uv-topology.c -o /tmp/animaltown-uv-topology && /tmp/animaltown-uv-topology')}
if('texture' -in $validation.native){RunNative '.' 'sh' @('-c','cc -std=c11 -Wall -Wextra -Werror -Iout/sdk-native/include tests/native-texture-pages.c -o /tmp/animaltown-texture-pages && /tmp/animaltown-texture-pages')}
if('voices' -in $validation.native){RunNative '.' 'sh' @('-c','cc -std=c11 -Wall -Wextra -Werror -Iout/sdk-native/include tests/native-dialogue-voices.c out/sdk-native/source/audio.c -lm -o /tmp/animaltown-dialogue-voices && /tmp/animaltown-dialogue-voices')}
if('python' -in $validation.native){RunNative '.' 'python3' @('tests/frame-profile-streaming.py')}
if('shadows' -in $validation.native){RunNative '.' 'sh' @('-c','cc -std=c11 -Wall -Wextra -Werror tests/native-shadow-union.c -lm -o /tmp/animaltown-shadow-union && /tmp/animaltown-shadow-union && cc -std=c11 -Wall -Wextra -Werror -Iout/sdk-native/include tests/native-shadow-lifecycle.c -lm -o /tmp/animaltown-shadow-lifecycle && /tmp/animaltown-shadow-lifecycle')}
Run dotnet @('build','game/AnimalTown.GameCube','-c','Release','--no-restore','-v','quiet','-p:DepotProfile=false')
Run dotnet @("$sdkRoot/tools/compiler/DolphinDotNet.Compiler.dll",'--bundle','game/AnimalTown.GameCube/bin/Release/net8.0/AnimalTown.GameCube.dll','out/native')
& (Join-Path $PSScriptRoot 'patch-native.ps1') -Root $projectRoot
RunNative '.' 'sh' (@('-c','cc -DANIMALTOWN_RETAIN_BUILDINGS -std=c11 -Wall -Wextra -Werror -ffunction-sections -fdata-sections -Iout/native/include tests/native-building-assets.c out/native/source/model.c out/native/source/model_animation.c out/native/source/verified_copy.c out/native/source/aram.c -Wl,--gc-sections -lm -o /tmp/animaltown-building-retained && /tmp/animaltown-building-retained "$@"','building-retained')+$buildingNativeParts)
$residentNativeModels=@(@('fox','bunny','giraffe','cat','hamster','spaniel')|ForEach-Object{"out/disc/$($_).dndmodel"})
RunNative '.' 'sh' (@('-c','cc -std=c11 -Wall -Wextra -Werror -ffunction-sections -fdata-sections -Iout/native/include tests/native-resident-models.c out/native/source/model.c out/native/source/model_animation.c out/native/source/verified_copy.c out/native/source/aram.c -Wl,--gc-sections -lm -o /tmp/animaltown-resident-models && /tmp/animaltown-resident-models "$@"','resident-models')+$residentNativeModels)
function CompileNative([string]$Directory,[string]$Target){
$nativeOutput=[IO.Path]::GetFullPath((Join-Path $projectRoot $Directory))
$workspacePrefix=$projectRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
if(!$nativeOutput.StartsWith($workspacePrefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Native output must stay inside the workspace'}
$nativeWorkingRoot=if($NativeInProcess){$nativeOutput}else{'/project/'+$Directory}
$nativeKey="$nativeWorkingRoot|$nativeImage|$env:ANIMALTOWN_BUILD_IMAGE|$sdkContentHash"
$nativeKeyFile=Join-Path $nativeOutput '.build-root'
# Make dependency files contain absolute mount paths. Never reuse objects from
# the host-native /project mount inside the combined /workspace image (or vice versa).
# Packaged SDK sources retain archive timestamps; a newer ABI can otherwise
# appear older than cached objects. Include package content in the cache key.
if((Test-Path -LiteralPath (Join-Path $nativeOutput 'build')) -and
   (!(Test-Path -LiteralPath $nativeKeyFile) -or [IO.File]::ReadAllText($nativeKeyFile) -ne $nativeKey)){
    # Preserve the independently verified upstream checkout across object cleans.
    RunNative $Directory 'sh' @('-c','if [ -d build/exi-upstream/.git ] && [ ! -e .exi-source-cache ]; then mv build/exi-upstream .exi-source-cache; fi')
    RunNative $Directory 'make' @('clean')
    RunNative $Directory 'sh' @('-c','if [ -d .exi-source-cache/.git ]; then mkdir -p build; mv .exi-source-cache build/exi-upstream; fi')
}
[IO.File]::WriteAllText($nativeKeyFile,$nativeKey)
RunNative $Directory 'make' @('-j2',$Target)
}
CompileNative 'out/native' 'apploader'
# Compile the SD app independently. Its bootstrap cannot mount or borrow a
# disc, including when a launcher supplies no argv. Keep both native bundles
# separate so Make's cached objects cannot mix the two compile-time profiles.
Run dotnet @('build','game/AnimalTown.GameCube','-c','Release','--no-restore','-v','quiet','-p:DepotProfile=true')
Run dotnet @("$sdkRoot/tools/compiler/DolphinDotNet.Compiler.dll",'--bundle','game/AnimalTown.GameCube/bin/Release/net8.0/AnimalTown.GameCube.dll','out/native-depot')
& (Join-Path $PSScriptRoot 'patch-native.ps1') -Root $projectRoot -NativeDirectory 'out/native-depot'
CompileNative 'out/native-depot' 'all'
Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','depot-receipt',$projectRoot)
$nativeOutput=Join-Path $projectRoot 'out/native'
# The same SDK package emits the validated IPL image and ships its MIT license.
# No independently versioned boot download or game-owned C build is needed.
Copy-Item -LiteralPath (Join-Path $nativeOutput 'boot/gamecube/LICENSE') -Destination (Join-Path $projectRoot 'out/disc/sdk-apploader-LICENSE.txt') -Force
$apploaderCommit=$sdkMetadata.package.metadata.repository.commit
$apploaderContentHash=(Get-FileHash -LiteralPath (Join-Path $nativeOutput 'apploader.img') -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $projectRoot 'out/build-revisions.txt'), "Version: $($identity.version)`nGame source: $gameFullCommit`nEngine: $engineCommit`nSDK: $sdkCommit`nPackage lock SHA256: $packageLocks`nEngine NuGet SHA512: $engineContentHash`nSDK NuGet SHA512: $sdkContentHash`nApploader SDK: capz/DolphinDotNet ($apploaderCommit)`nApploader SHA256: $apploaderContentHash`nAssets NuGet SHA512: $assetsContentHash`nUpdater source: $updaterCommit`nUpdater DOL SHA256: $updaterHash`nRuntime profile: $RuntimeProfile`nDirty: $([bool]$workingChanges)`nSource digest: $sourceDigest`nBuild image: $env:ANIMALTOWN_BUILD_IMAGE`n")
# NKit needs a raw source for conversion and the exact round-trip check. Keep
# both raw images temporary; deliver the verified NKit and standalone app ZIP.
$discStage=Join-Path $projectRoot ('out/disc-work/'+[guid]::NewGuid().ToString('N'))
$nkit=Join-Path $projectRoot "out/AnimalTown-$gameVersion.nkit.iso"
New-Item -ItemType Directory -Force -Path $discStage|Out-Null
# Banner/build metadata is shared with the app and does not require ISO creation.
Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','art',$projectRoot)
Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','banner',$projectRoot,$gameVersion)
$notices=Join-Path $projectRoot 'out/depot-notices'
New-Item -ItemType Directory -Force -Path $notices|Out-Null
Copy-Item -LiteralPath (Join-Path $sdkRoot 'LICENSE') -Destination (Join-Path $notices 'DolphinDotNet-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $engineRoot 'LICENSE') -Destination (Join-Path $notices 'Hypercube-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $sdkRoot 'tools/compiler/native/include/ryu/LICENSE-Boost') -Destination (Join-Path $notices 'Ryu-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $sdkRoot 'tools/compiler/native/vendor/ufbx/LICENSE') -Destination (Join-Path $notices 'ufbx-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/dolphin-depot-install.txt') -Destination (Join-Path $notices 'DEPOT-README.txt') -Force
$thirdParty="DolphinDotNet: $sdkCommit`nHypercube: $engineCommit`nBoth declare GPL-2.0-only; full texts accompany this package.`nCorresponding dependency sources: https://github.com/capz/DolphinDotNet and https://github.com/capz/hypercube-engine at the commits above.`nRyu floating-point conversion: Boost Software License 1.0, Ryu-LICENSE.txt.`nGorditas font: SIL Open Font License 1.1, Gorditas-OFL.txt.`nGame sources and asset provenance: https://github.com/capz/AnimalTown/tree/$gameFullCommit`nOriginal AnimalTown code/assets have no blanket redistribution permission established by this packaging feature. See docs/depot-release.md in that source revision. A NOASSERTION catalog draft is not permission to redistribute.`n"
$thirdParty+="ufbx model importer: see ufbx-LICENSE.txt.`n"
[IO.File]::WriteAllText((Join-Path $notices 'THIRD-PARTY-NOTICES.txt'),$thirdParty)
$depot=Join-Path $projectRoot "out/AnimalTown-$gameVersion-depot.zip"
Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','depot',$projectRoot,$depot)
try{
    $sourceIso=Join-Path $discStage 'source.iso'
    Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','iso',$projectRoot,'out/native/Game.dol',$gameVersion,$sourceIso)
    & (Join-Path $PSScriptRoot 'compress-nkit.ps1') -Root $projectRoot -Iso $sourceIso -Destination $nkit -Report (Join-Path $projectRoot 'out/nkit-validation.json')
}finally{
    if(Test-Path -LiteralPath $discStage){Remove-Item -LiteralPath $discStage -Recurse -Force}
}
Write-Host "Bootable NKit ISO: $nkit"
Write-Host "Standalone Dolphin Depot app: $depot"
