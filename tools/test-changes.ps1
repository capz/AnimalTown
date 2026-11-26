param([string]$BaseRef='',[switch]$Full,[switch]$PlanOnly,[switch]$NativeInProcess,[string]$ValidationPlan='',[switch]$ConsoleCandidate)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
if(!$ValidationPlan){
    & ./tools/select-validation.ps1 -BaseRef $BaseRef -Full:$Full -WorkingTree
    $ValidationPlan='out/validation-plan.json'
}
$plan=Get-Content -LiteralPath $ValidationPlan -Raw|ConvertFrom-Json
if($plan.schema -ne 3 -or $plan.commit -ne (& git rev-parse HEAD).Trim()){throw 'Test plan must match this source commit'}
if($PlanOnly){return}
# Managed/backend/docs checks do not need a GameCube SDK restore or native copy.
if(@($plan.native | Where-Object {$_ -in @('texture','voices','shadows','patched','content','jobs')}).Count){. ./tools/resolve-packages.ps1}
function Run([string]$file,[string[]]$arguments){& $file @arguments;if($LASTEXITCODE){throw "$file checks failed"}}
if($plan.rendering.Count){Run dotnet @('run','--project','tests/AnimalTown.RenderingChecks','-c','Release','--','--suites',($plan.rendering -join ','))}
if($plan.managed.Count){Run dotnet @('run','--project','tests/AnimalTown.Checks','-c','Release','--','--suites',($plan.managed -join ','))}
$resourceProjects=@{
    memory='AnimalTown.MemoryChecks'
    floor='AnimalTown.FloorResidencyChecks'
    snow='AnimalTown.SnowResidencyChecks'
    visual='AnimalTown.VisualAllocationChecks'
}
foreach($suite in $plan.resources){
    if(!$resourceProjects.ContainsKey($suite)){throw "Unknown resource suite $suite"}
    Run dotnet @('run','--project',('tests/'+$resourceProjects[$suite]),'-c','Release')
}
if('card' -in $plan.native){Run dotnet @('run','--project','tests/AnimalTown.CardChecks','-c','Release')}
if('card' -in $plan.native){Run dotnet @('run','--project','vendor/card-store-sdk/tests/CardRecordStoreTests','-c','Release')}
if($plan.tooling.Count){
    Run dotnet @('build','tools/AnimalTown.Tools','-c','Release','-v','quiet')
    foreach($suite in $plan.tooling){Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--',($suite+'-check'),(Get-Location).Path)}
}
$commands=@{
    helipad='python3 tools/helipad-vector.py --check'
    jobs='cc -std=c11 -D_DEFAULT_SOURCE -Wall -Wextra -Werror tests/native-job-calendar.c -o /tmp/animaltown-job-calendar && /tmp/animaltown-job-calendar && python3 tests/native-jobs.py'
    content='python3 tests/native-content-startup.py'
    card='cc -std=c11 -Wall -Wextra -Werror tests/native-card-read.c -o /tmp/animaltown-card-read && /tmp/animaltown-card-read && cc -std=c11 -Wall -Wextra -Werror tests/native-card-mount.c -o /tmp/animaltown-card-mount && /tmp/animaltown-card-mount && cc -std=c11 -Wall -Wextra -Werror -Ivendor/card-store-sdk/include vendor/card-store-sdk/tests/card_blocks_test.c -o /tmp/animaltown-card-blocks && /tmp/animaltown-card-blocks'
    uv='cc -std=c11 -Wall -Wextra -Werror tests/native-uv-topology.c -o /tmp/animaltown-uv-topology && /tmp/animaltown-uv-topology'
    texture='cc -std=c11 -Wall -Wextra -Werror -Iout/sdk-native/include tests/native-texture-pages.c -o /tmp/animaltown-texture-pages && /tmp/animaltown-texture-pages'
    voices='cc -std=c11 -Wall -Wextra -Werror -Iout/sdk-native/include tests/native-dialogue-voices.c out/sdk-native/source/audio.c -lm -o /tmp/animaltown-dialogue-voices && /tmp/animaltown-dialogue-voices'
    'frame-profile'='python3 tests/frame-profile-streaming.py'
    'release-bundle'='python3 tests/release-bundle.py'
    'water-smoke'='python3 tests/water-optics-smoke.py'
    'path-study'='python3 -m py_compile tools/path-study.py'
    shadows='cc -std=c11 -Wall -Wextra -Werror -Iout/sdk-native/include tests/native-path-mask.c -o /tmp/animaltown-path-mask && /tmp/animaltown-path-mask && cc -std=c11 -Wall -Wextra -Werror tests/native-shadow-union.c -lm -o /tmp/animaltown-shadow-union && /tmp/animaltown-shadow-union && cc -std=c11 -Wall -Wextra -Werror -Iout/sdk-native/include tests/native-shadow-lifecycle.c -lm -o /tmp/animaltown-shadow-lifecycle && /tmp/animaltown-shadow-lifecycle'
}
if('content' -in $plan.native){
    foreach($contentSmokeProfile in @('depot','disc')){
        $isDepotSmoke=($contentSmokeProfile -eq 'depot').ToString().ToLowerInvariant()
        Run dotnet @('build','tests/AnimalTown.ContentSmoke','-c','Release',"-p:DepotProfile=$isDepotSmoke",'-p:RestoreLockedMode=true','-v','quiet')
        Run dotnet @("$sdkRoot/tools/compiler/DolphinDotNet.Compiler.dll",'--aot',"tests/AnimalTown.ContentSmoke/bin/Release/$contentSmokeProfile/net8.0/AnimalTown.ContentSmoke.dll","out/content-startup-$contentSmokeProfile.c")
    }
}
if('jobs' -in $plan.native){
    Run dotnet @('build','tests/AnimalTown.JobSmoke','-c','Release','-p:RestoreLockedMode=true','-v','quiet')
    Run dotnet @("$sdkRoot/tools/compiler/DolphinDotNet.Compiler.dll",'--aot','tests/AnimalTown.JobSmoke/bin/Release/net8.0/AnimalTown.JobSmoke.dll','out/job-smoke.c')
}
if('patched' -in $plan.native -and !$ConsoleCandidate){
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
Write-Host 'Selected host checks passed.'
