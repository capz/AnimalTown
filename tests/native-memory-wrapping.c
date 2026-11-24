#include <assert.h>
#include <stdio.h>
#define DND_GAMECUBE_STORAGE
#include "../tools/native-memory-pressure.h"
/* SDK native units may include malloc.h after the bridge. Its declarations
 * must remain compatible with the aliased allocation functions. */
#include <malloc.h>

static int selected;
static size_t argument,alignment_argument;
static unsigned char owned[64];
void *animaltown_memory_malloc(size_t bytes){selected=1;argument=bytes;return owned;}
void *animaltown_memory_calloc(size_t count,size_t bytes){selected=2;argument=count*bytes;return owned;}
void *animaltown_memory_realloc(void *previous,size_t bytes){assert(previous==owned);selected=3;argument=bytes;return owned;}
void *animaltown_memory_memalign(size_t alignment,size_t bytes){selected=4;alignment_argument=alignment;argument=bytes;return owned;}

int main(void){
    assert(malloc(23)==owned&&selected==1&&argument==23);
    assert(calloc(3,7)==owned&&selected==2&&argument==21);
    assert(realloc(owned,31)==owned&&selected==3&&argument==31);
    assert(memalign(32,64)==owned&&selected==4&&alignment_argument==32&&argument==64);
    puts("MEM1 wrapping: SDK allocation aliases preserve signatures and caller ownership");
    return 0;
}
