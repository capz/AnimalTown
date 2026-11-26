$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$sourceRoot=Split-Path $PSScriptRoot -Parent
$originalLocation=Get-Location
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('animaltown-selection-'+[Guid]::NewGuid().ToString('N'))
function Assert($condition,[string]$message){if(!$condition){throw $message}}
function Invoke-FixtureGit([string[]]$arguments){
    $result=& git -C $fixture @arguments
    if($LASTEXITCODE){throw "Fixture git failed: $($arguments -join ' ')"}
    return $result
}
try{
    New-Item -ItemType Directory -Force -Path "$fixture/tools","$fixture/game/AnimalTown.GameCube","$fixture/docs"|Out-Null
    foreach($file in @('select-validation.ps1','validation-plan.ps1','version.ps1','test-changes.ps1')){
        Copy-Item (Join-Path $sourceRoot "tools/$file") "$fixture/tools/$file"
    }
    Invoke-FixtureGit @('init','-q','-b','main')|Out-Null
    Invoke-FixtureGit @('config','user.name','Validation fixture')
    Invoke-FixtureGit @('config','user.email','validation@example.invalid')
    Invoke-FixtureGit @('config','commit.gpgsign','false')
    Set-Content "$fixture/.gitignore" 'out/'
    Set-Content "$fixture/game/AnimalTown.GameCube/MenuNavigation.cs" '// original'
    Invoke-FixtureGit @('add','.')
    Invoke-FixtureGit @('commit','-qm','base')
    $base=(Invoke-FixtureGit @('rev-parse','HEAD')).Trim()
    Invoke-FixtureGit @('switch','-qc','feature')
    Set-Content "$fixture/game/AnimalTown.GameCube/MenuNavigation.cs" '// menu change'
    Invoke-FixtureGit @('commit','-qam','first change')
    Set-Content "$fixture/game/AnimalTown.GameCube/WrappedText.cs" '// text change'
    Invoke-FixtureGit @('add','.')
    Invoke-FixtureGit @('commit','-qm','second change')
    & "$fixture/tools/select-validation.ps1" -BaseRef $base
    $plan=Get-Content "$fixture/out/validation-plan.json" -Raw|ConvertFrom-Json
    Assert (($plan.managed -join ',') -eq 'menu,text') 'Selection lost an earlier commit in the change set'
    Assert ($plan.baseRef -eq $base -and $plan.commit -eq (Invoke-FixtureGit @('rev-parse','HEAD'))) 'Plan identity must record the actual comparison'
    Assert ($plan.schema -eq 3 -and $null -ne $plan.PSObject.Properties['emulator'] -and
        $null -ne $plan.PSObject.Properties['resources']) 'Serialized plan must include emulator and resource selection'

    # Both sides of a rename must contribute dependencies.
    Invoke-FixtureGit @('mv','game/AnimalTown.GameCube/MenuNavigation.cs','game/AnimalTown.GameCube/UiShapePlan.cs')
    & "$fixture/tools/select-validation.ps1" -BaseRef HEAD -WorkingTree
    $plan=Get-Content "$fixture/out/validation-plan.json" -Raw|ConvertFrom-Json
    Assert (($plan.managed -join ',') -eq 'ui,menu') 'Staged rename must include old and new dependencies'
    Invoke-FixtureGit @('reset','--hard','-q','HEAD')

    Set-Content "$fixture/game/AnimalTown.GameCube/WaterMaterial.cs" '// untracked material'
    & "$fixture/tools/select-validation.ps1" -BaseRef HEAD -WorkingTree
    $plan=Get-Content "$fixture/out/validation-plan.json" -Raw|ConvertFrom-Json
    Assert (($plan.managed -join ',') -eq 'terrain,core' -and ($plan.rendering -join ',') -eq 'water') 'Local selection must include untracked production files'
    Remove-Item "$fixture/game/AnimalTown.GameCube/WaterMaterial.cs"

    & "$fixture/tools/select-validation.ps1" -BaseRef HEAD -WorkingTree
    $plan=Get-Content "$fixture/out/validation-plan.json" -Raw|ConvertFrom-Json
    Assert (!$plan.release -and !$plan.full -and !$plan.paths.Count -and !$plan.managed.Count -and
        !$plan.rendering.Count -and !$plan.native.Count -and !$plan.tooling.Count -and !$plan.backend -and
        !$plan.emulator.Count -and !$plan.resources.Count) 'An empty working-tree comparison must schedule no component or emulator checks'
    $plan.schema=2
    $oldPlan="$fixture/out/obsolete-validation-plan.json"
    Set-Content -LiteralPath $oldPlan ($plan|ConvertTo-Json -Depth 5)
    $rejected=$false
    try{& "$fixture/tools/test-changes.ps1" -ValidationPlan $oldPlan -PlanOnly}
    catch{if($_.ToString() -notmatch 'plan|schema'){throw};$rejected=$true}
    Assert $rejected 'A validation plan without authoritative emulator routing must be rejected'

    Set-Content "$fixture/game/AnimalTown.GameCube/TownGame.MailUi.cs" '// untracked mail feature'
    & "$fixture/tools/select-validation.ps1" -BaseRef HEAD -WorkingTree
    $plan=Get-Content "$fixture/out/validation-plan.json" -Raw|ConvertFrom-Json
    Assert (($plan.emulator -join ',') -eq 'job-mail') 'Working-tree selection must serialize the sole affected emulator capture'
    Remove-Item "$fixture/game/AnimalTown.GameCube/TownGame.MailUi.cs"

    # Record runner dispatch without restoring/building anything in the fixture.
    $originalDotnet=Get-Command dotnet
    & {
        $ValidationRecordedCommands=[Collections.Generic.List[string]]::new()
        function dotnet {
            $ValidationRecordedCommands.Add(($args -join '|'))
            $global:LASTEXITCODE=0
        }
        Set-Content "$fixture/docs/notes.md" 'Documentation only'
        & "$fixture/tools/test-changes.ps1" -BaseRef HEAD
        Assert ($ValidationRecordedCommands.Count -eq 0) 'Docs-only validation must not restore the game SDK or run dotnet'
        Set-Content "$fixture/game/AnimalTown.GameCube/MenuNavigation.cs" '// local menu change'
        & "$fixture/tools/test-changes.ps1" -BaseRef HEAD
        Assert ($ValidationRecordedCommands.Count -eq 1 -and $ValidationRecordedCommands[0] -eq 'run|--project|tests/AnimalTown.Checks|-c|Release|--|--suites|menu') 'Menu validation must dispatch only its managed suite without SDK restore'
        $ValidationRecordedCommands.Clear()
        & "$fixture/tools/test-changes.ps1" -BaseRef HEAD -PlanOnly
        Assert ($ValidationRecordedCommands.Count -eq 0) 'PlanOnly must not execute component checks'
        Invoke-FixtureGit @('restore','game/AnimalTown.GameCube/MenuNavigation.cs')
        New-Item -ItemType Directory -Force -Path "$fixture/tests/AnimalTown.MemoryChecks"|Out-Null
        Set-Content "$fixture/tests/AnimalTown.MemoryChecks/Program.cs" '// memory fixture change'
        & "$fixture/tools/test-changes.ps1" -BaseRef HEAD
        Assert ($ValidationRecordedCommands.Count -eq 1 -and $ValidationRecordedCommands[0] -eq 'run|--project|tests/AnimalTown.MemoryChecks|-c|Release') 'A memory fixture must dispatch only its own resource check'
        $plan=Get-Content "$fixture/out/validation-plan.json" -Raw|ConvertFrom-Json
        Assert (!$plan.release -and !$plan.emulator.Count) 'A resource-only fixture must publish no build and schedule no emulator capture'
    }
    $restoredDotnet=Get-Command dotnet
    Assert ($restoredDotnet.CommandType -eq $originalDotnet.CommandType -and $restoredDotnet.Definition -eq $originalDotnet.Definition) 'Selection fixture must restore the real dotnet command before subsequent checks'
    Write-Host 'Validation selection integration checks passed.'
}finally{
    Set-Location $originalLocation
    Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
}
