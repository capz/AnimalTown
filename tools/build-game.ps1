param([switch]$SkipAssets,[switch]$NativeInProcess,[switch]$RunNativeTests,[string]$ValidationPlan='',[ValidateRange(0,7)][int]$RuntimeProfile=4,[switch]$OnlinePairing)
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
if(!$ValidationPlan){& ./tools/select-validation.ps1 -Full -SkipPolicyChecks;$ValidationPlan='out/validation-plan.json'}
$validation=Get-Content -LiteralPath $ValidationPlan -Raw|ConvertFrom-Json
if($validation.schema -ne 3 -or $validation.commit -ne (& git rev-parse HEAD).Trim()){throw 'Validation plan must match this source commit'}
$testBuildings=$RunNativeTests -and 'building' -in $validation.tooling
$testModels=$RunNativeTests -and ('memory' -in $validation.resources -or 'visual' -in $validation.resources)
$testPatches=$RunNativeTests -and 'patched' -in $validation.native
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
function VerifyNativeNetworking([string]$Directory){
    $libraryLine=Get-Content -LiteralPath (Join-Path $Directory 'Makefile') | Where-Object { $_ -match '^LIBS :=' }
    if(!$libraryLine){throw "Missing native library list in $Directory/Makefile"}
    $libraries=$libraryLine -split '\s+'
    foreach($library in @('-ldndtls','-ldndentropy')){
        if(($libraries -contains $library) -ne $OnlinePairing.IsPresent){
            throw "Unexpected $library linkage in $Directory (online pairing: $OnlinePairing)"
        }
    }
}
Run dotnet @('build', 'tools/AnimalTown.Tools', '-c', 'Release', '-v', 'quiet')
Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','buildings',$projectRoot)
Run dotnet @('tool','restore')
Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','audit',$projectRoot)
Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','prune-model-variants',$projectRoot)
if (!$SkipAssets) {
    $assetMap = [ordered]@{
        fox='villagers/Meshy_AI_Rigged_Fox_All_Animations.glb'
        bunny='villagers/villager_bunny.glb'
        giraffe='villagers/villager_giraffe.glb'
        cat='villagers/villager_mainecoon.glb'
        hamster='villagers/villager_hamster.glb'
        spaniel='villagers/villager_cockerspaniel.glb'
        penguin='villagers/villager_penguin.glb'
        whitetiger='villagers/villager_whitetiger.glb'
        lion='villagers/villager_lion.glb'
        robin='villagers/villager_robin.glb'
        raccoon='villagers/villager_raccoon.glb'
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
            $prepare=@('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','prepare',$projectRoot,"assets/models/$($assetMap[$name])","out/prepared/$name.glb")
            if($assetMap[$name].StartsWith('villagers/')){$prepare+='--villager-runtime'}
            Run dotnet $prepare
        }
        $textureEdges=if($assetMap[$name].StartsWith('villagers/')){@(128,256)}else{@(128,256,512,1024)}
        foreach($textureEdge in $textureEdges){
        $suffix=if($textureEdge -eq 256){''}else{"-tex$textureEdge"}
        # DNMD retains its package and charges a decoded RGBA8 copy of each
        # texture per model, even when the native texture cache shares pixels.
        # Cottage detail uses 64px. Shop/koban use compact palette atlases at
        # 32px; indexed static imports keep all three kits inside 768 KiB.
        $importEdge=if($buildingImports.ContainsKey($name) -and $textureEdge -eq 128){64}else{$textureEdge}
        if($buildingImports.ContainsKey($name) -and ($name.StartsWith('shop-') -or $name.StartsWith('koban-')) -and $textureEdge -eq 128){$importEdge=32}
        $importArguments=@('tool','run','dnd-assets','--','model',"out/prepared/$name.glb","out/disc/$name$suffix.dndmodel",'--texture-limit',"$importEdge",'--mip-levels','2','--compile-gx-materials')
        if($assetMap[$name].StartsWith('villagers/villager_')){$importArguments+='--allow-material-approximation'}
        Run dotnet $importArguments
        Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','model-native',$projectRoot,"out/disc/$name$suffix.dndmodel")
        }
    }
}
# Rebuild geometric LODs from the selected normalized packages even with
# SkipAssets, so a reused staging directory cannot ship a stale LOD cohort.
if($testModels){Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','model-lod-check',$projectRoot)}
Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','model-lod',$projectRoot)
if($testModels){Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','model-memory-check',$projectRoot)}
Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','model-memory',$projectRoot)
if($testBuildings){
    $buildingNativeParts=@((Get-Content out/building-assets.json -Raw|ConvertFrom-Json -AsHashtable).Keys|Sort-Object|ForEach-Object{"out/disc/$($_)-tex128.dndmodel"})
    # Closed composites remain cooked comparison assets. Runtime reuses the seven
    # exterior/door/interior parts, so only those enter its retained memory budget.
    $buildingResidentParts=@($buildingNativeParts|Where-Object{$_ -notlike '*-exterior-closed-tex128.dndmodel'})
    RunNative '.' 'python3' @('tests/building-closed-equivalence.py','out/disc')
    RunNative '.' 'sh' (@('-c','cc -std=c11 -Wall -Wextra -Werror -ffunction-sections -fdata-sections -Iout/sdk-native/include tests/native-building-assets.c out/sdk-native/source/model.c out/sdk-native/source/model_animation.c out/sdk-native/source/verified_copy.c out/sdk-native/source/aram.c -Wl,--gc-sections -lm -o /tmp/animaltown-building-assets && /tmp/animaltown-building-assets "$@"','building-assets')+$buildingNativeParts)
    # Exercise the opt-in static retention patch with the real cooked parts after
    # AOT/native patching below; the pinned SDK test above remains independent.
}
if($testModels){
    $actorWindingParts=@('fox','bunny','giraffe','cat','hamster','spaniel'|ForEach-Object{"out/disc/$($_)-tex128.dndmodel"})
    RunNative '.' 'sh' (@('-c','cc -std=c11 -Wall -Wextra -Werror -ffunction-sections -fdata-sections -Iout/sdk-native/include tests/native-model-winding.c out/sdk-native/source/model.c out/sdk-native/source/model_animation.c out/sdk-native/source/verified_copy.c out/sdk-native/source/aram.c -Wl,--gc-sections -lm -o /tmp/animaltown-model-winding && /tmp/animaltown-model-winding "$@"','model-winding')+$actorWindingParts)
}
# Validate admission peaks and sampled animation error with the exact shipped
# packages and pinned native loader, not nominal per-owner budget ceilings.
$lodAudit=Get-Content out/model-lod-audit.json -Raw|ConvertFrom-Json
$modelPeakArguments=@('--catalog','out/disc/villager-memory.bin','--max-bind-error',([double]$lodAudit.maximumBindError).ToString('R',[Globalization.CultureInfo]::InvariantCulture),'--max-pose-error',([double]$lodAudit.maximumPoseError).ToString('R',[Globalization.CultureInfo]::InvariantCulture))
foreach($species in @('fox','bunny','giraffe','cat','hamster','spaniel')){
    $modelPeakArguments+=@('--pair',"out/disc/$species-tex128.dndmodel","out/disc/$species-lod1-tex128.dndmodel","out/$species-lod1-map.bin")
}
RunNative '.' 'sh' (@('-c','cc -DANIMALTOWN_MODEL_PEAK_RETAIN -std=c11 -O2 -Wall -Wextra -Werror -ffunction-sections -fdata-sections -Iout/sdk-native/include tests/native-model-memory-peaks.c out/sdk-native/source/model.c out/sdk-native/source/model_animation.c out/sdk-native/source/verified_copy.c out/sdk-native/source/aram.c -Wl,--gc-sections,--wrap=malloc,--wrap=calloc,--wrap=realloc,--wrap=aligned_alloc,--wrap=free -lm -o out/native-model-memory-peaks && out/native-model-memory-peaks "$@" > out/model-native-peak-audit.jsonl','model-memory-peaks')+$modelPeakArguments)
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
$pairingProperty="-p:AnimalTownOnlinePairing=$($OnlinePairing.IsPresent.ToString().ToLowerInvariant())"
Run dotnet @('build','game/AnimalTown.GameCube','-c','Release','--no-restore','-v','quiet','-p:DepotProfile=false',$pairingProperty)
Run dotnet @("$sdkRoot/tools/compiler/DolphinDotNet.Compiler.dll",'--bundle','game/AnimalTown.GameCube/bin/Release/net8.0/AnimalTown.GameCube.dll','out/native')
VerifyNativeNetworking 'out/native'
& (Join-Path $PSScriptRoot 'patch-native.ps1') -Root $projectRoot -SkipTests:(!$testPatches)
if($testBuildings){
    RunNative '.' 'sh' (@('-c','cc -DANIMALTOWN_RETAIN_BUILDINGS -std=c11 -Wall -Wextra -Werror -ffunction-sections -fdata-sections -Iout/native/include tests/native-building-assets.c out/native/source/model.c out/native/source/model_animation.c out/native/source/verified_copy.c out/native/source/aram.c -Wl,--gc-sections -lm -o /tmp/animaltown-building-retained && /tmp/animaltown-building-retained "$@"','building-retained')+$buildingResidentParts)
}
if($testModels){
    $residentNativeModels=@(@('fox','bunny','giraffe','cat','hamster','spaniel')|ForEach-Object{"out/disc/$($_)-tex128.dndmodel"})
    RunNative '.' 'sh' (@('-c','cc -std=c11 -Wall -Wextra -Werror -ffunction-sections -fdata-sections -Iout/native/include tests/native-resident-models.c out/native/source/model.c out/native/source/model_animation.c out/native/source/verified_copy.c out/native/source/aram.c -Wl,--gc-sections -lm -o /tmp/animaltown-resident-models && /tmp/animaltown-resident-models "$@"','resident-models')+$residentNativeModels)
}
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
Run dotnet @('build','game/AnimalTown.GameCube','-c','Release','--no-restore','-v','quiet','-p:DepotProfile=true',$pairingProperty)
Run dotnet @("$sdkRoot/tools/compiler/DolphinDotNet.Compiler.dll",'--bundle','game/AnimalTown.GameCube/bin/Release/net8.0/AnimalTown.GameCube.dll','out/native-depot')
VerifyNativeNetworking 'out/native-depot'
& (Join-Path $PSScriptRoot 'patch-native.ps1') -Root $projectRoot -NativeDirectory 'out/native-depot' -SkipTests:(!$testPatches -or !$validation.full)
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
$networkingNotices=@{
    'mbedTLS-LICENSE.txt'='build/tls/source/LICENSE'
    'Jitterentropy-LICENSE.txt'='source/jitterentropy/LICENSE'
}
foreach($name in $networkingNotices.Keys){
    $noticePath=Join-Path $notices $name
    $discPath=Join-Path $projectRoot "out/disc/$name"
    if($OnlinePairing){
        Copy-Item -LiteralPath (Join-Path $nativeOutput $networkingNotices[$name]) -Destination $noticePath -Force
        Copy-Item -LiteralPath $noticePath -Destination $discPath -Force
    }else{
        foreach($path in @($noticePath,$discPath)){
            if(Test-Path -LiteralPath $path -PathType Leaf){Remove-Item -LiteralPath $path -Force}
        }
    }
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/dolphin-depot-install.txt') -Destination (Join-Path $notices 'DEPOT-README.txt') -Force
$thirdParty="DolphinDotNet: $sdkCommit`nHypercube: $engineCommit`nBoth declare GPL-2.0-only; full texts accompany this package.`nCorresponding dependency sources: https://github.com/capz/DolphinDotNet and https://github.com/capz/hypercube-engine at the commits above.`nRyu floating-point conversion: Boost Software License 1.0, Ryu-LICENSE.txt.`nGorditas font: SIL Open Font License 1.1, Gorditas-OFL.txt.`nGame sources and asset provenance: https://github.com/capz/AnimalTown/tree/$gameFullCommit`nOriginal AnimalTown code/assets have no blanket redistribution permission established by this packaging feature. See docs/depot-release.md in that source revision. A NOASSERTION catalog draft is not permission to redistribute.`n"
Copy-Item -LiteralPath (Join-Path $projectRoot 'assets/definitions/jobs/UNICODE-LICENSE.txt') -Destination (Join-Path $notices 'Unicode-LICENSE.txt') -Force
$thirdParty+="Unicode normalization and category data: Unicode 14.0.0, Unicode-LICENSE.txt.`n"
$thirdParty+="ufbx model importer: see ufbx-LICENSE.txt.`n"
if($OnlinePairing){
    $thirdParty+="Mbed TLS 3.6.5: see mbedTLS-LICENSE.txt. Source: https://github.com/Mbed-TLS/mbedtls/tree/e185d7fd85499c8ce5ca2a54f5cf8fe7dbe3f8df.`n"
    $thirdParty+="Jitterentropy 3.7.0: see Jitterentropy-LICENSE.txt and source/jitterentropy at the SDK revision above.`n"
}
[IO.File]::WriteAllText((Join-Path $notices 'THIRD-PARTY-NOTICES.txt'),$thirdParty)
Copy-Item -LiteralPath (Join-Path $notices 'THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $projectRoot 'out/disc') -Force
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
