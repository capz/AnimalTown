param([string]$BaseRef='',[switch]$Full,[switch]$PlanOnly,[switch]$NativeInProcess)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
& ./tools/select-validation.ps1 -BaseRef $BaseRef -Full:$Full -WorkingTree
$plan=Get-Content out/validation-plan.json -Raw|ConvertFrom-Json
if($PlanOnly){return}
# Managed/backend/docs checks do not need a GameCube SDK restore or native copy.
if(@($plan.native | Where-Object {$_ -in @('texture','voices','shadows','patched','content')}).Count){. ./tools/resolve-packages.ps1}
function Run([string]$file,[string[]]$arguments){& $file @arguments;if($LASTEXITCODE){throw "$file checks failed"}}
if($plan.rendering.Count){Run dotnet @('run','--project','tests/AnimalTown.RenderingChecks','-c','Release','--','--suites',($plan.rendering -join ','))}
if($plan.managed.Count){Run dotnet @('run','--project','tests/AnimalTown.Checks','-c','Release','--','--suites',($plan.managed -join ','))}
if('card' -in $plan.native){Run dotnet @('run','--project','tests/AnimalTown.CardChecks','-c','Release')}
if('card' -in $plan.native){Run dotnet @('run','--project','vendor/card-store-sdk/tests/CardRecordStoreTests','-c','Release')}
if($plan.tooling.Count){
    Run dotnet @('build','tools/AnimalTown.Tools','-c','Release','-v','quiet')
    foreach($suite in $plan.tooling){Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--',($suite+'-check'),(Get-Location).Path)}
}
$commands=@{
    content='python3 tests/native-content-startup.py'
    card='cc -std=c11 -Wall -Wextra -Werror tests/native-card-read.c -o /tmp/animaltown-card-read && /tmp/animaltown-card-read && cc -std=c11 -Wall -Wextra -Werror tests/native-card-mount.c -o /tmp/animaltown-card-mount && /tmp/animaltown-card-mount && cc -std=c11 -Wall -Wextra -Werror -Ivendor/card-store-sdk/include vendor/card-store-sdk/tests/card_blocks_test.c -o /tmp/animaltown-card-blocks && /tmp/animaltown-card-blocks'
    uv='cc -std=c11 -Wall -Wextra -Werror tests/native-uv-topology.c -o /tmp/animaltown-uv-topology && /tmp/animaltown-uv-topology'
    texture='cc -std=c11 -Wall -Wextra -Werror -Iout/sdk-native/include tests/native-texture-pages.c -o /tmp/animaltown-texture-pages && /tmp/animaltown-texture-pages'
    voices='cc -std=c11 -Wall -Wextra -Werror -Iout/sdk-native/include tests/native-dialogue-voices.c out/sdk-native/source/audio.c -lm -o /tmp/animaltown-dialogue-voices && /tmp/animaltown-dialogue-voices'
    python='python3 tests/frame-profile-streaming.py'
    shadows='cc -std=c11 -Wall -Wextra -Werror -Iout/sdk-native/include tests/native-path-mask.c -o /tmp/animaltown-path-mask && /tmp/animaltown-path-mask && cc -std=c11 -Wall -Wextra -Werror tests/native-shadow-union.c -lm -o /tmp/animaltown-shadow-union && /tmp/animaltown-shadow-union && cc -std=c11 -Wall -Wextra -Werror -Iout/sdk-native/include tests/native-shadow-lifecycle.c -lm -o /tmp/animaltown-shadow-lifecycle && /tmp/animaltown-shadow-lifecycle'
}
if('content' -in $plan.native){
    Run dotnet @('build','tests/AnimalTown.ContentSmoke','-c','Release','-p:RestoreLockedMode=true','-v','quiet')
    Run dotnet @("$sdkRoot/tools/compiler/DolphinDotNet.Compiler.dll",'--aot','tests/AnimalTown.ContentSmoke/bin/Release/net8.0/AnimalTown.ContentSmoke.dll','out/content-startup.c')
}
if('patched' -in $plan.native){
    # Generate fresh native sources so stale output cannot hide a patch regression.
    # This bundles C sources and runs host fixtures; it does not build a console ISO.
    Run dotnet @('build','game/AnimalTown.GameCube','-c','Release','--no-restore')
    Run dotnet @("$sdkRoot/tools/compiler/DolphinDotNet.Compiler.dll",'--bundle','game/AnimalTown.GameCube/bin/Release/net8.0/AnimalTown.GameCube.dll','out/native')
    if($NativeInProcess){& ./tools/patch-native.ps1 -Root (Get-Location).Path}
    else{
        $image=if($env:ANIMALTOWN_BUILD_IMAGE){$env:ANIMALTOWN_BUILD_IMAGE}else{'animaltown-builder:local'}
        Run docker @('run','--rm','--mount',"type=bind,src=$((Get-Location).Path),dst=/work",'-w','/work',$image,'pwsh','-NoProfile','-File','tools/patch-native.ps1','-Root','/work')
    }
}
foreach($suite in @($plan.native | Where-Object {$_ -ne 'patched'})){
    if($NativeInProcess){Run sh @('-c',$commands[$suite])}
    else{$image=if($env:ANIMALTOWN_BUILD_IMAGE){$env:ANIMALTOWN_BUILD_IMAGE}else{'animaltown-builder:local'};Run docker @('run','--rm','--mount',"type=bind,src=$((Get-Location).Path),dst=/work",'-w','/work',$image,'sh','-c',$commands[$suite])}
}
if($plan.backend){& ./tests/server-integration.ps1 -DatabaseConnection $env:ANIMALTOWN_TEST_CONNECTION}
Write-Host 'Selected change checks passed. No console build was requested.'
