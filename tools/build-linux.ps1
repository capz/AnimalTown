param(
    [ValidateSet('all','game','server','package','publish','release')][string]$Target='all'
)
$ErrorActionPreference='Stop'
if(!$IsLinux){throw 'Run this entry point inside the Linux build image'}
Set-Location (Split-Path $PSScriptRoot -Parent)
if($Target -in @('all','release','game')){
    if(!(Test-Path out/validation-plan.json)){& ./tools/select-validation.ps1 -Full}
    $validation=Get-Content out/validation-plan.json -Raw|ConvertFrom-Json
    if($validation.schema -ne 2 -or $validation.commit -ne (& git rev-parse HEAD).Trim()){throw 'Stale validation plan; regenerate it for this commit'}
    if($Target -in @('all','release') -and $validation.backend -and !$env:ANIMALTOWN_TEST_CONNECTION){throw 'A disposable PostgreSQL test database is required; use build/compose.yml'}
    & ./tools/build-game.ps1 -NativeInProcess -ValidationPlan out/validation-plan.json
}
if($Target -in @('all','release')){if($validation.backend){& ./tests/server-integration.ps1 -DatabaseConnection $env:ANIMALTOWN_TEST_CONNECTION}else{& ./tools/build-server.ps1}}
elseif($Target -eq 'server'){& ./tools/build-server.ps1}
if($Target -in @('all','package')){& ./tools/publish-release.ps1 -PackageOnly}
elseif($Target -in @('publish','release')){& ./tools/publish-release.ps1}
