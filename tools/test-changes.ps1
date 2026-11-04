param([string]$BaseRef='',[switch]$Full,[switch]$PlanOnly,[switch]$NativeInProcess)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
& ./tools/select-validation.ps1 -BaseRef $BaseRef -Full:$Full -WorkingTree
$plan=Get-Content out/validation-plan.json -Raw|ConvertFrom-Json
if($PlanOnly){return}
function Run([string]$file,[string[]]$arguments){& $file @arguments;if($LASTEXITCODE){throw "$file checks failed"}}
if($plan.managed.Count){Run dotnet @('run','--project','tests/AnimalTown.Checks','-c','Release','--','--suites',($plan.managed -join ','))}
if($plan.tooling.Count){
    Run dotnet @('build','tools/AnimalTown.Tools','-c','Release','-v','quiet')
    foreach($suite in $plan.tooling){Run dotnet @('run','--project','tools/AnimalTown.Tools','-c','Release','--no-build','--',($suite+'-check'),(Get-Location).Path)}
}
$commands=@{
    card='cc -std=c11 -Wall -Wextra -Werror tests/native-card-read.c -o /tmp/animaltown-card-read && /tmp/animaltown-card-read'
    uv='cc -std=c11 -Wall -Wextra -Werror tests/native-uv-topology.c -o /tmp/animaltown-uv-topology && /tmp/animaltown-uv-topology'
    texture='cc -std=c11 -Wall -Wextra -Werror -Ivendor/sdk/include tests/native-texture-pages.c -o /tmp/animaltown-texture-pages && /tmp/animaltown-texture-pages'
    voices='cc -std=c11 -Wall -Wextra -Werror -Ivendor/sdk/include tests/native-dialogue-voices.c vendor/sdk/source/audio.c -lm -o /tmp/animaltown-dialogue-voices && /tmp/animaltown-dialogue-voices'
    python='python3 -c ''import ast; ast.parse(open("tools/iso-smoke.py").read())'''
}
foreach($suite in $plan.native){
    if($NativeInProcess){Run sh @('-c',$commands[$suite])}
    else{$image=if($env:ANIMALTOWN_BUILD_IMAGE){$env:ANIMALTOWN_BUILD_IMAGE}else{'animaltown-builder:local'};Run docker @('run','--rm','--mount',"type=bind,src=$((Get-Location).Path),dst=/work",'-w','/work',$image,'sh','-c',$commands[$suite])}
}
if($plan.backend){& ./tests/server-integration.ps1}
Write-Host 'Selected change checks passed. No console build was requested.'
