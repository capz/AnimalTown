param([switch]$SkipAssets,[string]$SdkRootOverride='',[switch]$NativeInProcess,[string]$ValidationPlan='')
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
if($validation.schema -ne 1 -or $validation.commit -ne (& git rev-parse HEAD).Trim()){throw 'Validation plan must match this source commit'}
if($NativeInProcess -and !$IsLinux){throw 'NativeInProcess requires the Linux build image'}
$sdkRoot = if($SdkRootOverride){(Resolve-Path -LiteralPath $SdkRootOverride).Path}else{Join-Path $projectRoot 'vendor/sdk'}
if (!(Test-Path "$sdkRoot/tools/DolphinDotNet.Compiler")) { throw 'Run git submodule update --init --recursive first.' }
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
$assetTools = "$sdkRoot/tools/DolphinDotNet.Assets/bin/Release/net8.0/DolphinDotNet.Assets.dll"
Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','buildings',$projectRoot)
Run dotnet @('build', "$sdkRoot/tools/DolphinDotNet.Assets", '-c', 'Release', '-v', 'quiet')
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
    foreach($key in $buildingImports.Keys){$assetMap[$key]=$buildingImports[$key].Substring(14)}
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
            Copy-Item -LiteralPath "assets/models/$($assetMap[$name])" -Destination "out/prepared/$name.glb" -Force
        }else{
            Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','prepare',$projectRoot,"assets/models/$($assetMap[$name])","out/prepared/$name.glb")
        }
        foreach($textureEdge in @(128,256,512,1024)){
        $suffix=if($textureEdge -eq 256){''}else{"-tex$textureEdge"}
        # DNMD retains its package and charges a decoded RGBA8 copy of each
        # texture per model, even when the native texture cache shares pixels.
        # A 64px atlas + 32px mip keeps all seven static parts below 768 KiB.
        $importEdge=if($buildingImports.ContainsKey($name) -and $textureEdge -eq 128){64}else{$textureEdge}
        $importArguments=@($assetTools,'model',"out/prepared/$name.glb","out/disc/$name$suffix.dndmodel",'--texture-limit',"$importEdge",'--mip-levels','2','--compile-gx-materials')
        if($assetMap[$name].StartsWith('villagers/villager_')){$importArguments+='--allow-material-approximation'}
        Run dotnet $importArguments
        }
    }
}
$buildingNativeParts=@((Get-Content out/building-assets.json -Raw|ConvertFrom-Json -AsHashtable).Keys|Sort-Object|ForEach-Object{"out/disc/$($_)-tex128.dndmodel"})
RunNative '.' 'sh' (@('-c','cc -std=c11 -Wall -Wextra -Werror -ffunction-sections -fdata-sections -Ivendor/sdk/include tests/native-building-assets.c vendor/sdk/source/model.c vendor/sdk/source/model_animation.c -Wl,--gc-sections -lm -o /tmp/animaltown-building-assets && /tmp/animaltown-building-assets "$@"','building-assets')+$buildingNativeParts)
$gameCommit = $identity.bb
$gameFullCommit = $identity.commit
$gameVersion = $identity.version
$workingChanges = & git status --porcelain --untracked-files=normal
if ($workingChanges) { $gameCommit += '-dirty'; $gameVersion += '-dirty' }
$buildSource = "internal static class BuildInfo { internal const string Commit=`"$gameCommit`"; internal const string Version=`"$gameVersion`"; }"
[IO.File]::WriteAllText((Join-Path $projectRoot 'out/BuildInfo.cs'), $buildSource)
$engineCommit = (& git -C vendor/hypercube-engine rev-parse HEAD).Trim()
$sdkCommit = (& git -C $sdkRoot rev-parse HEAD).Trim()
$sourceManifest = foreach($sourceFile in (& git ls-files --cached --others --exclude-standard | Sort-Object)) {
    if(Test-Path -LiteralPath $sourceFile -PathType Leaf){ "$sourceFile $((Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash)" }
}
$sourceDigest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($sourceManifest -join "`n"))))
[IO.File]::WriteAllText((Join-Path $projectRoot 'out/build-revisions.txt'), "Version: $($identity.version)`nGame source: $gameFullCommit`nEngine: $engineCommit`nSDK: $sdkCommit`nDirty: $([bool]$workingChanges)`nSource digest: $sourceDigest`nBuild image: $env:ANIMALTOWN_BUILD_IMAGE`n")
if($validation.managed.Count){Run dotnet @('run','--project','tests/AnimalTown.Checks','-c','Release','--','--suites',($validation.managed -join ','))}
if('card' -in $validation.native){RunNative '.' 'sh' @('-c','cc -std=c11 -Wall -Wextra -Werror tests/native-card-read.c -o /tmp/animaltown-card-read && /tmp/animaltown-card-read')}
if('uv' -in $validation.native){RunNative '.' 'sh' @('-c','cc -std=c11 -Wall -Wextra -Werror tests/native-uv-topology.c -o /tmp/animaltown-uv-topology && /tmp/animaltown-uv-topology')}
if('texture' -in $validation.native){RunNative '.' 'sh' @('-c','cc -std=c11 -Wall -Wextra -Werror -Ivendor/sdk/include tests/native-texture-pages.c -o /tmp/animaltown-texture-pages && /tmp/animaltown-texture-pages')}
if('voices' -in $validation.native){RunNative '.' 'sh' @('-c','cc -std=c11 -Wall -Wextra -Werror -Ivendor/sdk/include tests/native-dialogue-voices.c vendor/sdk/source/audio.c -lm -o /tmp/animaltown-dialogue-voices && /tmp/animaltown-dialogue-voices')}
if('python' -in $validation.native){RunNative '.' 'python3' @('-c','import ast; ast.parse(open("tools/iso-smoke.py").read())')}
Run dotnet @('build',"$sdkRoot/tools/DolphinDotNet.Compiler",'-c','Release','-v','quiet')
Run dotnet @('build','game/AnimalTown.GameCube','-c','Release',"-p:DolphinDotNetRoot=$sdkRoot",'-v','quiet')
Run dotnet @("$sdkRoot/tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll",'--bundle','game/AnimalTown.GameCube/bin/Release/net8.0/AnimalTown.GameCube.dll','out/native')
& (Join-Path $PSScriptRoot 'patch-native.ps1') -Root $projectRoot
$nativeOutput=[IO.Path]::GetFullPath((Join-Path $projectRoot 'out/native'))
$workspacePrefix=$projectRoot.TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
if(!$nativeOutput.StartsWith($workspacePrefix,[StringComparison]::OrdinalIgnoreCase)){throw 'Native output must stay inside the workspace'}
$nativeWorkingRoot=if($NativeInProcess){$nativeOutput}else{'/project/out/native'}
$nativeKey="$nativeWorkingRoot|$nativeImage|$env:ANIMALTOWN_BUILD_IMAGE"
$nativeKeyFile=Join-Path $nativeOutput '.build-root'
# Make dependency files contain absolute mount paths. Never reuse objects from
# the host-native /project mount inside the combined /workspace image (or vice versa).
if((Test-Path -LiteralPath (Join-Path $nativeOutput 'build')) -and
   (!(Test-Path -LiteralPath $nativeKeyFile) -or [IO.File]::ReadAllText($nativeKeyFile) -ne $nativeKey)){
    RunNative 'out/native' 'make' @('clean')
}
[IO.File]::WriteAllText($nativeKeyFile,$nativeKey)
RunNative 'out/native' 'make' @('-j2')
$loaderBuild = 'set -eu
export PATH=/opt/devkitpro/devkitPPC/bin:$PATH
powerpc-eabi-gcc -mcpu=750 -m32 -mbig-endian -msdata=none -Os -fno-builtin -fno-pic -fno-asynchronous-unwind-tables -ffunction-sections -nostdlib -Wl,-T,tools/apploader/link.ld tools/apploader/apploader.c -lgcc -o out/apploader.elf
powerpc-eabi-objcopy -O binary out/apploader.elf out/apploader.bin
'
RunNative '.' 'sh' @('-c',$loaderBuild.Replace("`r`n","`n"))
Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','iso',$projectRoot,'out/native/Game.dol',$gameVersion)
Write-Host "Bootable ISO: $projectRoot/out/AnimalTown-$gameVersion.iso"
