#include <assert.h>
#include <stdatomic.h>
#include <stdlib.h>
#include "dnd_managed.h"
#include "native-frame-counters.h"
#include "native-model-memory.inc"
extern void animaltown_frame_memory(DndArray*,DndManagedHeap*);
extern void animaltown_frame_dump(DndArray*,int,DndArray*,uint32_t,uint32_t);
extern void animaltown_frame_dump_metrics(DndArray*,DndArray*,DndArray*,int,int,uint32_t);
int dnd_draw_total_bytes(void){return 123456;}
int animaltown_terrain_stat(int kind){return kind==0?32768:kind==6?14:kind==12?2:0;}
static DndArray *array(unsigned count){DndArray *a=calloc(1,sizeof(*a)+count*4);assert(a);a->length=count;a->element_size=4;return a;}
int main(void){
    // Allocate before sampling and free while the profiler is off: ownership
    // remains correct without a session-dependent pointer map or double count.
    town_model_allocated(1024);town_model_allocated(2048);
    atomic_fetch_sub(&town_model_live,1024);
    assert(animaltown_model_memory(0)==2048&&animaltown_model_memory(1)==3072);
    DndManagedHeap heap={0};heap.used=100;heap.capacity=1048576;town_frame_counters.heap_peak=200;
    DndArray *memory=array(32);animaltown_frame_memory(memory,&heap);uint32_t *m=(uint32_t*)memory->data;
    assert(m[0]==1&&m[1]==0&&m[7]==100&&m[8]==200&&m[9]==1048576&&m[11]==123456&&m[13]==2048&&m[14]==3072&&m[26]==12);
    DndArray *words=array(4096),*hist=array(32),*peaks=array(16),*events=array(256);
    uint32_t *w=(uint32_t*)words->data;
    for(unsigned i=0;i<256;i++){w[i*16]=65535+i;w[i*16+1]=33333;}
    ((uint32_t*)hist->data)[15]=300;
    animaltown_frame_dump(words,256,memory,42,300);
    animaltown_frame_dump_metrics(hist,peaks,events,0,0,42);
    free(memory);free(words);free(hist);free(peaks);free(events);
    return 0;
}
