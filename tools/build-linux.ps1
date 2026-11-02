param(
    [ValidateSet('all','game','server','package','publish','release')][string]$Target='all'
)
$ErrorActionPreference='Stop'
if(!$IsLinux){throw 'Run this entry point inside the Linux build image'}
Set-Location (Split-Path $PSScriptRoot -Parent)
if($Target -in @('all','release') -and !$env:ANIMALTOWN_TEST_CONNECTION){throw 'A disposable PostgreSQL test database is required; use build/compose.yml'}
if($Target -in @('all','release','game')){& ./tools/build-game.ps1 -NativeInProcess}
if($Target -in @('all','release')){& ./tests/server-integration.ps1 -DatabaseConnection $env:ANIMALTOWN_TEST_CONNECTION}
elseif($Target -eq 'server'){& ./tools/build-server.ps1}
if($Target -in @('all','package')){& ./tools/publish-release.ps1 -PackageOnly}
elseif($Target -in @('publish','release')){& ./tools/publish-release.ps1}
