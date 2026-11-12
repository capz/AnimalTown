param([string]$BaseRef='',[switch]$Full,[switch]$ForceRelease,[switch]$WorkingTree,[string]$OutputPath='out/validation-plan.json',[string]$GitHubOutput='')
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
Set-Location (Split-Path $PSScriptRoot -Parent)
. ./tools/validation-plan.ps1
. ./tools/version.ps1
$commit=(& git rev-parse HEAD).Trim()
if(!$Full -and !$BaseRef){$anchor=if($WorkingTree){'HEAD'}else{'HEAD^'};$BaseRef=Get-ReleaseAnchor (Get-Location).Path $anchor;if(!$BaseRef){$Full=$true;$BaseRef=''}}
$paths=@()
if(!$Full){
    & git merge-base --is-ancestor $BaseRef HEAD
    if($LASTEXITCODE){$Full=$true}else{$paths=@(& git diff --no-renames --name-only $BaseRef HEAD);if($LASTEXITCODE){throw 'Cannot determine changed paths'}}
}
if($WorkingTree){$paths+=@(& git diff --no-renames --name-only HEAD);if($LASTEXITCODE){throw 'Cannot inspect tracked working changes'};$paths+=@(& git ls-files --others --exclude-standard);if($LASTEXITCODE){throw 'Cannot inspect untracked changes'}}
$plan=Get-ValidationPlan -ChangedPaths @($paths|Sort-Object -Unique) -Full:$Full -ForceRelease:$ForceRelease -Commit $commit -BaseRef $BaseRef
$parent=Split-Path $OutputPath -Parent;if($parent){New-Item -ItemType Directory -Path $parent -Force|Out-Null}
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath,(Get-Location).Path),($plan|ConvertTo-Json -Depth 5)+"`n")
if($GitHubOutput){Add-Content -LiteralPath $GitHubOutput -Value ('release='+$plan.release.ToString().ToLowerInvariant())}
Write-Host "Validation: managed=$($plan.managed -join ',') rendering=$($plan.rendering -join ',') tooling=$($plan.tooling -join ',') native=$($plan.native -join ',') backend=$($plan.backend); release=$($plan.release)"
if($plan.policy){& ./tests/validation-plan.ps1;& ./tests/validation-selection.ps1;& ./tests/versioning.ps1}
$global:LASTEXITCODE=0
