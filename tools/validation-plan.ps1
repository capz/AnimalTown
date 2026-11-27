# Each changed path contributes its own scenarios. Mixed changes take the union.
function Get-EmulatorChecksForPath([string]$Path) {
    if($Path -match '^game/AnimalTown.GameCube/(Online[^/]*|TownGame\.Online[^/]*)\.cs$|^shared/AnimalTown.Domain/Online/|^tests/AnimalTown.OnlineChecks/DolphinTargetFixture\.cs$|^tests/server-integration\.ps1$|^tools/(online-play-smoke|profile-card-fixture|dolphin_network)\.py$'){'online-play'}
    if($Path -match '^assets/models/villagers/|^game/AnimalTown.GameCube/(VillagerVisualSwap|TownGame\.VillagerVisuals)\.cs$|^shared/AnimalTown.Domain/VillagerAppearance\.cs$'){'villager-visuals'}
    if($Path -match '^game/AnimalTown.GameCube/Program\.cs$|^tools/(iso-smoke|dolphin_pad)\.py$'){
        return $allEmulator
    }
    if($Path -match '^assets/textures/branding/|^game/AnimalTown.GameCube/(TitleScreen|TitleArt|TitleBackdrop|ProfilePickerUi|OnboardingUi|TextureCache|ModelResources)\.cs$|^shared/AnimalTown.Domain/(TexturePixels|TextureResolution)\.cs$|^tools/AnimalTown.Tools/(Brand[^/]*|DiscBannerFile|TextureAudit)\.cs$'){
        'title-logo'
    }
    if($Path -match '^game/AnimalTown.GameCube/(TitleScreen|VideoOptions|UserOptionsStorage|DevConfigStorage)\.cs$|^shared/AnimalTown.Domain/UserOptions\.cs$'){
        'video-options'
    }
    if($Path -match '^game/AnimalTown.GameCube/(OnlinePlay|OnlineMenuUi|TownGame\.Review|TownDataStorage|TitleScreen)\.cs$|^tools/(online-review-smoke|profile-card-fixture)\.py$|^tests/AnimalTown.Checks/JobFixture\.cs$'){
        'job-online-review'
    }
    if($Path -match '^assets/models/villagers/|^game/AnimalTown.GameCube/TownGame\.Save[^/]*\.cs$|^shared/AnimalTown.Domain/(SaveRepository|SaveCodec|SplitSaveCodec|IncrementalSaveCodec)\.cs$'){
        'save-and-quit'
    }
    if($Path -match '^assets/textures/environment/water/|^tools/AnimalTown.Tools/WaterTextureArt\.cs$|^game/AnimalTown.GameCube/(Water[^/]*|NativeWater(?:Probe)?|TerrainRenderer(?:\.[^/]+)?|TerrainSection|GroundMip[^/]*|ModelResources)\.cs$|^shared/AnimalTown.Domain/(GroundMip[^/]*|RenderDebugOptions|DevConfig|TextureResolution|Water[^/]*|WindWaker[^/]*)\.cs$|^tools/(patch-water-(?:probe|optics)\.py|native-water-(?:probe|optics)\.inc|native-shadow-union-gx\.inc)$'){
        'terrain-materials'
    }
    if($Path -match '^game/AnimalTown.GameCube/(SaveCards|CardArchive|CardSaveStorage|TownDataStorage|TownSession|TitleScreen(?:\.Cards)?|OnboardingFlow|OnboardingUi)\.cs$|^shared/AnimalTown.Domain/OnboardingDraft\.cs$'){
        'card-slot-b';'card-choose-b';'card-without-saving';'card-no-card'
    }
    elseif($Path -match '^game/AnimalTown.GameCube/(UserOptionsStorage|DevConfigStorage)\.cs$'){
        'card-slot-b'
    }
    if($Path -match '^shared/AnimalTown.Domain/(WorldLighting|DaylightLocations)\.cs$|^game/AnimalTown.GameCube/(CloudShadows|NativeShadows|TownLights|TerrainRenderer)\.cs$|^tools/native-shadow-union[^/]*$'){
        'daylight'
    }
    if($Path -match '^shared/AnimalTown.Domain/(Jobs/|GameState\.Jobs\.cs$)|^game/AnimalTown.GameCube/(TownGame\.JobScene|LoadingScreen|JobSceneRenderer|TownCardRecords|CardArchive)\.cs$|^tools/(jobs_smoke|profile-card-fixture)\.py$|^tests/AnimalTown.Checks/JobFixture\.cs$'){
        'job-delivery';'job-mail';'job-helipad'
    }
    else{
        if($Path -match '^game/AnimalTown.GameCube/TownGame\.JobsUi\.cs$'){'job-delivery'}
        if($Path -match '^game/AnimalTown.GameCube/(TownGame\.MailUi|WrappedText)\.cs$|^shared/AnimalTown.Domain/WrappedText\.cs$'){'job-mail'}
        if($Path -match '^shared/AnimalTown.Domain/(GameState\.Travel|HelipadFlight)\.cs$|^game/AnimalTown.GameCube/(TownGame\.(TravelUi|Helipad)|TownSession\.Travel|HelipadCruise|HelipadRenderer(?:\.Deck)?|OfflineTravelCards)\.cs$'){
            'job-helipad'
        }
    }
}

