param([string]$Root,[string]$NativeDirectory='out/native',[switch]$SkipTests)
$ErrorActionPreference='Stop'
$nativeRoot=[IO.Path]::GetFullPath((Join-Path $Root $NativeDirectory))
if(!$nativeRoot.StartsWith([IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::Ordinal)){throw 'Native bundle must stay inside the workspace'}
$generatedPath=Join-Path $nativeRoot 'source/generated_program.c'
# Forward sparse acceptance markers before the SDK's overlay truncates them.
& python3 (Join-Path $Root 'tools/patch-residency-trace.py') $nativeRoot
if($LASTEXITCODE){throw 'Pinned residency trace bridge patch failed'}
if($IsLinux -and !$SkipTests){
    foreach($target in @('host','gekko')){
        $check=Join-Path $Root "out/native-residency-trace-$target-check"
        [string[]]$traceFlags=@()
        if($target -eq 'gekko'){$traceFlags=@('-DGEKKO',"-I$(Join-Path $Root 'tests/residency-trace-sdk')")}
        & cc -std=c11 -O2 -Wall -Wextra -Werror -ffunction-sections -fdata-sections @traceFlags "-I$(Join-Path $nativeRoot 'include')" "-I$(Join-Path $nativeRoot 'source')" (Join-Path $Root 'tests/native-residency-trace.c') '-Wl,--gc-sections' -lm -o $check
        if($LASTEXITCODE){throw "Native residency trace $target compilation failed"}
        & $check
        if($LASTEXITCODE){throw "Native residency trace $target fixture failed"}
    }
}
# Keep the existing DNDREC01 format while allowing a bounded large mail archive.
$cardBlocks=Join-Path $nativeRoot 'include/dnd_card_blocks.inc'
$cardSource=[IO.File]::ReadAllText($cardBlocks)
if(([regex]::Matches($cardSource,'page>256|count>256')).Count -ne 2){throw 'Pinned card-container bounds changed'}
$cardSource=$cardSource.Replace('page>256','page>2048').Replace('count>256','count>2048')
[IO.File]::WriteAllText($cardBlocks,$cardSource)
if($IsLinux -and !$SkipTests){
    $check=Join-Path $Root 'out/native-job-card-growth-check'
    & cc -std=c11 -Wall -Wextra -Werror "-I$(Join-Path $nativeRoot 'include')" (Join-Path $Root 'tests/native-job-card-growth.c') -o $check
    if($LASTEXITCODE){throw 'Extended card block bounds compilation failed'}
    & $check
    if($LASTEXITCODE){throw 'Extended card block bounds fixture failed'}
}
$generated=[IO.File]::ReadAllText($generatedPath)
$blockBridge='intptr_t (dnd_value_AnimalTown_GameCube_CardBlockBridge_Io_[a-z0-9]+)\(intptr_t (\w+), intptr_t (\w+), intptr_t (\w+), intptr_t (\w+), intptr_t (\w+), intptr_t (\w+)\) \{.*?\r?\n\}'
if(([regex]::Matches($generated,$blockBridge,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw 'Card container bridge signature changed'}
$generated=[regex]::Replace($generated,$blockBridge,'intptr_t $1(intptr_t $2,intptr_t $3,intptr_t $4,intptr_t $5,intptr_t $6,intptr_t $7) { return dnd_card_blocks_io((int)$2,(DndString*)$3,(int)$4,(int)$5,(int)$6,(DndArray*)$7); }',[Text.RegularExpressions.RegexOptions]::Singleline)
$generated="#include `"dnd_storage.h`"`nextern int dnd_card_blocks_io(int,DndString*,int,int,int,DndArray*);`n"+$generated
# A direct float-to-byte expression currently retains a floating SSA type in
# the pinned compiler. On a byte[] that silently stores the low IEEE bits,
# producing an invisible shadow mask. The byte-returning Opacity boundary must
# keep BOTH atlas writes integer; check the real bundled native output.
foreach($mask in @('GroundShadow_Atlas','VillagerShadowPatch_Texture')){
    $atlasPattern='intptr_t dnd_value_AnimalTown_Domain_AnimalTown_'+$mask+'_[a-z0-9]+\(void\) \{.*?\r?\n\}'
    $atlasMatches=[regex]::Matches($generated,$atlasPattern,[Text.RegularExpressions.RegexOptions]::Singleline)
    if($atlasMatches.Count -ne 1 -or $atlasMatches[0].Value -match 'dnd_array_store_r[48]_fast' -or
       ([regex]::Matches($atlasMatches[0].Value,'dnd_array_store_u1_fast')).Count -ne 2){
        throw "Native shadow mask $mask requires two integer byte stores; preserve the Opacity byte-return boundary"
    }
}
# Cliff chunks contain only byte/boolean array writes. Keep them integer too.
$cliffPattern='intptr_t dnd_value_AnimalTown_Domain_AnimalTown_CliffShadowMap_Advance_[a-z0-9]+\(intptr_t \w+\) \{.*?\r?\n\}'
$cliffStores=[regex]::Matches($generated,$cliffPattern,[Text.RegularExpressions.RegexOptions]::Singleline)
if($cliffStores.Count -ne 1 -or $cliffStores[0].Value -match 'dnd_array_store_r[48]_fast' -or
   ([regex]::Matches($cliffStores[0].Value,'dnd_array_store_u1_fast')).Count -ne 2){
    throw 'Cliff shadow tiles require two integer byte stores'
}
$pattern='intptr_t (dnd_value_AnimalTown_GameCube_NativeCalendar_Read_[a-z0-9]+)\(void\) \{.*?\r?\n\}'
$matchesFound=[regex]::Matches($generated,$pattern,[Text.RegularExpressions.RegexOptions]::Singleline)
if ($matchesFound.Count -ne 1) { throw 'Pinned AOT RTC bridge signature changed' }
$generated=[regex]::Replace($generated,$pattern,'intptr_t $1(void) { return (intptr_t)animaltown_calendar(); }',[Text.RegularExpressions.RegexOptions]::Singleline)
$clockPattern='intptr_t (dnd_value_AnimalTown_GameCube_NativeCalendar_ReadClock_[a-z0-9]+)\(void\) \{.*?\r?\n\}'
if(([regex]::Matches($generated,$clockPattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw 'Native UI clock bridge signature changed'}
$generated=[regex]::Replace($generated,$clockPattern,'intptr_t $1(void) { return (intptr_t)animaltown_clock(); }',[Text.RegularExpressions.RegexOptions]::Singleline)
$generated="extern int animaltown_clock(void);`n"+$generated
$secondPattern='intptr_t (dnd_value_AnimalTown_GameCube_NativeCalendar_ReadSecond_[a-z0-9]+)\(void\) \{.*?\r?\n\}'
if(([regex]::Matches($generated,$secondPattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw 'Native dated-second clock bridge signature changed'}
$generated=[regex]::Replace($generated,$secondPattern,'intptr_t $1(void) { return (intptr_t)animaltown_clock_second(); }',[Text.RegularExpressions.RegexOptions]::Singleline)
$generated="extern int animaltown_clock_second(void);`n"+$generated
$errorPattern='intptr_t (dnd_value_AnimalTown_GameCube_NativeCalendar_CardError_[a-z0-9]+)\(void\) \{.*?\r?\n\}'
if(([regex]::Matches($generated,$errorPattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw 'Pinned AOT card diagnostic signature changed'}
$generated=[regex]::Replace($generated,$errorPattern,'intptr_t $1(void) { return (intptr_t)animaltown_card_debug; }',[Text.RegularExpressions.RegexOptions]::Singleline)
foreach($diagnostic in @(@('CardMountError','animaltown_card_mount_error'),@('CardRecoveryError','animaltown_card_recovery_error'))){
    $diagnosticPattern='intptr_t (dnd_value_AnimalTown_GameCube_NativeCalendar_'+$diagnostic[0]+'_[a-z0-9]+)\(void\) \{.*?\r?\n\}'
    if(([regex]::Matches($generated,$diagnosticPattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw 'Pinned AOT card mount diagnostic signature changed'}
    $generated=[regex]::Replace($generated,$diagnosticPattern,('intptr_t $1(void) { return (intptr_t)'+$diagnostic[1]+'; }'),[Text.RegularExpressions.RegexOptions]::Singleline)
    $generated='extern int '+$diagnostic[1]+";`n"+$generated
}
$generated="extern int animaltown_calendar(void);`nextern int animaltown_card_debug;`n"+$generated
[IO.File]::WriteAllText($generatedPath,$generated)
$uvPattern='intptr_t (dnd_value_AnimalTown_GameCube_NativeRenderProbe_Uv_[a-z0-9]+)\(intptr_t (\w+)\) \{.*?\r?\n\}'
if(([regex]::Matches($generated,$uvPattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw 'Native UV diagnostic bridge signature changed'}
$generated=[regex]::Replace($generated,$uvPattern,'intptr_t $1(intptr_t $2) { return animaltown_uv_mode((int)$2); }',[Text.RegularExpressions.RegexOptions]::Singleline)
$generated="extern int animaltown_uv_mode(int);`n"+$generated
[IO.File]::WriteAllText($generatedPath,$generated)
# Pinned, game-owned coverage union bridge. All buffers use SDK resource
# accounting; shader hooks shade existing ground geometry exactly once.
$shadowArgs=@{Init=3;Cloud=1;Snow=12;Lights=3;Points=2;Objects=1;Environment=2;Shutdown=0;Terrain=3;Begin=3;Mesh=5;Active=3;Prepare=1;Scope=1;Stats=1}
foreach($method in $shadowArgs.Keys){
    $n=$shadowArgs[$method];$argsPattern=if($n){((1..$n|ForEach-Object {'intptr_t (\w+)'}) -join ', ')}else{'void'}
    $p='intptr_t (dnd_value_AnimalTown_GameCube_NativeShadows_'+$method+'_[a-z0-9]+)\('+$argsPattern+'\) \{.*?\r?\n\}'
    if(([regex]::Matches($generated,$p,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw "Pinned shadow union bridge changed: $method"}
    $m=$method.ToLowerInvariant()
    $generated=[regex]::Replace($generated,$p,[Text.RegularExpressions.MatchEvaluator]{param($hit)
        $a=@();for($i=0;$i -lt $n;$i++){$cast=if(($method -in @('Init','Cloud')) -or ($method -eq 'Snow' -and $i -lt 4) -or ($method -in @('Terrain','Lights','Points') -and $i -eq 0) -or ($method -eq 'Mesh' -and $i -ge 3) -or ($method -eq 'Active' -and $i -eq 2)){'(DndArray*)'}else{'(int)'};$a+=$cast+$hit.Groups[$i+2].Value}
        'intptr_t '+$hit.Groups[1].Value+'('+$(if($n){((0..($n-1)|ForEach-Object {'intptr_t '+$hit.Groups[$_+2].Value}) -join ', ')}else{'void'})+') { return animaltown_shadows_'+$m+'('+($a -join ',')+'); }'
    },[Text.RegularExpressions.RegexOptions]::Singleline)
}
$generated=@'
#include "dnd_managed.h"
extern int animaltown_shadows_init(DndArray*,DndArray*,DndArray*);
extern int animaltown_shadows_cloud(DndArray*);
extern int animaltown_shadows_snow(DndArray*,DndArray*,DndArray*,DndArray*,int,int,int,int,int,int,int,int);
extern int animaltown_shadows_lights(DndArray*,int,int);
extern int animaltown_shadows_points(DndArray*,int);
extern int animaltown_shadows_objects(int);
extern int animaltown_shadows_environment(int,int);
extern int animaltown_shadows_shutdown(void);
extern int animaltown_shadows_terrain(DndArray*,int,int);
extern int animaltown_shadows_begin(int,int,int);
extern int animaltown_shadows_mesh(int,int,int,DndArray*,DndArray*);
extern int animaltown_shadows_active(int,int,DndArray*);
extern int animaltown_shadows_prepare(int);
extern int animaltown_shadows_scope(int);
extern int animaltown_shadows_stats(int);
'@+"`n"+$generated
[IO.File]::WriteAllText($generatedPath,$generated)
$pathPattern='intptr_t (dnd_value_AnimalTown_GameCube_NativePaths_Prepare_[a-z0-9]+)\(intptr_t ([a-zA-Z0-9_]+), intptr_t ([a-zA-Z0-9_]+), intptr_t ([a-zA-Z0-9_]+), intptr_t ([a-zA-Z0-9_]+), intptr_t ([a-zA-Z0-9_]+)\) \{.*?\r?\n\}'
if(([regex]::Matches($generated,$pathPattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw 'Native path mask bridge signature changed'}
$generated=[regex]::Replace($generated,$pathPattern,'intptr_t $1(intptr_t $2, intptr_t $3, intptr_t $4, intptr_t $5, intptr_t $6) { return animaltown_paths_prepare((DndArray*)$2,(DndArray*)$3,(int)$4,(int)$5,(int)$6); }',[Text.RegularExpressions.RegexOptions]::Singleline)
$generated="#include `"dnd_managed.h`"`nextern int animaltown_paths_prepare(DndArray*,DndArray*,int,int,int);`n"+$generated
[IO.File]::WriteAllText($generatedPath,$generated)
Copy-Item (Join-Path $Root 'tools/native-path-mask.c') (Join-Path $nativeRoot 'source/animaltown_path_mask.c') -Force
Copy-Item (Join-Path $Root 'tools/native-path-mask.h') (Join-Path $nativeRoot 'include/animaltown_path_mask.h') -Force
Copy-Item (Join-Path $Root 'tools/native-shadow-union.c') (Join-Path $nativeRoot 'source/animaltown_shadow_union.c') -Force
Copy-Item (Join-Path $Root 'tools/native-shadow-union.h') (Join-Path $nativeRoot 'include/animaltown_shadow_union.h') -Force
Copy-Item (Join-Path $Root 'tools/native-shadow-union-gx.inc') (Join-Path $nativeRoot 'source/animaltown_shadow_union_gx.inc') -Force
Copy-Item (Join-Path $Root 'tools/native-world-diagnostics.inc') (Join-Path $nativeRoot 'source/animaltown_world_diagnostics.inc') -Force
foreach($method in @('View','PrepareEdges','EdgePass')){
    $count=if($method -eq 'View'){4}else{1}
    $argsPattern=((1..$count|ForEach-Object {'intptr_t (\w+)'}) -join ', ')
    $pattern='intptr_t (dnd_value_AnimalTown_GameCube_NativeWorldDiagnostics_'+$method+'_[a-z0-9]+)\('+$argsPattern+'\) \{.*?\r?\n\}'
    if(([regex]::Matches($generated,$pattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw "Native world diagnostic bridge changed: $method"}
    $native='animaltown_diagnostics_'+$method.ToLowerInvariant()
    $types=((1..$count|ForEach-Object {'int'}) -join ',')
    $generated=[regex]::Replace($generated,$pattern,[Text.RegularExpressions.MatchEvaluator]{param($hit)
        $args=2..($count+1)|ForEach-Object {$hit.Groups[$_].Value}
        'intptr_t '+$hit.Groups[1].Value+'('+((@($args)|ForEach-Object {'intptr_t '+$_}) -join ',')+') { return '+$native+'('+((@($args)|ForEach-Object {'(int)'+$_}) -join ',')+'); }'
    },[Text.RegularExpressions.RegexOptions]::Singleline)
    $generated="extern int $native($types);`n"+$generated
}
[IO.File]::WriteAllText($generatedPath,$generated)
$gxPath=Join-Path $nativeRoot 'source/graphics_gx.c';$gx=[IO.File]::ReadAllText($gxPath)
$gxSignature='void dnd_draw_backend_vertices(const DndDrawVertex *v,int count,int primitive,const DndDrawTexture *texture,const DndDrawMaterial *material,const float *world) {'
if(([regex]::Matches($gx,[regex]::Escape($gxSignature))).Count -ne 1){throw 'Pinned shadow ground scope changed'}
$gx=$gx.Replace($gxSignature,"#include `"animaltown_shadow_union_gx.inc`"`nextern void animaltown_shadows_model_reset(void);`n#include `"animaltown_world_diagnostics.inc`"`n"+$gxSignature)
$gxHook='    if(!draw_state(texture,material,world,&state))return;'
if(([regex]::Matches($gx,[regex]::Escape($gxHook))).Count -ne 1){throw 'Pinned direct ground shadow hook changed'}
$gx=$gx.Replace($gxHook,$gxHook+"`n    town_shadow_ground(v,NULL,count,texture,material,&state.bindings);`n    if(town_debug_collecting&&primitive==GX_TRIANGLES&&!(material->flags&27))animaltown_diagnostics_capture(v,sizeof(*v),NULL,0,count,&state.modelview[0][0],town_debug_mirrored(world),1);")
$directEnd="    GX_End();`n}`n#include `"dnd_indexed_mesh.inc`""
if(([regex]::Matches($gx,[regex]::Escape($directEnd))).Count -ne 1){throw 'Pinned direct diagnostic geometry hook changed'}
$gx=$gx.Replace($directEnd,"    GX_End();`n    if(primitive==GX_TRIANGLES)town_debug_overlay(v,NULL,count,state.modelview);`n}`n#include `"dnd_indexed_mesh.inc`"")
[IO.File]::WriteAllText($gxPath,$gx)
$meshPath=Join-Path $nativeRoot 'include/dnd_indexed_mesh.inc';$mesh=[IO.File]::ReadAllText($meshPath)
$meshHook='    if(!draw_state(texture,material,world,&state))return true;'
if(([regex]::Matches($mesh,[regex]::Escape($meshHook))).Count -ne 1){throw 'Pinned indexed ground shadow hook changed'}
$mesh=$mesh.Replace($meshHook,$meshHook+"`n    town_shadow_ground(vertices,indices,count,texture,material,&state.bindings);`n    if(town_debug_collecting&&!(material->flags&27))animaltown_diagnostics_capture(vertices,sizeof(*vertices),indices,indices?2:0,count,&state.modelview[0][0],town_debug_mirrored(world),0);")
$indexedEnd='    /* Preserve material state across indexed draws.'
if(([regex]::Matches($mesh,[regex]::Escape($indexedEnd))).Count -ne 1){throw 'Pinned indexed diagnostic geometry hook changed'}
$mesh=$mesh.Replace($indexedEnd,"    town_debug_overlay(vertices,indices,count,state.modelview);`n"+$indexedEnd)
[IO.File]::WriteAllText($meshPath,$mesh)
$modelGraphicsPath=Join-Path $nativeRoot 'source/model_graphics.c'
$modelGraphics=[IO.File]::ReadAllText($modelGraphicsPath)
$modelEdgeHook='    GX_SetCullMode(mat->two          ? GX_CULL_NONE'
if(([regex]::Matches($modelGraphics,[regex]::Escape($modelEdgeHook))).Count -ne 1){throw 'Pinned obstructed model edge hook changed'}
$modelGraphics=$modelGraphics.Replace($modelEdgeHook,"    if(!translucent)animaltown_diagnostics_capture(mesh->skin>=0?(const void*)in->vertices[mi]:(const void*)mesh->data,mesh->skin>=0?24:80,mesh->index,4,mesh->indices,&matrix[0][0],determinant<0,0);`n"+$modelEdgeHook)
$modelGraphics="extern void animaltown_diagnostics_capture(const void*,unsigned,const void*,unsigned,unsigned,const float*,int,int);`n"+$modelGraphics
# Keep the game-owned rim hook pinned to the imported indexed model path. Fail
# closed if a package upgrade changes a hook rather than silently CPU-expanding.
$rimHooks=@{
    '#include "dnd_indexed_model.inc"'='#include "dnd_indexed_model.inc"'
    '    if(!reuse)dnd_draw_backend_tev(selected, mat->texture >= 0);'="    if(!reuse)dnd_draw_backend_tev(selected, mat->texture >= 0);`n    if(town_rim_enabled&&!effect)town_rim_apply(normal,selected,reuse,&bindings);"
    'if(!(mat->mode&2)){Mtx n;'='if(town_rim_enabled||!(mat->mode&2)){Mtx n;'
    'if(authored||(custom_scene&&!(mat->mode&2)))GX_Normal3f32'='if(authored||town_rim_enabled||(custom_scene&&!(mat->mode&2)))GX_Normal3f32'
    '    if(custom_scene){'='    if(custom_scene||town_rim_enabled){'
    '      dnd_draw_backend_scene(!(mat->mode&2),frame_view);'='      if(custom_scene)dnd_draw_backend_scene(!(mat->mode&2),frame_view);'
}
foreach($hook in $rimHooks.Keys){
    if(([regex]::Matches($modelGraphics,[regex]::Escape($hook))).Count -ne 1){throw "Pinned native rim hook changed: $hook"}
    $modelGraphics=$modelGraphics.Replace($hook,$rimHooks[$hook])
}
$modelGraphics=$modelGraphics.Replace('int dnd_model_state_cache(int enabled){',"#include `"animaltown_rim.inc`"`nint dnd_model_state_cache(int enabled){")
$modelGraphics=$modelGraphics.Replace('int dnd_model_state_cache(int enabled){',"#include `"animaltown_shadow_union_gx.inc`"`nvoid animaltown_shadows_model_reset(void){batch_model=NULL;batch_material=NULL;model_batch_vertex_ready=false;}`nint dnd_model_state_cache(int enabled){")
$shadowModelHook='    if(!reuse)dnd_draw_backend_tev(selected, mat->texture >= 0);'
if(([regex]::Matches($modelGraphics,[regex]::Escape($shadowModelHook))).Count -ne 1){throw 'Pinned interior shadow hook changed'}
$modelGraphics=$modelGraphics.Replace($shadowModelHook,$shadowModelHook+"`n    if(animaltown_shadows_mode()==2)town_shadow_apply(object[12]+2*object[0]+2*object[8],object[14]+2*object[2]+2*object[10],object,selected,&bindings);")
# The rim stage is configured immediately after this material hook. Append
# environment tint after it, preserving both the model's effect and rim stages.
$environmentHook='    if(town_rim_enabled&&!effect)town_rim_apply(normal,selected,reuse,&bindings);'
$modelGraphics=$modelGraphics.Replace($environmentHook,$environmentHook+"`n    town_points_prepare(object,frame_view);`n    if(animaltown_shadows_mode()!=2){int next=town_environment_apply(selected,(selected?selected->count:1)+(town_rim_enabled&&!effect?1:0));town_point_tev(mat->texture>=0,next);}")
# Local diffuse light uses channel 1 and the existing texture, preserving the
# indexed model path and channel 0's ordinary ambient/headlight behavior.
$modelGraphics=$modelGraphics.Replace('#include "dnd_indexed_model.inc"',"#include `"animaltown_point_lights_gx.inc`"`n#include `"dnd_indexed_model.inc`"")
$pointHooks=@{
    '    if(custom_scene||town_rim_enabled){'='    if(custom_scene||town_rim_enabled||town_points_active()){'
    'if(town_rim_enabled||!(mat->mode&2)){Mtx n;'='if(town_rim_enabled||town_points_active()||!(mat->mode&2)){Mtx n;'
    '    for (unsigned first = 0; first < mesh->indices;)'=@'
    town_point_channels(GX_SRC_VTX,(GXColor){255,255,255,255});
    if(town_points_active()){GX_SetVtxDesc(GX_VA_CLR1,GX_DIRECT);GX_SetVtxAttrFmt(GX_VTXFMT0,GX_VA_CLR1,GX_CLR_RGBA,GX_RGBA8,0);}
    for (unsigned first = 0; first < mesh->indices;)
'@
    '        if(authored||town_rim_enabled||(custom_scene&&!(mat->mode&2)))GX_Normal3f32(v[3],v[4],v[5]);'=@'
        if(town_points_active()){
          float n[3];for(int r=0;r<3;r++)n[r]=normal[r*4]*v[3]+normal[r*4+1]*v[4]+normal[r*4+2]*v[5];
          float length=sqrtf(n[0]*n[0]+n[1]*n[1]+n[2]*n[2]);float scale=length>0?1/length:0;
          GX_Normal3f32(v[3]*scale,v[4]*scale,v[5]*scale);
        }else if(authored||town_rim_enabled||(custom_scene&&!(mat->mode&2)))GX_Normal3f32(v[3],v[4],v[5]);
'@
    '        GX_Color4u8(color[0], color[1], color[2], color[3]);'=@'
        GX_Color4u8(color[0], color[1], color[2], color[3]);
        if(town_points_active()){
          u8 reflectance[4];for(int c=0;c<4;c++)reflectance[c]=(u8)(fminf(1,fmaxf(0,dnd_model_f32(src+32+c*4)*mat->color[c]))*255);
          GX_Color4u8(reflectance[0],reflectance[1],reflectance[2],reflectance[3]);
        }
'@
}
foreach($hook in $pointHooks.Keys){
    if(([regex]::Matches($modelGraphics,[regex]::Escape($hook))).Count -ne 1){throw "Pinned model point lighting hook changed: $hook"}
    $modelGraphics=$modelGraphics.Replace($hook,$pointHooks[$hook])
}
Copy-Item (Join-Path $Root 'tools/native-point-lights-gx.inc') (Join-Path $nativeRoot 'source/animaltown_point_lights_gx.inc') -Force
Copy-Item (Join-Path $Root 'tools/native-point-lights.h') (Join-Path $nativeRoot 'source/native-point-lights.h') -Force
Copy-Item -LiteralPath (Join-Path $Root 'tools/native-rim.inc') -Destination (Join-Path $nativeRoot 'source/animaltown_rim.inc') -Force
Copy-Item -LiteralPath (Join-Path $Root 'tools/native-rim-policy.h') -Destination (Join-Path $nativeRoot 'source/native-rim-policy.h') -Force
if($IsLinux -and !$SkipTests){
    & cc -std=c11 -Wall -Wextra -Werror "-I$(Join-Path $Root 'tools')" (Join-Path $Root 'tests/native-rim-policy.c') -o (Join-Path $Root 'out/native-rim-policy-check')
    if($LASTEXITCODE){throw 'Native rim policy compilation failed'}
    & (Join-Path $Root 'out/native-rim-policy-check')
    if($LASTEXITCODE){throw 'Native rim policy fixture failed'}
}
$indexedPath=Join-Path $nativeRoot 'include/dnd_indexed_model.inc'
$indexed=[IO.File]::ReadAllText($indexedPath)
$pointIndexedHooks=@{
    '  bool lit=!(material->mode&2);'='  bool material_lit=!(material->mode&2),lit=material_lit||town_points_active();'
    'GX_SetChanCtrl(GX_COLOR0,lit?GX_ENABLE:GX_DISABLE'='GX_SetChanCtrl(GX_COLOR0,material_lit?GX_ENABLE:GX_DISABLE'
    'lit?GX_LIGHT0:GX_LIGHTNULL,lit?GX_DF_CLAMP:GX_DF_NONE'='material_lit?GX_LIGHT0:GX_LIGHTNULL,material_lit?GX_DF_CLAMP:GX_DF_NONE'
    '  GX_ClearVtxDesc();if(!model_batch_active'="  town_point_channels(GX_SRC_REG,tint);`n  GX_ClearVtxDesc();if(!model_batch_active"
}
foreach($hook in $pointIndexedHooks.Keys){
    if(([regex]::Matches($indexed,[regex]::Escape($hook))).Count -ne 1){throw "Pinned indexed point lighting hook changed: $hook"}
    $indexed=$indexed.Replace($hook,$pointIndexedHooks[$hook])
}
foreach($hook in @('if(lit){GX_SetVtxDesc(GX_VA_NRM','if(lit)GX_Normal1x16(index);')){
    if(([regex]::Matches($indexed,[regex]::Escape($hook))).Count -ne 1){throw "Pinned indexed rim normals changed: $hook"}
    $indexed=$indexed.Replace($hook,$hook.Replace('if(lit)','if(lit||town_rim_enabled)'))
}
[IO.File]::WriteAllText($indexedPath,$indexed)
$rimPattern='intptr_t (dnd_value_AnimalTown_GameCube_NativeRim_Enable_[a-z0-9]+)\(intptr_t (\w+)\) \{.*?\r?\n\}'
if(([regex]::Matches($generated,$rimPattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw 'Pinned rim scope bridge changed'}
$generated=[regex]::Replace($generated,$rimPattern,'intptr_t $1(intptr_t $2) { return animaltown_rim_enable((int)$2); }',[Text.RegularExpressions.RegexOptions]::Singleline)
$generated="extern int animaltown_rim_enable(int);`n"+$generated
[IO.File]::WriteAllText($generatedPath,$generated)
$drawSignature='static int draw_model_selected(DndInstance *in,const float world[16],const DndDrawEffect *effect,int filter,int only) {'
if(([regex]::Matches($modelGraphics,[regex]::Escape($drawSignature))).Count -ne 1){throw 'Pinned model UV diagnostic hook changed'}
$modelGraphics=$modelGraphics.Replace($drawSignature,$drawSignature.Replace('draw_model_selected(','draw_model_selected_base('))
$modelGraphics+="`n#include `"animaltown_uv_debug.inc`"`n"
[IO.File]::WriteAllText($modelGraphicsPath,$modelGraphics)
Copy-Item -LiteralPath (Join-Path $Root 'tools/native-uv-debug.inc') -Destination (Join-Path $nativeRoot 'source/animaltown_uv_debug.inc') -Force
Copy-Item -LiteralPath (Join-Path $Root 'tools/native-uv-topology.h') -Destination (Join-Path $nativeRoot 'source/animaltown_uv_topology.h') -Force
$entryPath=Join-Path $nativeRoot 'source/main.c'
$entrySource=[IO.File]::ReadAllText($entryPath)
foreach($signature in @('dnd_platform_init();','managed_heap_memory[DND_MANAGED_HEAP_BYTES]')){
    if(([regex]::Matches($entrySource,[regex]::Escape($signature))).Count -ne 1){throw "Pinned native entry signature changed: $signature"}
}
$entrySource=$entrySource.Replace('dnd_platform_init();','dnd_platform_init(); dnd_console_set_visible(0);')
$makePath=Join-Path $nativeRoot 'Makefile'
$makeSource=[IO.File]::ReadAllText($makePath)
$heapSetting='DND_MANAGED_HEAP_BYTES ?= 262144'
if(([regex]::Matches($makeSource,[regex]::Escape($heapSetting))).Count -ne 1){throw 'Pinned SDK managed heap setting changed'}
# Maximum text mail plus two-card transfer readback is qualified at 6 MiB.
$makeSource=$makeSource.Replace($heapSetting,'DND_MANAGED_HEAP_BYTES ?= 6291456')
# Generated managed exceptions and GC use explicit runtime frames, not the C
# unwinder. No linked native code calls _Unwind. Avoid loading hundreds of KiB
# of unused C unwind tables beside the six-MiB heap; keep ordinary debug info.
foreach($flags in @('CFLAGS += -O3 -flto -fno-strict-aliasing','LDFLAGS += -flto')){
    if(([regex]::Matches($makeSource,[regex]::Escape($flags))).Count -ne 1){throw 'Pinned SDK native optimization flags changed'}
    $makeSource=$makeSource.Replace($flags,$flags+' -fno-asynchronous-unwind-tables -fno-unwind-tables')
}
[IO.File]::WriteAllText($makePath,$makeSource)
[IO.File]::WriteAllText($entryPath,$entrySource)
# Keep the SDK's 16 MiB byte budget; the wider terrain prefetch pool needs more
# small mesh handles. The ten-bit token index supports at most 1023 slots.
$resourcePath=Join-Path $nativeRoot 'source/draw_resources.c'
$resourceSource=[IO.File]::ReadAllText($resourcePath)
if(([regex]::Matches($resourceSource,'#define SLOTS 512')).Count -ne 1){throw 'Pinned native resource table changed'}
$resourceSource=$resourceSource.Replace('#define SLOTS 512',"#include `"native-resource-limits.h`"`n#define SLOTS ANIMALTOWN_DRAW_RESOURCE_SLOTS")
[IO.File]::WriteAllText($resourcePath,$resourceSource)
Copy-Item -LiteralPath (Join-Path $Root 'tools/native-resource-limits.h') -Destination (Join-Path $nativeRoot 'include/native-resource-limits.h') -Force
Copy-Item -LiteralPath (Join-Path $Root 'tools/native-frame-counters.h') -Destination (Join-Path $nativeRoot 'include/native-frame-counters.h') -Force
if($IsLinux -and !$SkipTests){
    $resourceCheck=Join-Path $Root 'out/native-resource-slots-check'
    & cc -std=c11 -Wall -Wextra -Werror -ffunction-sections -fdata-sections "-I$(Join-Path $nativeRoot 'include')" (Join-Path $Root 'tests/native-resource-slots.c') $resourcePath '-Wl,--gc-sections' -lm -o $resourceCheck
    if($LASTEXITCODE){throw 'Native resource-slot fixture compilation failed'}
    & $resourceCheck
    if($LASTEXITCODE){throw 'Native resource-slot fixture failed'}
}
Copy-Item -LiteralPath (Join-Path $Root 'tools/native-calendar.c') -Destination (Join-Path $nativeRoot 'source/animaltown_calendar.c') -Force
Copy-Item -LiteralPath (Join-Path $Root 'tools/native-texture-pages.c') -Destination (Join-Path $nativeRoot 'source/animaltown_texture_pages.c') -Force
$pagePattern='intptr_t (dnd_value_AnimalTown_GameCube_AnimalTown_GameCube_NativeTexturePages_IplPage_[a-z0-9]+)\(intptr_t (\w+), intptr_t (\w+), intptr_t (\w+)\) \{.*?\r?\n\}'
if(([regex]::Matches($generated,$pagePattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw 'Native IPL page bridge signature changed'}
$generated=[regex]::Replace($generated,$pagePattern,'intptr_t $1(intptr_t $2, intptr_t $3, intptr_t $4) { return animaltown_ipl_page((int)$2,(DndArray*)$3,(DndArray*)$4); }',[Text.RegularExpressions.RegexOptions]::Singleline)
$collectPattern='intptr_t (dnd_value_AnimalTown_GameCube_AnimalTown_GameCube_NativeTexturePages_Collect_[a-z0-9]+)\(void\) \{.*?\r?\n\}'
if(([regex]::Matches($generated,$collectPattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw 'Native texture collection bridge signature changed'}
$generated=[regex]::Replace($generated,$collectPattern,'intptr_t $1(void) { dnd_gc_collect(dnd_value_heap,NULL); return 0; }',[Text.RegularExpressions.RegexOptions]::Singleline)
$probePattern='intptr_t (dnd_value_AnimalTown_GameCube_NativeRenderProbe_Indexed_[a-z0-9]+)\(intptr_t (\w+)\) \{.*?\r?\n\}'
if(([regex]::Matches($generated,$probePattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw 'Native model comparison bridge signature changed'}
$generated=[regex]::Replace($generated,$probePattern,'intptr_t $1(intptr_t $2) { return dnd_model_indexed_path((int)$2); }',[Text.RegularExpressions.RegexOptions]::Singleline)
$batchPattern='intptr_t (dnd_value_AnimalTown_GameCube_NativeRenderProbe_Batch_[a-z0-9]+)\(intptr_t (\w+)\) \{.*?\r?\n\}'
if(([regex]::Matches($generated,$batchPattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw 'Native batch comparison bridge signature changed'}
$generated=[regex]::Replace($generated,$batchPattern,'intptr_t $1(intptr_t $2) { return dnd_model_state_cache((int)$2); }',[Text.RegularExpressions.RegexOptions]::Singleline)
$generated="#include `"dnd_model.h`"`n"+$generated
$generated="#include `"dnd_managed.h`"`nextern int animaltown_ipl_page(int,DndArray*,DndArray*);`n"+$generated
[IO.File]::WriteAllText($generatedPath,$generated)
# A new libogc directory entry has 0xff artwork fields. Clear those before
# committing the transaction journal's ready flag; CARD_SetStatus validates
# artwork formats even though this temporary journal has no artwork.
$cardPath=Join-Path $nativeRoot 'source/storage_gamecube.c'
$cardSource=[IO.File]::ReadAllText($cardPath)
$mountPattern='bool dnd_gc_card_mount\(int device, const char \*game, const char \*company\) \{.*?\r?\n\}'
if(([regex]::Matches($cardSource,$mountPattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw 'Pinned SDK card mount changed'}
$replacementMount=[IO.File]::ReadAllText((Join-Path $Root 'tools/native-card-mount.c.inc'))
$cardSource=[regex]::Replace($cardSource,$mountPattern,[Text.RegularExpressions.MatchEvaluator]{param($match) $replacementMount},[Text.RegularExpressions.RegexOptions]::Singleline)
$readPattern='int dnd_gc_card_read\(int device, const char \*name, void \*data, int capacity\) \{.*?\r?\n\}'
if(([regex]::Matches($cardSource,$readPattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw 'Pinned SDK card reader changed'}
$replacementReader=[IO.File]::ReadAllText((Join-Path $Root 'tools/native-card-read.c.inc'))
$cardSource=[regex]::Replace($cardSource,$readPattern,[Text.RegularExpressions.MatchEvaluator]{param($match) $replacementReader},[Text.RegularExpressions.RegexOptions]::Singleline)
$readyMarker='status.comment_addr = 0;'
if(([regex]::Matches($cardSource,[regex]::Escape($readyMarker))).Count -ne 1){throw 'Pinned SDK card journal marker changed'}
$cardSource=$cardSource.Replace($readyMarker,'status.comment_addr = 0; status.banner_fmt = CARD_BANNER_NONE; status.icon_fmt = CARD_ICON_NONE; status.icon_speed = 0; status.icon_addr = UINT32_MAX;')
$cardSource="extern int animaltown_card_debug;`n"+$cardSource
$stage=0
foreach($operation in @('txn_read','txn_write','txn_ready','txn_metadata')) {
    $stage++
    $operationPattern="static int $operation\([^\{]+\) \{"
    $cardSource=[regex]::Replace($cardSource,$operationPattern,"`$0 animaltown_card_debug=$stage;")
}
[IO.File]::WriteAllText($cardPath,$cardSource)
$storagePath=Join-Path $nativeRoot 'source/storage.c'
$storageSource="extern int animaltown_card_debug;`n"+[IO.File]::ReadAllText($storagePath)
if(([regex]::Matches($storageSource,'bool dnd_card_mount\(')).Count -ne 1){throw 'Pinned card container native hook changed'}
$storageSource=$storageSource.Replace('fail(-rc, "Memory card write failed.");','{ animaltown_card_debug=animaltown_card_debug*1000-rc; fail(-rc, "Memory card write failed."); }')
[IO.File]::WriteAllText($storagePath,$storageSource)
& python3 (Join-Path $Root 'tools/patch-managed-allocator.py') $nativeRoot
if($LASTEXITCODE){throw 'Pinned allocation/frame profiling patch failed'}
if($IsLinux -and !$SkipTests){
    foreach($fixture in @('managed-allocator','mesh-allocation','terrain-recycle','terrain-archive','terrain-reset','profile')){
        $check=Join-Path $Root "out/native-$fixture-check"
        [string[]]$extra=@()
        if($fixture -eq 'managed-allocator'){$extra=@((Join-Path $nativeRoot 'source/managed_runtime.c'))}
        if($fixture -eq 'terrain-archive'){$extra=@((Join-Path $nativeRoot 'source/aram.c'))}
        if($fixture -eq 'profile'){$extra=@((Join-Path $nativeRoot 'source/animaltown_frame_counters.c'))}
        & cc -std=c11 -O2 -Wall -Wextra -Werror -ffunction-sections -fdata-sections "-I$(Join-Path $nativeRoot 'include')" "-I$(Join-Path $nativeRoot 'source')" (Join-Path $Root "tests/native-$fixture.c") @extra '-Wl,--gc-sections' -lm -o $check
        if($LASTEXITCODE){throw "Native $fixture compilation failed"}
        if($fixture -eq 'profile'){
            $profileDump=Join-Path $Root 'out/native-profile.log'
            & $check | Set-Content -LiteralPath $profileDump
            if($LASTEXITCODE){throw 'Native profiling fixture failed'}
            & python3 (Join-Path $Root 'tools/read-profile-dump.py') $profileDump --platform unknown | Set-Content -LiteralPath (Join-Path $Root 'out/native-profile.json')
        }else{& $check}
        if($LASTEXITCODE){throw "Native $fixture checks failed"}
    }
}

& python3 (Join-Path $Root 'tools/patch-face-culling.py') $nativeRoot
if($LASTEXITCODE){throw 'Pinned backface culling patch failed'}
if($IsLinux -and !$SkipTests){
    & cc -std=c11 -Wall -Wextra -Werror "-I$(Join-Path $Root 'tools')" (Join-Path $Root 'tests/native-face-culling.c') -o (Join-Path $Root 'out/native-face-culling-check')
    if($LASTEXITCODE){throw 'Face culling fixture compilation failed'}
    & (Join-Path $Root 'out/native-face-culling-check')
    if($LASTEXITCODE){throw 'Face culling fixture failed'}
}

# Exercise the SDK's outgoing libogc argv handoff. The game no longer reads
# incoming loader arguments to choose an update image.
if($IsLinux -and !$SkipTests){
    foreach($fixture in @('launch-arguments','launch-handoff')){
        $check=Join-Path $Root "out/native-$fixture-check"
        [string[]]$extra=@();if($fixture -eq 'launch-handoff'){$extra=@((Join-Path $nativeRoot 'source/dol.c'))}
        & cc -std=c11 -Wall -Wextra -Werror -ffunction-sections -fdata-sections "-I$(Join-Path $nativeRoot 'include')" (Join-Path $Root "tests/native-$fixture.c") @extra '-Wl,--gc-sections' -o $check
        if($LASTEXITCODE){throw "Native $fixture compilation failed"}
        & $check
        if($LASTEXITCODE){throw "Native $fixture checks failed"}
    }
}

& python3 (Join-Path $Root 'tools/patch-ui-noise.py') $nativeRoot
if($LASTEXITCODE){throw 'Pinned UI noise bridge patch failed'}
if($IsLinux -and !$SkipTests){
    $uiNoiseCheck=Join-Path $Root 'out/native-ui-noise-check'
    & cc -std=c11 -Wall -Wextra -Werror -ffunction-sections -fdata-sections "-I$(Join-Path $nativeRoot 'include')" "-I$(Join-Path $nativeRoot 'source')" (Join-Path $Root 'tests/native-ui-noise.c') '-Wl,--gc-sections' -lm -o $uiNoiseCheck
    if($LASTEXITCODE){throw 'UI grain fixture compilation failed'}
    & $uiNoiseCheck
    if($LASTEXITCODE){throw 'UI grain checks failed'}
}

if($IsLinux -and !$SkipTests){
    $uiNoiseGxCheck=Join-Path $Root 'out/native-ui-noise-gx-check'
    & cc -std=c11 -Wall -Wextra -Werror "-I$(Join-Path $nativeRoot 'include')" (Join-Path $Root 'tests/native-ui-noise-gx.c') -lm -o $uiNoiseGxCheck
    if($LASTEXITCODE){throw 'UI grain GX fixture compilation failed'}
    & $uiNoiseGxCheck
    if($LASTEXITCODE){throw 'UI grain GX checks failed'}
}

& python3 (Join-Path $Root 'tools/patch-water-probe.py') $nativeRoot
if($LASTEXITCODE){throw 'Pinned water draw probe patch failed'}
if($IsLinux -and !$SkipTests){
    $waterProbeCheck=Join-Path $Root 'out/native-water-probe-check'
    & cc -std=c11 -Wall -Wextra -Werror "-I$(Join-Path $nativeRoot 'include')" (Join-Path $Root 'tests/native-water-probe.c') -lm -o $waterProbeCheck
    if($LASTEXITCODE){throw 'Water probe forwarding fixture compilation failed'}
    & $waterProbeCheck
    if($LASTEXITCODE){throw 'Water probe forwarding fixture failed'}
}

# Apply after the pinned host fixtures: console allocations report failures to
# the system-pressure controller, without altering their owning resource APIs.
& python3 (Join-Path $Root 'tools/patch-memory-pressure.py') $nativeRoot
if($LASTEXITCODE){throw 'Native MEM1 sampling/instrumentation patch failed'}
if($IsLinux){
    $memoryCheck=Join-Path $Root 'out/native-memory-pressure-check'
    & cc -std=c11 -O2 -Wall -Wextra -Werror "-I$(Join-Path $Root 'tests/memory-pressure-sdk')" "-I$(Join-Path $Root 'tools')" "-I$(Join-Path $nativeRoot 'include')" (Join-Path $Root 'tests/native-memory-pressure.c') -o $memoryCheck
    if($LASTEXITCODE){throw 'Native MEM1 fixture compilation failed'}
    & $memoryCheck
    if($LASTEXITCODE){throw 'Native MEM1 fixture failed'}
    $memoryWrappingCheck=Join-Path $Root 'out/native-memory-wrapping-check'
    & cc -std=c11 -O2 -Wall -Wextra -Werror (Join-Path $Root 'tests/native-memory-wrapping.c') -o $memoryWrappingCheck
    if($LASTEXITCODE){throw 'Native MEM1 allocation alias fixture compilation failed'}
    & $memoryWrappingCheck
    if($LASTEXITCODE){throw 'Native MEM1 allocation alias fixture failed'}
}
