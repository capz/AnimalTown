param([string]$Root)
$ErrorActionPreference='Stop'
$generatedPath=Join-Path $Root 'out/native/source/generated_program.c'
$generated=[IO.File]::ReadAllText($generatedPath)
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
$modelGraphicsPath=Join-Path $Root 'out/native/source/model_graphics.c'
$modelGraphics=[IO.File]::ReadAllText($modelGraphicsPath)
$drawSignature='static int draw_model_selected(DndInstance *in,const float world[16],const DndDrawEffect *effect,int filter,int only) {'
if(([regex]::Matches($modelGraphics,[regex]::Escape($drawSignature))).Count -ne 1){throw 'Pinned model UV diagnostic hook changed'}
$modelGraphics=$modelGraphics.Replace($drawSignature,$drawSignature.Replace('draw_model_selected(','draw_model_selected_base('))
$modelGraphics+="`n#include `"animaltown_uv_debug.inc`"`n"
[IO.File]::WriteAllText($modelGraphicsPath,$modelGraphics)
Copy-Item -LiteralPath (Join-Path $Root 'tools/native-uv-debug.inc') -Destination (Join-Path $Root 'out/native/source/animaltown_uv_debug.inc') -Force
Copy-Item -LiteralPath (Join-Path $Root 'tools/native-uv-topology.h') -Destination (Join-Path $Root 'out/native/source/animaltown_uv_topology.h') -Force
$entryPath=Join-Path $Root 'out/native/source/main.c'
$entrySource=[IO.File]::ReadAllText($entryPath).Replace('dnd_platform_init();','dnd_platform_init(); dnd_console_set_visible(0);')
$entrySource=$entrySource.Replace('managed_heap_memory[256 * 1024]','managed_heap_memory[4 * 1024 * 1024]')
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
