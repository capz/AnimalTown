param(
    [ValidateSet('all','tests','checks','build','game','server','package','publish','release')][string]$Target='all'
)
$ErrorActionPreference='Stop'
if(!$IsLinux){throw 'Run this entry point inside the Linux build image'}
Set-Location (Split-Path $PSScriptRoot -Parent)
$plan='out/validation-plan.json'
if($Target -in @('all','release','tests','checks','game')){
    if(!(Test-Path $plan)){& ./tools/select-validation.ps1 -Full -SkipPolicyChecks:($Target -eq 'game')}
    $validation=Get-Content -LiteralPath $plan -Raw|ConvertFrom-Json
    if($validation.schema -ne 3 -or $validation.commit -ne (& git rev-parse HEAD).Trim()){throw 'Stale validation plan; regenerate it for this commit'}
    $runTests=$Target -ne 'game'
    if($runTests){
        if($validation.backend -and !$env:ANIMALTOWN_TEST_CONNECTION){throw 'A disposable PostgreSQL test database is required; use build/compose.yml'}
        & ./tools/test-changes.ps1 -NativeInProcess -ValidationPlan $plan -ConsoleCandidate:($Target -ne 'checks')
    }
    if($Target -ne 'checks'){& ./tools/build-game.ps1 -NativeInProcess -ValidationPlan $plan -RunNativeTests:$runTests}
}
if($Target -in @('all','release','build','server')){& ./tools/build-server.ps1}
if($Target -in @('all','release','build','package')){& ./tools/publish-release.ps1 -PackageOnly}
if($Target -in @('publish','release')){& ./tools/publish-release.ps1 -PublishOnly}
