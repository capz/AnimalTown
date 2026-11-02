param([string]$Root)
$ErrorActionPreference='Stop'
$generatedPath=Join-Path $Root 'out/native/source/generated_program.c'
$generated=[IO.File]::ReadAllText($generatedPath)
$pattern='intptr_t (dnd_value_AnimalTown_GameCube_NativeCalendar_Read_[a-z0-9]+)\(void\) \{.*?\r?\n\}'
$matchesFound=[regex]::Matches($generated,$pattern,[Text.RegularExpressions.RegexOptions]::Singleline)
if ($matchesFound.Count -ne 1) { throw 'Pinned AOT RTC bridge signature changed' }
$generated=[regex]::Replace($generated,$pattern,'intptr_t $1(void) { return (intptr_t)animaltown_calendar(); }',[Text.RegularExpressions.RegexOptions]::Singleline)
$errorPattern='intptr_t (dnd_value_AnimalTown_GameCube_NativeCalendar_CardError_[a-z0-9]+)\(void\) \{.*?\r?\n\}'
if(([regex]::Matches($generated,$errorPattern,[Text.RegularExpressions.RegexOptions]::Singleline)).Count -ne 1){throw 'Pinned AOT card diagnostic signature changed'}
$generated=[regex]::Replace($generated,$errorPattern,'intptr_t $1(void) { return (intptr_t)animaltown_card_debug; }',[Text.RegularExpressions.RegexOptions]::Singleline)
$generated="extern int animaltown_calendar(void);`nextern int animaltown_card_debug;`n"+$generated
[IO.File]::WriteAllText($generatedPath,$generated)
$entryPath=Join-Path $Root 'out/native/source/main.c'
$entrySource=[IO.File]::ReadAllText($entryPath).Replace('dnd_platform_init();','dnd_platform_init(); dnd_console_set_visible(0);')
$entrySource=$entrySource.Replace('managed_heap_memory[256 * 1024]','managed_heap_memory[4 * 1024 * 1024]')
[IO.File]::WriteAllText($entryPath,$entrySource)
Copy-Item -LiteralPath (Join-Path $Root 'tools/native-calendar.c') -Destination (Join-Path $Root 'out/native/source/animaltown_calendar.c') -Force
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
