$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../tools/validation-plan.ps1')
function Assert($value,[string]$message){if(!$value){throw $message}}
$p=Get-ValidationPlan -ChangedPaths @('docs/concepts/cliff-kit.png','README.md');Assert (!$p.release -and !$p.backend -and !$p.managed.Count) 'Art/docs should not trigger builds/tests'
$p=Get-ValidationPlan -ChangedPaths @('game/AnimalTown.GameCube/MenuNavigation.cs');Assert (($p.managed -join ',') -eq 'menu' -and !$p.backend -and !$p.tooling.Count) 'Menu routing too broad'
$p=Get-ValidationPlan -ChangedPaths @('tools/native-uv-debug.inc','tools/native-uv-topology.h');Assert (($p.native -join ',') -eq 'uv' -and !$p.managed.Count -and !$p.backend) 'UV native routing too broad'
$p=Get-ValidationPlan -ChangedPaths @('server/AnimalTown.Server/Program.cs');Assert ($p.backend -and !$p.managed.Count -and !$p.native.Count) 'Backend should not run console unit suites'
$p=Get-ValidationPlan -ChangedPaths @('assets/models/villagers/fox.glb');Assert ($p.release -and !$p.managed.Count -and !$p.backend) 'Model imports should use import/build audits'
$p=Get-ValidationPlan -ChangedPaths @('assets/textures/environment/ground/test.png');Assert (($p.tooling -join ',') -eq 'terrain' -and ($p.managed -join ',') -eq 'core' -and !$p.backend) 'Ground routing incorrect'
$p=Get-ValidationPlan -ChangedPaths @('shared/AnimalTown.Domain/GameState.cs');Assert ($p.backend -and $p.managed.Count -eq 12 -and !$p.tooling.Count) 'Shared gameplay must cover backend and all managed dependencies'
$p=Get-ValidationPlan -ChangedPaths @('game/AnimalTown.GameCube/Program.cs');Assert ($p.managed.Count -eq 12 -and !$p.backend) 'Mixed game entry point needs managed coverage, not unrelated DB tests'
$p=Get-ValidationPlan -ChangedPaths @('tools/AnimalTown.Tools/TextureRepackerChecks.cs');Assert (($p.tooling -join ',') -eq 'repack' -and !$p.backend) 'Repacker routing incorrect'
$p=Get-ValidationPlan -ChangedPaths @('shared/AnimalTown.Domain/NewDependency.cs');Assert ($p.managed.Count -eq 12 -and $p.backend) 'Unknown domain must be conservative'
$p=Get-ValidationPlan -ChangedPaths @('new-production/file.xyz');Assert ($p.full -and $p.backend -and $p.native.Count -eq 7) 'Unknown production paths must fall back to all checks'
$p=Get-ValidationPlan -ChangedPaths @('tools/native-shadow-union.h','tests/native-shadow-union.c');Assert (($p.native -join ',') -eq 'shadows' -and ($p.managed -join ',') -eq 'terrain' -and !$p.backend) 'Shadow union must include the native rasterizer checks'
$p=Get-ValidationPlan -ChangedPaths @('.github/workflows/build-release.yml');Assert ($p.full -and $p.policy) 'Workflow changes must exercise gates'
$p=Get-ValidationPlan -Full;Assert ($p.full -and $p.release -and $p.managed.Count -eq 12 -and $p.tooling.Count -eq 5 -and 'nkit' -in $p.tooling) 'Manual full validation incomplete'
$p=Get-ValidationPlan -ChangedPaths @('game/AnimalTown.GameCube/MenuNavigation.cs','game/AnimalTown.GameCube/MenuNavigation.cs','game/AnimalTown.GameCube/WrappedText.cs');Assert (($p.managed -join ',') -eq 'menu,text') 'Union/deduplication incorrect'
$p=Get-ValidationPlan -ChangedPaths @('shared/AnimalTown.Domain/WaterSurface.cs','tests/AnimalTown.Checks/WaterSurfaceChecks.cs');Assert (($p.managed -join ',') -eq 'terrain' -and !$p.backend) 'Water rules use terrain checks'
$p=Get-ValidationPlan -ChangedPaths @('game/AnimalTown.GameCube/WaterMaterial.cs','game/AnimalTown.GameCube/WaterRenderer.cs');Assert (($p.managed -join ',') -eq 'terrain,core' -and !$p.backend) 'Water rendering needs terrain and core checks'
$p=Get-ValidationPlan -ChangedPaths @('shared/AnimalTown.Domain/PortalSystem.cs','assets/definitions/buildings/cottage_small.json');Assert ('buildings' -in $p.managed -and 'building' -in $p.tooling -and !$p.full) 'Building logic/assets need bounded component checks'
$p=Get-ValidationPlan -ChangedPaths @('shared/AnimalTown.Domain/RiverBankGeometry.cs','tests/AnimalTown.Checks/RiverBankChecks.cs');Assert (($p.managed -join ',') -eq 'terrain,movement,generation,life' -and !$p.backend -and !$p.full) 'River banks need geometry, movement and route checks'
$p=Get-ValidationPlan -ChangedPaths @('shared/AnimalTown.Domain/TerrainStream.cs');Assert (($p.managed -join ',') -eq 'terrain,movement,generation,life,core' -and !$p.backend -and !$p.full) 'Terrain streaming must exercise visibility, movement and generation checks'
$p=Get-ValidationPlan -ChangedPaths @('docs/versioning.md') -ForceRelease;Assert ($p.release -and !$p.full -and !$p.managed.Count -and !$p.backend) 'Every merged PR can publish without forcing unrelated test suites'
$p=Get-ValidationPlan -ChangedPaths @('game/AnimalTown.GameCube/TitleArt.cs','game/AnimalTown.GameCube/TitleBackdrop.cs','game/AnimalTown.GameCube/LoadingScreen.cs');Assert (($p.managed -join ',') -eq 'ui,menu,saves' -and !$p.backend -and !$p.full) 'Independent title/loading UI needs UI, navigation and save checks'
$p=Get-ValidationPlan -ChangedPaths @('game/AnimalTown.GameCube/ProfilePicker.cs','game/AnimalTown.GameCube/TownSession.cs');Assert (($p.managed -join ',') -eq 'ui,menu,saves' -and !$p.backend -and !$p.full) 'Profile selection must exercise UI, navigation and save loading'
# Rendering fixture edits exercise only that fixture; production edits include dependents.
$fixtures=@{WaterEffect='water';RenderingService='services';MovementTerrain='movement';GroundRing='ground';TerrainIndex='index';ActorVisibility='visibility';AttentionGeometry='attention';DroppedItem='dropped-item';UiTextLayout='font'}
foreach($fixture in $fixtures.Keys){
    $p=Get-ValidationPlan -ChangedPaths @("tests/AnimalTown.RenderingChecks/$($fixture)Checks.cs")
    Assert (($p.rendering -join ',') -eq $fixtures[$fixture] -and !$p.managed.Count -and !$p.backend -and !$p.full) "Rendering fixture routing incorrect: $fixture"
}
$p=Get-ValidationPlan -ChangedPaths @('shared/AnimalTown.Domain/SunshineWater.cs');Assert (($p.managed -join ',') -eq 'terrain,core' -and ($p.rendering -join ',') -eq 'water' -and !$p.backend -and !$p.full) 'Water materials must not start backend/importer tests'
$p=Get-ValidationPlan -ChangedPaths @('shared/AnimalTown.Domain/WaterSurface.cs');Assert (($p.rendering -join ',') -eq 'water,movement') 'Water geometry must exercise the depth cache and cylinder movement checks'
$p=Get-ValidationPlan -ChangedPaths @('shared/AnimalTown.Domain/WaterTerrain.cs');Assert (($p.managed -join ',') -eq 'terrain,movement,generation,life,core' -and $p.rendering.Count -eq 9 -and !$p.backend) 'Water terrain topology needs geometry, navigation and generation coverage'
$p=Get-ValidationPlan -ChangedPaths @('shared/AnimalTown.Domain/SunshineWater.cs','shared/AnimalTown.Domain/RenderView.cs');Assert ($p.rendering.Count -eq 9) 'Mixed water and camera changes must retain all camera dependents'
$p=Get-ValidationPlan -ChangedPaths @('shared/AnimalTown.Domain/RenderView.cs','shared/AnimalTown.Domain/SunshineWater.cs');Assert ($p.rendering.Count -eq 9) 'Selection must not depend on changed-path order'
$p=Get-ValidationPlan -ChangedPaths @('shared/AnimalTown.Domain/GroundRingPlan.cs','tests/AnimalTown.RenderingChecks/WaterEffectChecks.cs');Assert (($p.rendering -join ',') -eq 'water,ground') 'Rendering selections must union across components'
$p=Get-ValidationPlan -ChangedPaths @('game/AnimalTown.GameCube/GeometryStaging.cs');Assert (($p.rendering -join ',') -eq 'services' -and !$p.backend) 'Geometry staging must run rendering service checks'
$p=Get-ValidationPlan -ChangedPaths @('game/AnimalTown.GameCube/TerrainIndexBuffer.cs');Assert (($p.rendering -join ',') -eq 'index') 'Index buffers must run index topology checks'
$p=Get-ValidationPlan -ChangedPaths @('game/AnimalTown.GameCube/VillagerAttentionRenderer.cs');Assert (($p.rendering -join ',') -eq 'attention') 'Attention renderer must run geometry checks'
$p=Get-ValidationPlan -ChangedPaths @('shared/AnimalTown.Domain/ActorFrameVisibility.cs');Assert (($p.rendering -join ',') -eq 'movement,visibility' -and !$p.backend) 'Actor visibility must exercise animation and frustum checks'
$p=Get-ValidationPlan -ChangedPaths @('tools/native-terrain-archive.inc','tests/native-terrain-archive.c');Assert (($p.native -join ',') -eq 'patched' -and ($p.managed -join ',') -eq 'terrain,core' -and !$p.full -and !$p.backend) 'Archive changes need native patch fixtures, not backend/importer checks'
$p=Get-ValidationPlan -ChangedPaths @('tests/AnimalTown.Checks/WaterSurfaceChecks.cs');Assert (($p.managed -join ',') -eq 'terrain' -and !$p.rendering.Count) 'Managed test-only edits must not rerun rendering fixtures'
$p=Get-ValidationPlan -ChangedPaths @('shared/AnimalTown.Domain/RunningMinimap.cs');Assert (($p.managed -join ',') -eq 'ui' -and !$p.backend) 'Minimap geometry must use its UI fixture'
$p=Get-ValidationPlan -ChangedPaths @('shared\AnimalTown.Domain\SunshineWater.cs','shared/AnimalTown.Domain/SunshineWater.cs');Assert (($p.rendering -join ',') -eq 'water') 'Windows path normalization and deduplication must preserve narrow checks'
$p=Get-ValidationPlan -ChangedPaths @('tests/AnimalTown.RenderingChecks/NewChecks.cs');Assert ($p.full -and $p.rendering.Count -eq 9 -and $p.backend) 'Unknown rendering fixtures must be conservative'
$p=Get-ValidationPlan -Full;Assert ($p.schema -eq 2 -and $p.rendering.Count -eq 9 -and 'patched' -in $p.native) 'Full validation must cover rendering and native patch fixtures'
$p=Get-ValidationPlan -ChangedPaths @('shared/AnimalTown.Domain/DroppedItemPlan.cs','game/AnimalTown.GameCube/DroppedItemRenderer.cs');Assert (($p.managed -join ',') -eq 'terrain,core' -and ($p.rendering -join ',') -eq 'dropped-item' -and !$p.backend -and !$p.full) 'Dropped sprites need visibility and actual quad geometry checks'
$p=Get-ValidationPlan -ChangedPaths @('assets/textures/items/dropped-item.dndtex','tools/AnimalTown.Tools/DroppedItemAssets.cs');Assert (($p.tooling -join ',') -eq 'dropped-item' -and ($p.rendering -join ',') -eq 'dropped-item' -and 'terrain' -in $p.managed -and !$p.full -and !$p.backend) 'Dropped item sources and native assets must exercise their cooker and geometry checks'
$p=Get-ValidationPlan -Full;Assert ('dropped-item' -in $p.tooling -and 'dropped-item' -in $p.rendering) 'Full validation must include sprite source/native round trips and geometry'
$p=Get-ValidationPlan -ChangedPaths @('game/AnimalTown.GameCube/WaterMaterial.cs','game/AnimalTown.GameCube/DroppedItemRenderer.cs');Assert (($p.rendering -join ',') -eq 'water,dropped-item') 'Mixed water and sprite changes must preserve both rendering groups'
$p=Get-ValidationPlan -ChangedPaths @('game/AnimalTown.GameCube/DroppedItemRenderer.cs','game/AnimalTown.GameCube/WaterMaterial.cs');Assert (($p.rendering -join ',') -eq 'water,dropped-item') 'Sprite and water routing must not depend on path order'
foreach($path in @('shared/AnimalTown.Domain/BuildingPrompt.cs','tests/AnimalTown.Checks/BuildingEntryChecks.cs')){
    $p=Get-ValidationPlan -ChangedPaths @($path)
    Assert (($p.managed -join ',') -eq 'movement,life,saves,buildings' -and !$p.backend -and !$p.full -and !$p.rendering.Count) "Building entry routing must retain component coverage: $path"
}
$p=Get-ValidationPlan -ChangedPaths @('game/AnimalTown.GameCube/UiFont.cs','game/AnimalTown.GameCube/UiTextLayoutCache.cs');Assert (($p.managed -join ',') -eq 'ui,text' -and ($p.rendering -join ',') -eq 'font' -and !$p.backend -and !$p.full) 'Font storage must select layout equivalence checks'
Write-Host 'Validation routing checks passed (56 cases).'
