#include <assert.h>
#include <limits.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "dnd_managed.h"
#include "dnd_draw.h"
#include <malloc.h>

static int active,fail,libc_free;
static size_t arena_free,observed_count,observed_bytes,observed_alignment;
static unsigned calls;
static unsigned char allocation[64];
static void *observed_previous;

bool dnd_device_active(void){return active!=0;}
size_t SYS_GetArena1Size(void){return arena_free;}
struct mallinfo town_test_mallinfo(void){return (struct mallinfo){libc_free};}
static void *town_test_malloc(size_t bytes){calls++;observed_bytes=bytes;return fail?NULL:allocation;}
static void *town_test_calloc(size_t count,size_t bytes){calls++;observed_count=count;observed_bytes=bytes;return fail?NULL:allocation;}
static void *town_test_realloc(void *previous,size_t bytes){calls++;observed_previous=previous;observed_bytes=bytes;return fail?NULL:allocation;}
void *town_test_memalign(size_t alignment,size_t bytes){calls++;observed_alignment=alignment;observed_bytes=bytes;return fail?NULL:allocation;}

#define ANIMALTOWN_MEMORY_TEST
#define malloc town_test_malloc
#define calloc town_test_calloc
#define realloc town_test_realloc
#include "../tools/native-memory-pressure.c"
#undef malloc
#undef calloc
#undef realloc

int main(void){
    _Alignas(DndArray) unsigned char storage[sizeof(DndArray)+6*sizeof(int)]={0};
    DndArray *snapshot=(DndArray*)storage;
    snapshot->length=6;snapshot->element_size=4;
    int *values=(int*)snapshot->data;values[5]=12345;
    libc_free=128*1024;arena_free=384*1024;
    unsigned before=calls;
    assert(animaltown_memory_read(snapshot)==1);
    assert(calls==before); /* A sample must not probe by allocating. */
    assert(values[0]==512*1024&&values[1]==128*1024&&values[2]==384*1024);
    assert(values[3]==0&&values[4]==0&&values[5]==12345);
    assert(animaltown_memory_malloc(32)==allocation&&observed_bytes==32);
    assert(animaltown_memory_calloc(4,8)==allocation&&observed_count==4&&observed_bytes==8);
    assert(animaltown_memory_realloc(allocation,48)==allocation&&observed_previous==allocation&&observed_bytes==48);
    assert(animaltown_memory_memalign(32,64)==allocation&&observed_alignment==32&&observed_bytes==64);
    assert(animaltown_memory_read(snapshot)==1&&values[3]==0);
    fail=1;
    assert(!animaltown_memory_malloc(65536));
    assert(animaltown_memory_read(snapshot)==1&&values[3]==1&&values[4]==65536);
    assert(!animaltown_memory_calloc(SIZE_MAX,2));
    assert(animaltown_memory_read(snapshot)==1&&values[3]==2&&values[4]==INT_MAX);
    assert(!animaltown_memory_realloc(allocation,131072));
    assert(animaltown_memory_read(snapshot)==1&&values[3]==3&&values[4]==131072);
    assert(observed_previous==allocation); /* realloc preserves original caller ownership on failure. */
    assert(!animaltown_memory_memalign(32,262144));
    assert(animaltown_memory_read(snapshot)==1&&values[3]==4&&values[4]==262144);
    assert(!animaltown_memory_malloc(0)&&!animaltown_memory_realloc(allocation,0)&&!animaltown_memory_calloc(0,32));
    assert(animaltown_memory_read(snapshot)==1&&values[3]==4&&values[4]==262144);
    libc_free=-1;arena_free=SIZE_MAX;
    assert(animaltown_memory_read(snapshot)==1&&values[0]==INT_MAX&&values[1]==0&&values[2]==INT_MAX);
    libc_free=INT_MAX;arena_free=SIZE_MAX;
    assert(animaltown_memory_read(snapshot)==1&&values[0]==INT_MAX); /* saturating sum */
    town_memory_failures=INT_MAX;
    assert(!animaltown_memory_malloc(32));
    assert(animaltown_memory_read(snapshot)==1&&values[3]==INT_MAX);
    int retained=values[0];
    active=1;assert(animaltown_memory_read(snapshot)==0&&values[0]==retained);active=0;
    assert(animaltown_memory_read(NULL)==0);
    snapshot->length=4;assert(animaltown_memory_read(snapshot)==0);snapshot->length=6;
    snapshot->element_size=1;assert(animaltown_memory_read(snapshot)==0);snapshot->element_size=4;
    snapshot->elements_are_references=1;assert(animaltown_memory_read(snapshot)==0);
    puts("MEM1: libc+arena accounting, no sample allocations, real failure evidence, ownership and bounds passed");
    return 0;
}
