/* System headroom and real allocation failures, without allocation probes,
 * free-list walks of our own, or summing overlapping resource budgets. */
#define ANIMALTOWN_MEMORY_IMPLEMENTATION
#include "native-memory-pressure.h"
#include "dnd_managed.h"
#include "dnd_draw.h"
#include <limits.h>
#include <stdint.h>
#include <stdlib.h>
#if defined(DND_GAMECUBE_STORAGE) || defined(ANIMALTOWN_MEMORY_TEST)
#include <malloc.h>
#include <ogc/system.h>
#endif

static unsigned town_memory_failures,town_memory_failed_bytes;

static unsigned town_memory_clamp(size_t bytes){
    return bytes>(size_t)INT_MAX?INT_MAX:(unsigned)bytes;
}
static void town_memory_failed(size_t bytes){
    if(!bytes)return; /* malloc(0)/realloc(p,0) may legally return NULL. */
    __atomic_store_n(&town_memory_failed_bytes,town_memory_clamp(bytes),__ATOMIC_RELAXED);
    unsigned previous=__atomic_load_n(&town_memory_failures,__ATOMIC_RELAXED);
    while(previous<INT_MAX&&!__atomic_compare_exchange_n(&town_memory_failures,&previous,previous+1,0,__ATOMIC_RELEASE,__ATOMIC_RELAXED)){}
}
void *animaltown_memory_malloc(size_t bytes){
    void *result=malloc(bytes);if(!result)town_memory_failed(bytes);return result;
}
void *animaltown_memory_calloc(size_t count,size_t bytes){
    void *result=calloc(count,bytes);
    if(!result)town_memory_failed(count&&bytes>SIZE_MAX/count?SIZE_MAX:count*bytes);
    return result;
}
void *animaltown_memory_realloc(void *previous,size_t bytes){
    void *result=realloc(previous,bytes);if(!result)town_memory_failed(bytes);return result;
}
void *animaltown_memory_memalign(size_t alignment,size_t bytes){
#if defined(DND_GAMECUBE_STORAGE) || defined(ANIMALTOWN_MEMORY_TEST)
    void *result=memalign(alignment,bytes);
#else
    /* Host bundles are not gameplay targets. Keep this bridge buildable with
     * ISO C and the caller's requested alignment for native host checks. */
    void *result=NULL;
    if(alignment&&!(alignment&(alignment-1))&&bytes<=SIZE_MAX-(alignment-1))
        result=aligned_alloc(alignment,(bytes+alignment-1)&~(alignment-1));
#endif
    if(!result)town_memory_failed(bytes);
    return result;
}

int animaltown_memory_read(DndArray *snapshot){
    if(dnd_device_active()||!snapshot||snapshot->elements_are_references||
       snapshot->element_size!=4||snapshot->length<5)return 0;
#if defined(DND_GAMECUBE_STORAGE) || defined(ANIMALTOWN_MEMORY_TEST)
    struct mallinfo info=mallinfo();
    /* newlib can keep free chunks inside its arena; SYS's remaining arena is
     * memory not yet handed to libc. Their sum counts both exactly once. */
    size_t libc_free=info.fordblks>0?(size_t)info.fordblks:0;
    size_t arena_free=SYS_GetArena1Size();
    size_t total=libc_free>SIZE_MAX-arena_free?SIZE_MAX:libc_free+arena_free;
    int values[5]={(int)town_memory_clamp(total),(int)town_memory_clamp(libc_free),
        (int)town_memory_clamp(arena_free),
        (int)__atomic_load_n(&town_memory_failures,__ATOMIC_ACQUIRE),
        (int)__atomic_load_n(&town_memory_failed_bytes,__ATOMIC_RELAXED)};
    memcpy(snapshot->data,values,sizeof(values));return 1;
#else
    return 0; /* Do not report a host's RAM as console MEM1. */
#endif
}
