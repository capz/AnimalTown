# Pure path-to-check routing; conservative fallback for new production paths.
function Get-ValidationPlan {
    param([string[]]$ChangedPaths=@(),[switch]$Full,[string]$Commit='',[string]$BaseRef='')
    $allManaged=@('ui','menu','text','trim','terrain','movement','dialogue','generation','life','saves','buildings','core')
    $managed=[Collections.Generic.HashSet[string]]::new()
    $tooling=[Collections.Generic.HashSet[string]]::new()
    $native=[Collections.Generic.HashSet[string]]::new()
    $backend=$false;$policy=$false;$release=[bool]$Full;$broad=[bool]$Full
    foreach($raw in $ChangedPaths){
        $p=$raw.Replace('\','/')
        if($p -match '^(docs/|.*\.md$|LICENSE$|\.gitignore$)'){continue}
        $release=$true
        if($p -match '^(build/|\.github/|Directory\.|.*\.sln$|.*\.csproj$|\.gitmodules$|vendor/|tools/(build-|publish-release|patch-native|.*validation|test-changes)|tests/validation-plan)'){$broad=$true;continue}
        if($p -match '^server/|^tests/(server-integration|AnimalTown.ServerChecks)'){$backend=$true;continue}
        if($p -match '^tests/AnimalTown.Checks/Program.cs$'){$broad=$true;continue}
        if($p -match '^(shared/AnimalTown.Domain/WaterSurface|tests/AnimalTown.Checks/WaterSurfaceChecks)\.cs$') {[void]$managed.Add('terrain');continue}
        if($p -match '^(shared/AnimalTown.Domain/RiverBankGeometry|tests/AnimalTown.Checks/RiverBankChecks)\.cs$'){foreach($s in @('terrain','movement','life','generation')){[void]$managed.Add($s)};continue}
        if($p -match '(MenuNavigation|UiShapePlan|WrappedText|PlayerMovement|DialogueVoice|TownTrim|TerrainEdge|TownGeneration|TerrainLife|SplitSave)Checks\.cs$'){
            $names=@{MenuNavigation='menu';UiShapePlan='ui';WrappedText='text';PlayerMovement='movement';DialogueVoice='dialogue';TownTrim='trim';TerrainEdge='terrain';TownGeneration='generation';TerrainLife='life';SplitSave='saves'}
            [void]$managed.Add($names[$Matches[1]]);continue
        }
        if($p -match '^(?:shared/AnimalTown.Domain|game/AnimalTown.GameCube)/(MenuNavigation|UiShapePlan|WrappedText|PlayerMovement|DialogueVoice)\.cs$'){
            $names=@{MenuNavigation='menu';UiShapePlan='ui';WrappedText='text';PlayerMovement='movement';DialogueVoice='dialogue'}
            [void]$managed.Add($names[$Matches[1]]);continue
        }
        if($p -match '^shared/AnimalTown.Domain/(TownClock|VillagerNavigation|VillagerNames)\.cs$'){[void]$managed.Add('life');[void]$managed.Add('generation');continue}
        if($p -match '^shared/AnimalTown.Domain/TownGenerator\.cs$'){foreach($s in @('generation','life','trim','core')){[void]$managed.Add($s)};continue}
        if($p -match '^shared/AnimalTown.Domain/LowPolyTree\.cs$'){[void]$managed.Add('core');[void]$managed.Add('terrain');continue}
        if($p -match '^shared/AnimalTown.Domain/(TerrainElevation|CylinderWorld|RenderView|TerrainTiles|FloorStrips|TerrainStream)\.cs$'){
            foreach($s in @('core','terrain','movement','generation','life')){[void]$managed.Add($s)};continue
        }
        if($p -match '^shared/AnimalTown.Domain/(TexturePixels|TextureResolution|FloorTexture)\.cs$'){
            [void]$managed.Add('core');[void]$tooling.Add('terrain');[void]$tooling.Add('repack');continue
        }
        if($p -match '^(shared/AnimalTown.Domain/(BuildingWorld|BuildingState|BuildingSave|PortalSystem|InteriorNavigation|InteriorCamera|VillagerJourney)|tests/AnimalTown.Checks/BuildingChecks)\.cs$'){
            foreach($s in @('buildings','movement','life','saves')){[void]$managed.Add($s)};continue
        }
        if($p -match '^assets/definitions/|^assets/models/(buildings/|interiors/cottage_small/)|^assets/textures/buildings/|^tools/AnimalTown.Tools/BuildingAssets\.cs$'){
            [void]$managed.Add('buildings');[void]$tooling.Add('building');continue
        }
        if($p -match '^shared/AnimalTown.Domain/'){
            foreach($s in $allManaged){[void]$managed.Add($s)};$backend=$true;continue
        }
        if($p -match '^(tools/native-uv|tests/native-uv)'){[void]$native.Add('uv');continue}
        if($p -match '^(tools/native-card|tests/native-card)'){[void]$native.Add('card');[void]$managed.Add('saves');continue}
        if($p -match '^(tools/native-texture|tests/native-texture)'){[void]$native.Add('texture');[void]$managed.Add('core');continue}
        if($p -match '^tests/native-dialogue') {[void]$native.Add('voices');[void]$managed.Add('dialogue');continue}
        if($p -match '^tools/native-calendar'){[void]$managed.Add('life');continue}
        if($p -match '^tools/AnimalTown.Tools/(TextureRepacker|Uv)'){[void]$tooling.Add('repack');continue}
        if($p -match '^tools/AnimalTown.Tools/(Terrain|NeutralFloor|RepeatFloor)'){[void]$tooling.Add('terrain');[void]$managed.Add('core');[void]$managed.Add('terrain');continue}
        if($p -match '^assets/models/'){continue} # Real imports + ISO/texture audits validate these.
        if($p -match '^assets/(terrain/|textures/environment/)'){[void]$tooling.Add('terrain');[void]$managed.Add('core');continue}
        if($p -match '^assets/(fonts/|textures/)|^tools/AnimalTown.Tools/Brand'){[void]$managed.Add('ui');[void]$managed.Add('text');continue}
        if($p -match '^game/'){
            if($p -match '(Terrain|Water|Tree|Texture|Model|Render|FrameProfile)'){[void]$managed.Add('core');[void]$managed.Add('terrain')}
            elseif($p -match 'NativeCalendar'){[void]$managed.Add('life')}
            elseif($p -match 'Dialogue'){[void]$managed.Add('dialogue')}
            elseif($p -match 'CardSave'){[void]$managed.Add('saves');[void]$native.Add('card')}
            elseif($p -match '(Ui|Backpack|Map)'){[void]$managed.Add('ui');[void]$managed.Add('menu');[void]$managed.Add('text')}
            else{foreach($s in $allManaged){[void]$managed.Add($s)}}
            continue
        }
        if($p -match '^tools/iso-smoke.py$'){[void]$native.Add('python');continue} # Scenarios are explicitly selected.
        $broad=$true
    }
    if($broad){foreach($s in $allManaged){[void]$managed.Add($s)};foreach($s in @('terrain','repack','building')){[void]$tooling.Add($s)};foreach($s in @('uv','card','texture','voices','python')){[void]$native.Add($s)};$backend=$true;$policy=$true}
    [pscustomobject]@{schema=1;commit=$Commit;baseRef=$BaseRef;release=$release;full=$broad;managed=@($allManaged|Where-Object {$managed.Contains($_)});tooling=@($tooling|Sort-Object);native=@($native|Sort-Object);backend=$backend;policy=$policy;paths=@($ChangedPaths|Sort-Object -Unique)}
}
