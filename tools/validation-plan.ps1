# Pure path-to-check routing; conservative fallback for new production paths.
function Get-ValidationPlan {
    param([string[]]$ChangedPaths=@(),[switch]$Full,[switch]$ForceRelease,[string]$Commit='',[string]$BaseRef='')
    $allManaged=@('ui','menu','text','trim','terrain','movement','dialogue','generation','life','saves','buildings','core')
    $allRendering=@('water','services','movement','ground','index','visibility','attention')
    $rendering=[Collections.Generic.HashSet[string]]::new()
    $managed=[Collections.Generic.HashSet[string]]::new()
    # Each terrain-producing rule contributes its own rendering dependencies.
    # This preserves the union for mixed changes, even after a narrow water rule.
    function Add-Managed([string]$Suite){
        [void]$managed.Add($Suite)
        if($Suite -eq 'terrain'){foreach($r in $renderingForPath){[void]$rendering.Add($r)}}
    }
    $tooling=[Collections.Generic.HashSet[string]]::new()
    $native=[Collections.Generic.HashSet[string]]::new()
    $backend=$false;$policy=$false;$release=[bool]($Full -or $ForceRelease);$broad=[bool]$Full
    foreach($raw in $ChangedPaths){
        $p=$raw.Replace('\','/')
        $renderingForPath=if($p -match '^tests/AnimalTown.Checks/'){ @() }else{ $allRendering }
        if($p -match '^(shared/AnimalTown.Domain/(SunshineWater|WindWakerWaterPattern|WindWakerWaveField|WindWakerWaveGrid)|game/AnimalTown.GameCube/WaterMaterial)\.cs$'){$renderingForPath=@('water')}
        elseif($p -match '^(shared/AnimalTown.Domain/(WaterSurface|WaterDepthCache)|game/AnimalTown.GameCube/(WaterRenderer|LocalOceanWaves))\.cs$'){$renderingForPath=@('water','movement')}
        elseif($p -match '^shared/AnimalTown.Domain/(ActorFrameVisibility|AnimationFrameClock)\.cs$'){$renderingForPath=@('movement','visibility')}
        elseif($p -match '^(shared/AnimalTown.Domain/GroundRingPlan|game/AnimalTown.GameCube/GroundRingRenderer)\.cs$'){$renderingForPath=@('ground')}
        elseif($p -match '^game/AnimalTown.GameCube/GeometryStaging\.cs$'){$renderingForPath=@('services')}
        elseif($p -match '^game/AnimalTown.GameCube/TerrainIndexBuffer\.cs$'){$renderingForPath=@('index')}
        elseif($p -match '^game/AnimalTown.GameCube/VillagerAttentionRenderer\.cs$'){$renderingForPath=@('attention')}
        if($p -match '^(docs/|.*\.md$|LICENSE$|\.gitignore$)'){continue}
        $release=$true
        if($p -match '^(build/|\.github/|Directory\.|.*\.sln$|.*\.csproj$|\.gitmodules$|vendor/|tools/(build-|publish-release|patch-native|.*validation|test-changes)|tests/validation-(plan|selection))'){$broad=$true;continue}
        if($p -match '^server/|^tests/(server-integration|AnimalTown.ServerChecks)'){$backend=$true;continue}
        if($p -match '^(shared/AnimalTown.Domain/DevConfig|game/AnimalTown.GameCube/DevConfigStorage|tests/AnimalTown.Checks/DevConfigChecks)\.cs$'){Add-Managed 'saves';Add-Managed 'menu';continue}
        if($p -match '^game/AnimalTown.GameCube/(TitleScreen|TitleArt|TitleBackdrop|LoadingScreen|ProfilePicker|TownSession)\.cs$'){Add-Managed 'ui';Add-Managed 'menu';Add-Managed 'saves';continue}
        if($p -match '^tests/AnimalTown.RenderingChecks/(WaterEffect|RenderingService|MovementTerrain|GroundRing|TerrainIndex|ActorVisibility|AttentionGeometry)Checks\.cs$'){
            $names=@{WaterEffect='water';RenderingService='services';MovementTerrain='movement';GroundRing='ground';TerrainIndex='index';ActorVisibility='visibility';AttentionGeometry='attention'}
            [void]$rendering.Add($names[$Matches[1]]);continue
        }
        if($p -match '^(shared/AnimalTown.Domain/(SunshineWater|WindWakerWaterPattern|WindWakerWaveField|WindWakerWaveGrid|WaterDepthCache|GroundRingPlan|ActorFrameVisibility|AnimationFrameClock)|game/AnimalTown.GameCube/(LocalOceanWaves|GroundRingRenderer|GeometryStaging|TerrainIndexBuffer|VillagerAttentionRenderer))\.cs$'){
            Add-Managed 'terrain';Add-Managed 'core';continue
        }
        if($p -match '^(tools/native-terrain-(archive|recycle)\.inc|tests/native-terrain-(archive|recycle)\.c)$'){
            Add-Managed 'terrain';Add-Managed 'core';[void]$native.Add('patched');continue
        }
        if($p -match '^tests/AnimalTown.Checks/Program.cs$'){$broad=$true;continue}
        if($p -match '^(shared/AnimalTown.Domain/WaterSurface|tests/AnimalTown.Checks/WaterSurfaceChecks)\.cs$') {Add-Managed 'terrain';continue}
        if($p -match '^(shared/AnimalTown.Domain/RiverBankGeometry|tests/AnimalTown.Checks/RiverBankChecks)\.cs$'){foreach($s in @('terrain','movement','life','generation')){Add-Managed $s};continue}
        if($p -match '(MenuNavigation|UiShapePlan|WrappedText|PlayerMovement|DialogueVoice|TownTrim|TerrainEdge|TownGeneration|TerrainLife|SplitSave)Checks\.cs$'){
            $names=@{MenuNavigation='menu';UiShapePlan='ui';WrappedText='text';PlayerMovement='movement';DialogueVoice='dialogue';TownTrim='trim';TerrainEdge='terrain';TownGeneration='generation';TerrainLife='life';SplitSave='saves'}
            Add-Managed $names[$Matches[1]];continue
        }
        if($p -match '^(?:shared/AnimalTown.Domain|game/AnimalTown.GameCube)/(MenuNavigation|UiShapePlan|WrappedText|PlayerMovement|DialogueVoice)\.cs$'){
            $names=@{MenuNavigation='menu';UiShapePlan='ui';WrappedText='text';PlayerMovement='movement';DialogueVoice='dialogue'}
            Add-Managed $names[$Matches[1]];continue
        }
        if($p -match '^shared/AnimalTown.Domain/(TownClock|VillagerNavigation|VillagerNames)\.cs$'){Add-Managed 'life';Add-Managed 'generation';continue}
        if($p -match '^(shared/AnimalTown.Domain/RunningMinimap|tests/AnimalTown.Checks/RunningMinimapChecks)\.cs$'){Add-Managed 'ui';continue}
        if($p -match '^shared/AnimalTown.Domain/(TerrainArchivePlan|TerrainArchiveView|TerrainCliffBinding|TerrainCliffGeometry|TerrainSectionBounds|TerrainSectionPlan|GroundSurface|WaterInspection)\.cs$'){Add-Managed 'terrain';Add-Managed 'core';continue}
        if($p -match '^shared/AnimalTown.Domain/(WaterTerrain|CylinderRows)\.cs$'){foreach($s in @('terrain','movement','generation','life','core')){Add-Managed $s};continue}
        if($p -match '^shared/AnimalTown.Domain/TownGenerator\.cs$'){foreach($s in @('generation','life','trim','core')){Add-Managed $s};continue}
        if($p -match '^shared/AnimalTown.Domain/LowPolyTree\.cs$'){Add-Managed 'core';Add-Managed 'terrain';continue}
        if($p -match '^shared/AnimalTown.Domain/(TerrainElevation|CylinderWorld|RenderView|TerrainTiles|FloorStrips|TerrainStream)\.cs$'){
            foreach($s in @('core','terrain','movement','generation','life')){Add-Managed $s};continue
        }
        if($p -match '^shared/AnimalTown.Domain/(TexturePixels|TextureResolution|FloorTexture)\.cs$'){
            Add-Managed 'core';[void]$tooling.Add('terrain');[void]$tooling.Add('repack');continue
        }
        if($p -match '^(shared/AnimalTown.Domain/(BuildingWorld|BuildingState|BuildingSave|PortalSystem|InteriorNavigation|InteriorCamera|VillagerJourney)|tests/AnimalTown.Checks/BuildingChecks)\.cs$'){
            foreach($s in @('buildings','movement','life','saves')){Add-Managed $s};continue
        }
        if($p -match '^assets/definitions/|^assets/models/(buildings/|interiors/cottage_small/)|^assets/textures/buildings/|^tools/AnimalTown.Tools/BuildingAssets\.cs$'){
            Add-Managed 'buildings';[void]$tooling.Add('building');continue
        }
        if($p -match '^shared/AnimalTown.Domain/'){
            foreach($s in $allManaged){Add-Managed $s};$backend=$true;continue
        }
        if($p -match '^(tools/native-uv|tests/native-uv)'){[void]$native.Add('uv');continue}
        if($p -match '^(tools/native-card|tests/native-card)'){[void]$native.Add('card');Add-Managed 'saves';continue}
        if($p -match '^(tools/native-texture|tests/native-texture)'){[void]$native.Add('texture');Add-Managed 'core';continue}
        if($p -match '^(tools/native-shadow|tests/native-shadow)'){[void]$native.Add('shadows');Add-Managed 'terrain';continue}
        if($p -match '^tests/native-dialogue') {[void]$native.Add('voices');Add-Managed 'dialogue';continue}
        if($p -match '^tools/native-calendar'){Add-Managed 'life';continue}
        if($p -match '^tools/AnimalTown.Tools/(TextureRepacker|Uv)'){[void]$tooling.Add('repack');continue}
        if($p -match '^tools/AnimalTown.Tools/(Terrain|NeutralFloor|RepeatFloor)'){[void]$tooling.Add('terrain');Add-Managed 'core';Add-Managed 'terrain';continue}
        if($p -match '^assets/models/'){continue} # Real imports + ISO/texture audits validate these.
        if($p -match '^assets/(terrain/|textures/environment/)'){[void]$tooling.Add('terrain');Add-Managed 'core';continue}
        if($p -match '^assets/(fonts/|textures/)|^tools/AnimalTown.Tools/Brand'){Add-Managed 'ui';Add-Managed 'text';continue}
        if($p -match '^game/'){
            if($p -match '(Terrain|Water|Tree|Texture|Model|Render|FrameProfile)'){Add-Managed 'core';Add-Managed 'terrain'}
            elseif($p -match 'NativeCalendar'){Add-Managed 'life'}
            elseif($p -match 'Dialogue'){Add-Managed 'dialogue'}
            elseif($p -match 'CardSave'){Add-Managed 'saves';[void]$native.Add('card')}
            elseif($p -match '(Ui|Backpack|Map)'){Add-Managed 'ui';Add-Managed 'menu';Add-Managed 'text'}
            else{foreach($s in $allManaged){Add-Managed $s}}
            continue
        }
        if($p -match '^tools/iso-smoke.py$'){[void]$native.Add('python');continue} # Scenarios are explicitly selected.
        $broad=$true
    }
    if($broad){foreach($s in $allManaged){Add-Managed $s};foreach($s in @('terrain','repack','building','nkit')){[void]$tooling.Add($s)};foreach($s in @('uv','card','texture','voices','python','shadows','patched')){[void]$native.Add($s)};$backend=$true;$policy=$true}
    if($broad){foreach($r in $allRendering){[void]$rendering.Add($r)}}
    [pscustomobject]@{schema=2;commit=$Commit;baseRef=$BaseRef;release=$release;full=$broad;managed=@($allManaged|Where-Object {$managed.Contains($_)});rendering=@($allRendering|Where-Object {$rendering.Contains($_)});tooling=@($tooling|Sort-Object);native=@($native|Sort-Object);backend=$backend;policy=$policy;paths=@($ChangedPaths|Sort-Object -Unique)}
}