# Pure path-to-check routing; conservative fallback for new production paths.
function Get-ValidationPlan {
    param([string[]]$ChangedPaths=@(),[switch]$Full,[switch]$ForceRelease,[string]$Commit='',[string]$BaseRef='')
    $allManaged=@('ui','menu','text','trim','terrain','movement','dialogue','generation','life','saves','buildings','core','content')
    $allRendering=@('water','services','movement','ground','index','visibility','attention','dropped-item')
    $allResources=@('memory','floor','snow','visual')
    $allEmulator=@('title-logo','video-options','save-and-quit','terrain-materials','card-slot-b','card-choose-b','card-without-saving','card-no-card','daylight','job-delivery','job-mail','job-helipad','job-online-review','villager-visuals','online-play')
    $resources=[Collections.Generic.HashSet[string]]::new()
    $emulator=[Collections.Generic.HashSet[string]]::new()
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
    $backend=$false;$policy=$false;$release=[bool]($Full -or $ForceRelease);$broad=[bool]$Full;$broadEmulator=[bool]$Full
    foreach($raw in $ChangedPaths){
        $p=$raw.Replace('\','/')
        $renderingForPath=if($p -match '^tests/AnimalTown.Checks/'){ @() }else{ $allRendering }
        if($p -match '^(shared/AnimalTown.Domain/(WindWakerWaterPattern|WindWakerWaveField|WindWakerWaveGrid|ShallowWaterPattern)|game/AnimalTown.GameCube/WaterMaterial)\.cs$'){$renderingForPath=@('water')}
        elseif($p -match '^(shared/AnimalTown.Domain/(WaterSurface|WaterDepthCache|WaterBedGeometry)|game/AnimalTown.GameCube/(WaterRenderer|WaterBedRenderer|LocalOceanWaves))\.cs$'){$renderingForPath=@('water','movement')}
        elseif($p -match '^shared/AnimalTown.Domain/(ActorFrameVisibility|AnimationFrameClock)\.cs$'){$renderingForPath=@('movement','visibility')}
        elseif($p -match '^(shared/AnimalTown.Domain/GroundRingPlan|game/AnimalTown.GameCube/GroundRingRenderer)\.cs$'){$renderingForPath=@('ground')}
        elseif($p -match '^game/AnimalTown.GameCube/GeometryStaging\.cs$'){$renderingForPath=@('services')}
        elseif($p -match '^game/AnimalTown.GameCube/TerrainIndexBuffer\.cs$'){$renderingForPath=@('index')}
        elseif($p -match '^game/AnimalTown.GameCube/VillagerAttentionRenderer\.cs$'){$renderingForPath=@('attention')}
        elseif($p -match '^(shared/AnimalTown.Domain/DroppedItemPlan|game/AnimalTown.GameCube/DroppedItemRenderer)\.cs$|^assets/textures/items/|^tools/AnimalTown.Tools/DroppedItemAssets\.cs$'){$renderingForPath=@('dropped-item')}
        if($p -eq 'docs/dolphin-depot-install.txt'){$release=$true;[void]$tooling.Add('depot');continue}
        if($p -match '^(docs/|.*\.md$|LICENSE$|\.gitignore$)'){continue}
        if($p -eq 'version.json'){$policy=$true;continue}
        if($p -notmatch '^tests/|^tools/(test-changes|select-validation|validation-plan)\.ps1$'){
            $release=$true
        }
        foreach($scenario in (Get-EmulatorChecksForPath $p)){[void]$emulator.Add($scenario)}
        if($p -match '^(shared/AnimalTown.Domain/VillagerAppearance|game/AnimalTown.GameCube/(VillagerVisualSwap|TownGame\.VillagerVisuals)|tests/AnimalTown.Checks/VillagerVisualSwapChecks)\.cs$'){
            Add-Managed 'menu';Add-Managed 'buildings';Add-Managed 'saves';[void]$resources.Add('memory');[void]$resources.Add('visual');continue
        }
        if($p -eq 'tools/import-villagers.py'){[void]$tooling.Add('repack');[void]$resources.Add('memory');continue}
        if($p -match '^game/AnimalTown.GameCube/(BuildingRenderer|VisualResidencyBudget|StartupModelAdmission|TownLights|WaterBumpEffect)\.cs$'){
            [void]$resources.Add('memory')
        }
        if($p -match '^game/AnimalTown.GameCube/TerrainRenderer\.Textures\.cs$|^shared/AnimalTown.Domain/(FloorStrips|FloorTexture)\.cs$'){
            [void]$resources.Add('floor')
        }
        if($p -match '^game/AnimalTown.GameCube/TerrainRenderer\.Snow\.cs$|^shared/AnimalTown.Domain/SnowWear(?:\.[^/]+)?\.cs$'){
            [void]$resources.Add('snow')
        }
        if($p -match '^game/AnimalTown.GameCube/(LowPolyTrees|DroppedItemRenderer|RainRenderer|SnowEdgeRenderer(?:\.Geometry)?|TerrainIndexBuffer|TerrainMeshCache)\.cs$'){
            [void]$resources.Add('visual')
        }
        if($p -match '^tests/AnimalTown\.(Memory|FloorResidency|SnowResidency|VisualAllocation)Checks/'){
            $groups=@{Memory='memory';FloorResidency='floor';SnowResidency='snow';VisualAllocation='visual'}
            [void]$resources.Add($groups[$Matches[1]]);continue
        }
        if($p -match '^tests/AnimalTown.CardChecks/'){[void]$native.Add('card');continue}
        if($p -match '^shared/AnimalTown.Domain/Online/'){
            $backend=$true
            [void]$native.Add('online')
            if($p -match '(OnlinePosePump|OnlineWire)\.cs$'){Add-Managed 'movement'}
            if($p -match '(OnlineCredentialStore|OnlineTravelStore|TownCheckpointExport)\.cs$'){Add-Managed 'saves'}
            continue
        }
        if($p -match '^tests/AnimalTown.OnlineSmoke/|^tests/native-online\.(c|py)$'){$backend=$true;[void]$native.Add('online');continue}
        if($p -match '^tests/AnimalTown\.(Server|Online)Checks/|^tests/server-integration\.ps1$|^tools/online-poc\.ps1$'){$backend=$true;continue}
        if($p -match '^tests/(validation-plan|validation-selection|versioning|release-publish)\.ps1$|^tools/(test-changes|select-validation|validation-plan)\.ps1$'){
            $policy=$true;continue
        }
        if($p -match '^(shared/AnimalTown.Domain/VisualMemoryPressure\.cs$|game/AnimalTown.GameCube/(VisualMemoryCoordinator|NativeMemory|TerrainRenderer\.(Textures|Snow|Streaming))\.cs$|tools/(native-memory-pressure\.(c|h)|patch-memory-pressure\.py)$|tests/(native-memory-(pressure|wrapping)\.c|memory-pressure-sdk/))'){
            if($p -notmatch 'TerrainRenderer\.(Textures|Snow)\.cs$'){
                foreach($group in $allResources){[void]$resources.Add($group)}
            }
            Add-Managed 'core';Add-Managed 'terrain';[void]$native.Add('patched');continue
        }
        if($p -match '^tests/AnimalTown.JobSmoke/'){Add-Managed 'life';[void]$native.Add('jobs');continue}
        if($p -match '^tests/AnimalTown.ContentSmoke/'){Add-Managed 'content';[void]$native.Add('content');continue}
        if($p -match '^tests/AnimalTown.Checks/(Program\.cs|[^/]+\.csproj|packages\.lock\.json)$'){
            foreach($suite in $allManaged){Add-Managed $suite}
            continue
        }
        if($p -match '^tests/AnimalTown.RenderingChecks/(Program\.cs|[^/]+\.csproj|packages\.lock\.json)$'){
            foreach($suite in $allRendering){[void]$rendering.Add($suite)}
            continue
        }
        if($p -match '^(build/|\.github/|Directory\.|global\.json$|NuGet\.Config$|\.config/|.*\.sln$|.*\.csproj$|\.gitmodules$|vendor/|tools/(build-|publish-release|patch-native|.*validation|test-changes))'){$broad=$true;$broadEmulator=$broadEmulator -or $p -notmatch '^tests/';continue}
        if($p -match '^server/'){$backend=$true;continue}
        if($p -match '^tests/AnimalTown.Checks/(OfflineJob|JobLetter|JobMail|JobTravel|JobWorld|JobMailboxCapacity|JobRecovery|WorldSimulation|VillagerJob)Checks\.cs$'){
            Add-Managed 'life';continue
        }
        if($p -match '^tests/AnimalTown.Checks/(JobFixture|VillagerJobIdentityChecks|CheckpointExportChecks|OnlineCredentialChecks)\.cs$'){
            Add-Managed 'saves';continue
        }
        if($p -match '^tests/AnimalTown.Checks/(OnlineWire|OnlinePosePump)Checks\.cs$'){
            Add-Managed 'movement';continue
        }
        if($p -match '^shared/AnimalTown.Domain/(Jobs/|GameState\.Jobs\.cs$)|^tests/native-jobs\.|^tools/generate-job-unicode\.py$'){
            Add-Managed 'life';Add-Managed 'saves';[void]$native.Add('jobs')
            if($p -notmatch '^tests/'){$backend=$true}
            continue
        }
        if($p -match '^game/AnimalTown.GameCube/TownGame\.(JobsUi|MailUi|TravelUi|Helipad)\.cs$'){
            Add-Managed 'menu';Add-Managed 'saves'
            if($p -match '\.(TravelUi|Helipad)\.cs$'){Add-Managed 'buildings';Add-Managed 'movement'}
            continue
        }
        if($p -match '^shared/AnimalTown.Domain/HelipadFlight\.cs$'){
            Add-Managed 'buildings';Add-Managed 'movement';continue
        }
        if($p -match '^game/AnimalTown.GameCube/(OnlinePairing|OnlineDeviceLink|OnlineCredentialCard)\.cs$'){
            Add-Managed 'menu';Add-Managed 'ui';Add-Managed 'saves'
            if($p -match 'OnlineCredentialCard\.cs$'){[void]$native.Add('card')}
            continue
        }
        if($p -match '^game/AnimalTown.GameCube/OnlineGameplayConnection\.cs$|^shared/AnimalTown.Domain/Online/Online(?:Wire|PosePump)\.cs$'){
            Add-Managed 'movement'
            if($p -match 'OnlineWire\.cs$'){$backend=$true}
            continue
        }
        if($p -match '^shared/AnimalTown.Domain/(Online/(OnlineCredentialStore|TownCheckpointExport)|SaveRepository|TownDataDeletion)\.cs$'){
            Add-Managed 'saves';[void]$native.Add('card')
            if($p -match 'TownCheckpointExport\.cs$'){$backend=$true}
            continue
        }
        if($p -match '^assets/(definitions/buildings/helipad\.json|models/helipad/)|^tools/helipad-vector\.py$|^game/AnimalTown.GameCube/HelipadRenderer\.Deck\.cs$'){
            Add-Managed 'buildings';[void]$native.Add('helipad');[void]$emulator.Add('job-helipad');continue
        }
        if($p -match '^(shared/AnimalTown.Domain/Content(?:Paths|Identity)|game/AnimalTown.GameCube/ContentBootstrap|tests/AnimalTown.Checks/ContentChecks)\.cs$'){Add-Managed 'content';[void]$native.Add('content');continue}
        if($p -match '^tests/(AnimalTown.ContentSmoke/|native-content-startup\.)'){Add-Managed 'content';[void]$native.Add('content');continue}
        if($p -match '^(shared/AnimalTown.Domain/TownChat|game/AnimalTown.GameCube/TownGame\.Chat|tests/AnimalTown.Checks/TownChatChecks)\.cs$'){
            Add-Managed 'menu';Add-Managed 'text';Add-Managed 'dialogue';continue
        }
        if($p -match '^tools/AnimalTown.Tools/DepotPackage(?:Checks)?\.cs$'){[void]$tooling.Add('depot');continue}
        if($p -match '^(shared/AnimalTown.Domain/WaterTextureCatalog|game/AnimalTown.GameCube/WaterTextureAssets|tools/AnimalTown.Tools/WaterTextureArt(?:Checks)?)\.cs$|^assets/textures/environment/water/'){
            [void]$tooling.Add('water');[void]$tooling.Add('depot');[void]$rendering.Add('water');continue
        }
        if($p -match '^(tools|tests)/release-bundle\.py$'){[void]$native.Add('release-bundle');continue}
        if($p -eq 'tests/water-optics-smoke.py'){[void]$native.Add('water-smoke');continue}
        if($p -match '^(tools/dolphin_network|tests/dolphin-network)\.py$'){[void]$native.Add('dolphin-network');continue}
        if($p -eq 'tests/frame-profile-streaming.py'){[void]$native.Add('frame-profile');continue}
        if($p -match '^tools/(patch-water-(probe|optics)\.py|native-water-(probe|optics)\.inc)$|^tests/native-water-(probe|optics)\.c$'){
            [void]$rendering.Add('water');[void]$native.Add('patched');continue
        }
        if($p -match '^(shared/AnimalTown.Domain/WorldLighting|game/AnimalTown.GameCube/(CloudShadows|NativeShadows|TownLights)|tests/AnimalTown.Checks/WorldLightingChecks)\.cs$'){
            Add-Managed 'core';[void]$native.Add('shadows')
            if($p -match '/NativeShadows\.cs$'){[void]$native.Add('patched')}
            continue
        }
        if($p -match '^game/AnimalTown.GameCube/(SaveCards|CardArchive|VillagerCardFiles|CardSettingsStorage|CardSaveStorage|CardSaveError|TownDataStorage|UserOptionsStorage|DevConfigStorage|TownSession|TitleScreen\.Cards)\.cs$'){
            Add-Managed 'saves';[void]$native.Add('card')
            if($p -match '(DevConfigStorage|TownSession|TitleScreen\.Cards)'){Add-Managed 'menu'}
            if($p -match '(TownSession|TitleScreen\.Cards)'){Add-Managed 'ui'}
            continue
        }
        if($p -match '^(shared/AnimalTown.Domain/DevConfig|game/AnimalTown.GameCube/DevConfigStorage|tests/AnimalTown.Checks/DevConfigChecks)\.cs$'){Add-Managed 'saves';Add-Managed 'menu';continue}
        if($p -match '^game/AnimalTown.GameCube/(TitleScreen|TitleArt|TitleBackdrop|LoadingScreen|ProfilePicker|TownSession)\.cs$'){Add-Managed 'ui';Add-Managed 'menu';Add-Managed 'saves';continue}
        if($p -match '^tests/AnimalTown.RenderingChecks/(WaterEffect|ShallowWaterMaterial|RenderingService|MovementTerrain|GroundRing|TerrainIndex|ActorVisibility|AttentionGeometry|DroppedItem)Checks\.cs$'){
            $names=@{WaterEffect='water';ShallowWaterMaterial='water';RenderingService='services';MovementTerrain='movement';GroundRing='ground';TerrainIndex='index';ActorVisibility='visibility';AttentionGeometry='attention';DroppedItem='dropped-item'}
            [void]$rendering.Add($names[$Matches[1]]);continue
        }
        if($p -match '^(shared/AnimalTown.Domain/(WindWakerWaterPattern|WindWakerWaveField|WindWakerWaveGrid|ShallowWaterPattern|WaterDepthCache|WaterBedGeometry|GroundRingPlan|ActorFrameVisibility|AnimationFrameClock)|game/AnimalTown.GameCube/(WaterBedRenderer|LocalOceanWaves|GroundRingRenderer|GeometryStaging|TerrainIndexBuffer|VillagerAttentionRenderer))\.cs$'){
            Add-Managed 'terrain';Add-Managed 'core';continue
        }
        if($p -match '^(tools/native-terrain-(archive|recycle)\.inc|tests/native-terrain-(archive|recycle)\.c)$'){
            Add-Managed 'terrain';Add-Managed 'core';[void]$native.Add('patched');continue
        }
        if($p -match '^(shared/AnimalTown.Domain/WaterSurface|tests/AnimalTown.Checks/(WaterSurface|WaterBed)Checks)\.cs$') {Add-Managed 'terrain';continue}
        if($p -match '^(shared/AnimalTown.Domain/(TownPaths|PathShape)|tests/AnimalTown.Checks/PathChecks)\.cs$'){foreach($s in @('terrain','generation','saves','core')){Add-Managed $s};continue}
        if($p -match '^game/AnimalTown.GameCube/(PathTextureResidency|PathProfile)\.cs$'){Add-Managed 'terrain';[void]$native.Add('patched');continue}
        if($p -match '^tools/AnimalTown.Tools/PathAssets\.cs$|^assets/textures/environment/paths/'){[void]$tooling.Add('path');continue}
        if($p -match '^shared/AnimalTown.Domain/CliffShadowMap\.cs$'){Add-Managed 'terrain';[void]$native.Add('shadows');continue}
        if($p -match '^tests/AnimalTown.Checks/CliffShadowChecks\.cs$'){Add-Managed 'terrain';continue}
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
        if($p -match '^shared/AnimalTown.Domain/(TexturePixels|TextureAtlasStream|TextureResolution|FloorTexture)\.cs$'){
            Add-Managed 'core';[void]$tooling.Add('terrain');[void]$tooling.Add('repack');continue
        }
        if($p -match '^(shared/AnimalTown.Domain/(BuildingWorld|BuildingPrompt|BuildingState|BuildingSave|PortalSystem|InteriorNavigation|InteriorCamera|VillagerJourney)|tests/AnimalTown.Checks/(BuildingChecks|BuildingEntryChecks))\.cs$'){
            foreach($s in @('buildings','movement','life','saves')){Add-Managed $s};continue
        }
        if($p -match '^assets/definitions/|^assets/models/(buildings/|interiors/cottage_small/)|^assets/textures/buildings/|^tools/AnimalTown.Tools/BuildingAssets\.cs$'){
            Add-Managed 'buildings';[void]$tooling.Add('building');continue
        }
        if($p -match '^(shared/AnimalTown.Domain/DroppedItemPlan|game/AnimalTown.GameCube/DroppedItemRenderer)\.cs$'){
            Add-Managed 'core';Add-Managed 'terrain';continue
        }
        if($p -match '^assets/textures/items/|^tools/AnimalTown.Tools/DroppedItemAssets\.cs$'){
            [void]$tooling.Add('dropped-item');Add-Managed 'core';Add-Managed 'terrain';continue
        }
        if($p -match '^shared/AnimalTown.Domain/'){
            foreach($s in $allManaged){Add-Managed $s}
            foreach($group in $allResources){[void]$resources.Add($group)}
            foreach($scenario in $allEmulator){[void]$emulator.Add($scenario)}
            $backend=$true;continue
        }
        if($p -match '^(tools/native-uv|tests/native-uv)'){[void]$native.Add('uv');continue}
        if($p -match '^(tools/native-card|tests/native-card|tests/AnimalTown.CardChecks/)'){[void]$native.Add('card');Add-Managed 'saves';continue}
        if($p -match '^(tools/native-texture|tests/native-texture)'){[void]$native.Add('texture');Add-Managed 'core';continue}
        if($p -match '^(tools/native-shadow|tests/native-shadow)'){[void]$native.Add('shadows');Add-Managed 'terrain';continue}
        if($p -match '^tests/native-dialogue') {[void]$native.Add('voices');Add-Managed 'dialogue';continue}
        if($p -match '^tools/native-calendar'){Add-Managed 'life';continue}
        if($p -match '^tools/AnimalTown.Tools/(TextureRepacker|Uv)'){[void]$tooling.Add('repack');continue}
        if($p -match '^tools/AnimalTown.Tools/(Terrain|NeutralFloor|RepeatFloor)'){[void]$tooling.Add('terrain');Add-Managed 'core';Add-Managed 'terrain';continue}
        if($p -match '^assets/models/'){
            if($p -match '^assets/models/villagers/'){[void]$resources.Add('memory');[void]$resources.Add('visual')}
            continue
        } # Real imports and audited ISO assets validate other models.
        if($p -match '^assets/(terrain/|textures/environment/)'){[void]$tooling.Add('terrain');Add-Managed 'core';continue}
        if($p -match '^assets/(fonts/|textures/)|^tools/AnimalTown.Tools/Brand'){Add-Managed 'ui';Add-Managed 'text';continue}
        if($p -match '^game/'){
            if($p -match '(Terrain|Water|Tree|Texture|Model|Render|FrameProfile)'){
                Add-Managed 'core';Add-Managed 'terrain'
                if($p -match '(Texture|Model|FrameProfile)'){[void]$resources.Add('visual')}
                if($p -match '(Model|TerrainRenderer)'){[void]$resources.Add('memory')}
                if($p -match 'TerrainRenderer'){[void]$resources.Add('floor');[void]$resources.Add('snow')}
            }
            elseif($p -match 'NativeCalendar'){Add-Managed 'life'}
            elseif($p -match 'Dialogue'){Add-Managed 'dialogue'}
            elseif($p -match 'CardSave'){Add-Managed 'saves';[void]$native.Add('card')}
            elseif($p -match '(Ui|Backpack|Map)'){Add-Managed 'ui';Add-Managed 'menu';Add-Managed 'text'}
            else{
                foreach($s in $allManaged){Add-Managed $s}
                foreach($group in $allResources){[void]$resources.Add($group)}
                foreach($scenario in $allEmulator){[void]$emulator.Add($scenario)}
            }
            continue
        }
        if($p -eq 'tools/iso-smoke.py'){[void]$native.Add('water-smoke');[void]$native.Add('dolphin-network');continue}
        if($p -eq 'tools/online-review-smoke.py'){Add-Managed 'saves';continue}
        if($p -eq 'tools/online-play-smoke.py'){$backend=$true;[void]$native.Add('dolphin-network');continue}
        if($p -eq 'tools/path-study.py'){[void]$native.Add('path-study');continue}
        $broad=$true
        if($p -notmatch '^tests/'){$broadEmulator=$true}
    }
    if($broad){
        foreach($s in $allManaged){Add-Managed $s}
        foreach($r in $allRendering){[void]$rendering.Add($r)}
        foreach($group in $allResources){[void]$resources.Add($group)}
        foreach($s in @('terrain','repack','building','nkit','dropped-item','depot','water','path')){[void]$tooling.Add($s)}
        foreach($s in @('uv','card','texture','voices','frame-profile','release-bundle','water-smoke','path-study','shadows','patched','content','jobs','helipad','online','dolphin-network')){[void]$native.Add($s)}
        $backend=$true;$policy=$true
    }
    if($broadEmulator){foreach($scenario in $allEmulator){[void]$emulator.Add($scenario)}}
    if($emulator.Count){$release=$true}
    $checks=[bool]($managed.Count -or $rendering.Count -or $tooling.Count -or $native.Count -or $resources.Count -or $backend)
    [pscustomobject]@{
        schema=3;commit=$Commit;baseRef=$BaseRef;release=$release;full=$broad;checks=$checks
        managed=@($allManaged|Where-Object {$managed.Contains($_)})
        rendering=@($allRendering|Where-Object {$rendering.Contains($_)})
        resources=@($allResources|Where-Object {$resources.Contains($_)})
        emulator=@($allEmulator|Where-Object {$emulator.Contains($_)})
        tooling=@($tooling|Sort-Object);native=@($native|Sort-Object)
        backend=$backend;policy=$policy;paths=@($ChangedPaths|Sort-Object -Unique)
    }
}
