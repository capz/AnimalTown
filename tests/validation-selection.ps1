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
    }
    $restoredDotnet=Get-Command dotnet
    Assert ($restoredDotnet.CommandType -eq $originalDotnet.CommandType -and $restoredDotnet.Definition -eq $originalDotnet.Definition) 'Selection fixture must restore the real dotnet command before subsequent checks'
    Write-Host 'Validation selection integration checks passed (8 cases).'
}finally{
    Set-Location $originalLocation
    Remove-Item -LiteralPath $fixture -Recurse -Force -ErrorAction SilentlyContinue
}
