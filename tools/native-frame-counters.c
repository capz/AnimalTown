#include "dnd_managed.h"
#include "dnd_async.h"
#include "native-frame-counters.h"
#include <string.h>
#ifdef GEKKO
#include <ogc/system.h>
#endif
TownFrameCounters town_frame_counters;
static size_t last_collections;
void animaltown_frame_reset(int enabled,DndManagedHeap *heap){
#ifdef GEKKO
    if(enabled&&!town_frame_counters.enabled)SYS_Report("ANIMALTOWN MEMORY managed_used %u managed_peak %u managed_capacity %u collections %u\n",(unsigned)heap->used,town_frame_counters.heap_peak,(unsigned)heap->capacity,(unsigned)heap->collections);
#endif
    town_frame_counters.enabled=(unsigned)enabled;memset(town_frame_counters.count,0,sizeof(town_frame_counters.count));last_collections=heap->collections;
}
void animaltown_frame_snapshot(DndArray *data,DndManagedHeap *heap){
    if(!data||data->elements_are_references||data->element_size!=4||data->length<8)return;
    int32_t *out=(int32_t*)data->data;
    for(int i=0;i<TOWN_COUNTERS;i++)out[i]=(int32_t)town_frame_counters.count[i];
    out[7]=(int32_t)(heap->collections-last_collections);
}
void animaltown_frame_service(void){dnd_async_service();}
