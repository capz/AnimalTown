param([string]$Root)
$ErrorActionPreference='Stop'
$nativeRoot=[IO.Path]::GetFullPath((Join-Path $Root 'out/native'))
$generatedPath=Join-Path $nativeRoot 'source/generated_program.c'
$generated=[IO.File]::ReadAllText($generatedPath)
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
$pattern='intptr_t (dnd_value_AnimalTown_GameCube_NativeCalendar_Read_[a-z0-9]+)\(void\) \{.*?\r?\n\}'
$matchesFound=[regex]::Matches($generated,$pattern,[Text.RegularExpressions.RegexOptions]::Singleline)
if ($matchesFound.Count -ne 1) { throw 'Pinned AOT RTC bridge signature changed' }
$generated=[regex]::Replace($generated,$pattern,'intptr_t $1(void) { return (intptr_t)animaltown_calendar(); }',[Text.RegularExpressions.RegexOptions]::Singleline)
$clockPattern='intptr_t (dnd_value_AnimalTown_GameCube_NativeCalendar_ReadClock_[a-z0-9]+)\(void\) \{.*?\r?\n\}'
if(([regex]::Matches($generated,$clockPattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw 'Native UI clock bridge signature changed'}
$generated=[regex]::Replace($generated,$clockPattern,'intptr_t $1(void) { return (intptr_t)animaltown_clock(); }',[Text.RegularExpressions.RegexOptions]::Singleline)
$generated="extern int animaltown_clock(void);`n"+$generated
$errorPattern='intptr_t (dnd_value_AnimalTown_GameCube_NativeCalendar_CardError_[a-z0-9]+)\(void\) \{.*?\r?\n\}'
if(([regex]::Matches($generated,$errorPattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw 'Pinned AOT card diagnostic signature changed'}
$generated=[regex]::Replace($generated,$errorPattern,'intptr_t $1(void) { return (intptr_t)animaltown_card_debug; }',[Text.RegularExpressions.RegexOptions]::Singleline)
$generated="extern int animaltown_calendar(void);`nextern int animaltown_card_debug;`n"+$generated
[IO.File]::WriteAllText($generatedPath,$generated)
$uvPattern='intptr_t (dnd_value_AnimalTown_GameCube_NativeRenderProbe_Uv_[a-z0-9]+)\(intptr_t (\w+)\) \{.*?\r?\n\}'
if(([regex]::Matches($generated,$uvPattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw 'Native UV diagnostic bridge signature changed'}
$generated=[regex]::Replace($generated,$uvPattern,'intptr_t $1(intptr_t $2) { return animaltown_uv_mode((int)$2); }',[Text.RegularExpressions.RegexOptions]::Singleline)
$generated="extern int animaltown_uv_mode(int);`n"+$generated
[IO.File]::WriteAllText($generatedPath,$generated)
# Pinned, game-owned coverage union bridge. All buffers use SDK resource
# accounting; shader hooks shade existing ground geometry exactly once.
$shadowArgs=@{Init=2;Begin=3;Mesh=5;Active=3;Prepare=0;Scope=1;Stats=1}
foreach($method in $shadowArgs.Keys){
    $n=$shadowArgs[$method];$argsPattern=if($n){((1..$n|ForEach-Object {'intptr_t (\w+)'}) -join ', ')}else{'void'}
    $p='intptr_t (dnd_value_AnimalTown_GameCube_NativeShadows_'+$method+'_[a-z0-9]+)\('+$argsPattern+'\) \{.*?\r?\n\}'
    if(([regex]::Matches($generated,$p,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw "Pinned shadow union bridge changed: $method"}
    $m=$method.ToLowerInvariant()
    $generated=[regex]::Replace($generated,$p,[Text.RegularExpressions.MatchEvaluator]{param($hit)
        $a=@();for($i=0;$i -lt $n;$i++){$cast=if(($method -eq 'Init') -or ($method -eq 'Mesh' -and $i -ge 3) -or ($method -eq 'Active' -and $i -eq 2)){'(DndArray*)'}else{'(int)'};$a+=$cast+$hit.Groups[$i+2].Value}
        'intptr_t '+$hit.Groups[1].Value+'('+$(if($n){((0..($n-1)|ForEach-Object {'intptr_t '+$hit.Groups[$_+2].Value}) -join ', ')}else{'void'})+') { return animaltown_shadows_'+$m+'('+($a -join ',')+'); }'
    },[Text.RegularExpressions.RegexOptions]::Singleline)
}
$generated=@'
#include "dnd_managed.h"
extern int animaltown_shadows_init(DndArray*,DndArray*);
extern int animaltown_shadows_begin(int,int,int);
extern int animaltown_shadows_mesh(int,int,int,DndArray*,DndArray*);
extern int animaltown_shadows_active(int,int,DndArray*);
extern int animaltown_shadows_prepare(void);
extern int animaltown_shadows_scope(int);
extern int animaltown_shadows_stats(int);
'@+"`n"+$generated
[IO.File]::WriteAllText($generatedPath,$generated)
Copy-Item (Join-Path $Root 'tools/native-shadow-union.c') (Join-Path $nativeRoot 'source/animaltown_shadow_union.c') -Force
Copy-Item (Join-Path $Root 'tools/native-shadow-union.h') (Join-Path $nativeRoot 'include/animaltown_shadow_union.h') -Force
Copy-Item (Join-Path $Root 'tools/native-shadow-union-gx.inc') (Join-Path $nativeRoot 'source/animaltown_shadow_union_gx.inc') -Force
$gxPath=Join-Path $nativeRoot 'source/graphics_gx.c';$gx=[IO.File]::ReadAllText($gxPath)
$gxSignature='void dnd_draw_backend_vertices(const DndDrawVertex *v,int count,int primitive,const DndDrawTexture *texture,const DndDrawMaterial *material,const float *world) {'
if(([regex]::Matches($gx,[regex]::Escape($gxSignature))).Count -ne 1){throw 'Pinned shadow ground scope changed'}
$gx=$gx.Replace($gxSignature,"#include `"animaltown_shadow_union_gx.inc`"`n"+$gxSignature)
$gxHook='    if(!draw_state(texture,material,world,&state))return;'
if(([regex]::Matches($gx,[regex]::Escape($gxHook))).Count -ne 1){throw 'Pinned direct ground shadow hook changed'}
$gx=$gx.Replace($gxHook,$gxHook+"`n    town_shadow_ground(v,NULL,count,material,&state.bindings);")
[IO.File]::WriteAllText($gxPath,$gx)
$meshPath=Join-Path $nativeRoot 'include/dnd_indexed_mesh.inc';$mesh=[IO.File]::ReadAllText($meshPath)
$meshHook='    if(!draw_state(texture,material,world,&state))return true;'
if(([regex]::Matches($mesh,[regex]::Escape($meshHook))).Count -ne 1){throw 'Pinned indexed ground shadow hook changed'}
$mesh=$mesh.Replace($meshHook,$meshHook+"`n    town_shadow_ground(vertices,indices,count,material,&state.bindings);")
[IO.File]::WriteAllText($meshPath,$mesh)
$modelGraphicsPath=Join-Path $Root 'out/native/source/model_graphics.c'
$modelGraphics=[IO.File]::ReadAllText($modelGraphicsPath)
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
Copy-Item -LiteralPath (Join-Path $Root 'tools/native-rim.inc') -Destination (Join-Path $Root 'out/native/source/animaltown_rim.inc') -Force
$indexedPath=Join-Path $Root 'out/native/include/dnd_indexed_model.inc'
$indexed=[IO.File]::ReadAllText($indexedPath)
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
Copy-Item -LiteralPath (Join-Path $Root 'tools/native-uv-debug.inc') -Destination (Join-Path $Root 'out/native/source/animaltown_uv_debug.inc') -Force
Copy-Item -LiteralPath (Join-Path $Root 'tools/native-uv-topology.h') -Destination (Join-Path $Root 'out/native/source/animaltown_uv_topology.h') -Force
$entryPath=Join-Path $Root 'out/native/source/main.c'
$entrySource=[IO.File]::ReadAllText($entryPath)
foreach($signature in @('dnd_platform_init();','managed_heap_memory[DND_MANAGED_HEAP_BYTES]')){
    if(([regex]::Matches($entrySource,[regex]::Escape($signature))).Count -ne 1){throw "Pinned native entry signature changed: $signature"}
}
$entrySource=$entrySource.Replace('dnd_platform_init();','dnd_platform_init(); dnd_console_set_visible(0);')
$makePath=Join-Path $nativeRoot 'Makefile'
$makeSource=[IO.File]::ReadAllText($makePath)
$heapSetting='DND_MANAGED_HEAP_BYTES ?= 262144'
if(([regex]::Matches($makeSource,[regex]::Escape($heapSetting))).Count -ne 1){throw 'Pinned SDK managed heap setting changed'}
$makeSource=$makeSource.Replace($heapSetting,'DND_MANAGED_HEAP_BYTES ?= 4194304')
[IO.File]::WriteAllText($makePath,$makeSource)
[IO.File]::WriteAllText($entryPath,$entrySource)
Copy-Item -LiteralPath (Join-Path $Root 'tools/native-calendar.c') -Destination (Join-Path $Root 'out/native/source/animaltown_calendar.c') -Force
Copy-Item -LiteralPath (Join-Path $Root 'tools/native-texture-pages.c') -Destination (Join-Path $Root 'out/native/source/animaltown_texture_pages.c') -Force
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
$cardPath=Join-Path $Root 'out/native/source/storage_gamecube.c'
$cardSource=[IO.File]::ReadAllText($cardPath)
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
$storagePath=Join-Path $Root 'out/native/source/storage.c'
$storageSource="extern int animaltown_card_debug;`n"+[IO.File]::ReadAllText($storagePath)
$storageSource=$storageSource.Replace('fail(-rc, "Memory card write failed.");','{ animaltown_card_debug=animaltown_card_debug*1000-rc; fail(-rc, "Memory card write failed."); }')
[IO.File]::WriteAllText($storagePath,$storageSource)
