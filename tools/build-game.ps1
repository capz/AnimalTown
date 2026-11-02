param([switch]$SkipAssets,[string]$SdkRootOverride='',[switch]$NativeInProcess)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
function Run([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed ($LASTEXITCODE)" }
}
$projectRoot = (Get-Location).Path
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
Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','terrain-check',$projectRoot)
$assetTools = "$sdkRoot/tools/DolphinDotNet.Assets/bin/Release/net8.0/DolphinDotNet.Assets.dll"
Run dotnet @('build', "$sdkRoot/tools/DolphinDotNet.Assets", '-c', 'Release', '-v', 'quiet')
Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','audit',$projectRoot)
if (!$SkipAssets) {
    $assetMap = [ordered]@{
        fox='villagers/Meshy_AI_Rigged_Fox_All_Animations.glb'
        gecko='villagers/Meshy_AI_Rigged_Gecko_All_Animations.glb'
        giraffe='villagers/Meshy_AI_Rigged_Giraffe_All_Animations.glb'
        cat='villagers/Meshy_AI_Rigged_Maine_Coon_All_Animations.glb'
        prairie='villagers/Meshy_AI_Rigged_Prairie_Dog_All_Animations.glb'
        spaniel='villagers/Meshy_AI_Rigged_Spaniel_All_Animations.glb'
        building='world-stuctures/Meshy_AI_animal_crossing_konbi_1102122741_texture.glb'
        deciduous='world-stuctures/Meshy_AI_ac_deciduous_tree_3d_1102122647_texture.glb'
        palm='world-stuctures/Meshy_AI_ac_palm_tree_3d_1102122704_texture.glb'
        pine='world-stuctures/Meshy_AI_ac_pine_tree_3d_1102122723_texture.glb'
    }
    foreach ($name in $assetMap.Keys) {
        Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','prepare',$projectRoot,"assets/models/$($assetMap[$name])","out/prepared/$name.glb")
        Run dotnet @($assetTools,'model',"out/prepared/$name.glb","out/disc/$name.dndmodel",'--texture-limit','128','--compile-gx-materials')
    }
}
$gameCommit = (& git rev-parse --short=12 HEAD).Trim()
$gameFullCommit = (& git rev-parse HEAD).Trim()
$workingChanges = & git status --porcelain --untracked-files=normal
if ($workingChanges) { $gameCommit += '-dirty' }
$buildSource = "internal static class BuildInfo { internal const string Commit=`"$gameCommit`"; }"
[IO.File]::WriteAllText((Join-Path $projectRoot 'out/BuildInfo.cs'), $buildSource)
$engineCommit = (& git -C vendor/hypercube-engine rev-parse HEAD).Trim()
$sdkCommit = (& git -C $sdkRoot rev-parse HEAD).Trim()
$sourceManifest = foreach($sourceFile in (& git ls-files --cached --others --exclude-standard | Sort-Object)) {
    if(Test-Path -LiteralPath $sourceFile -PathType Leaf){ "$sourceFile $((Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash)" }
}
$sourceDigest=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($sourceManifest -join "`n"))))
[IO.File]::WriteAllText((Join-Path $projectRoot 'out/build-revisions.txt'), "Game source: $gameFullCommit`nEngine: $engineCommit`nSDK: $sdkCommit`nDirty: $([bool]$workingChanges)`nSource digest: $sourceDigest`nBuild image: $env:ANIMALTOWN_BUILD_IMAGE`n")
Run dotnet @('run','--project','tests/AnimalTown.Checks','-c','Release')
RunNative '.' 'sh' @('-c','cc -std=c11 -Wall -Wextra -Werror tests/native-card-read.c -o /tmp/animaltown-card-read && /tmp/animaltown-card-read')
Run dotnet @('build',"$sdkRoot/tools/DolphinDotNet.Compiler",'-c','Release','-v','quiet')
Run dotnet @('build','game/AnimalTown.GameCube','-c','Release',"-p:DolphinDotNetRoot=$sdkRoot",'-v','quiet')
Run dotnet @("$sdkRoot/tools/DolphinDotNet.Compiler/bin/Release/net8.0/DolphinDotNet.Compiler.dll",'--bundle','game/AnimalTown.GameCube/bin/Release/net8.0/AnimalTown.GameCube.dll','out/native')
& (Join-Path $PSScriptRoot 'patch-native.ps1') -Root $projectRoot
RunNative 'out/native' 'make' @('-j2')
$loaderBuild = 'set -eu
export PATH=/opt/devkitpro/devkitPPC/bin:$PATH
powerpc-eabi-gcc -mcpu=750 -m32 -mbig-endian -msdata=none -Os -fno-builtin -fno-pic -fno-asynchronous-unwind-tables -ffunction-sections -nostdlib -Wl,-T,tools/apploader/link.ld tools/apploader/apploader.c -lgcc -o out/apploader.elf
powerpc-eabi-objcopy -O binary out/apploader.elf out/apploader.bin
'
RunNative '.' 'sh' @('-c',$loaderBuild.Replace("`r`n","`n"))
Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--','iso',$projectRoot,'out/native/Game.dol',$gameCommit)
Write-Host "Bootable ISO: $projectRoot/out/AnimalTown-$gameCommit.iso"
