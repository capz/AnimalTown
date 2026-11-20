#include "dnd_managed.h"
#include "native-frame-counters.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
TownFrameCounters town_frame_counters;
size_t job_heap_peak;
int job_report(int stage,DndManagedHeap *heap){
    static clock_t started;clock_t now=clock();dnd_gc_collect(heap,NULL);
    printf("JOB RESOURCE stage=%d heap_peak=%zu heap_after_gc=%zu capacity=%zu host_cpu_ms=%.3f\n",stage,job_heap_peak,heap->used,heap->capacity,started?1000.0*(now-started)/CLOCKS_PER_SEC:0.0);
    started=clock();job_heap_peak=heap->used;return 0;
}
void dnd_console_write_line(const char *text) { puts(text); }
void dnd_console_write_managed_line(DndString *text) { if(text){for(uint32_t i=0;i<text->length;i++)putchar(text->chars[i]<128?(char)text->chars[i]:'?');putchar('\n');} }
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
int main(void) {
    setvbuf(stdout,NULL,_IONBF,0);
    static unsigned char memory[8*1024*1024];DndManagedHeap heap;
    int mib=getenv("ANIMALTOWN_JOB_HEAP_MIB")?atoi(getenv("ANIMALTOWN_JOB_HEAP_MIB")):4;
    if(mib<1||mib>8)return 2;
    dnd_managed_heap_init(&heap,memory,(size_t)mib*1024*1024);
    dnd_gc_set_stress(getenv("ANIMALTOWN_JOB_GC_STRESS")!=NULL);
    intptr_t result=dnd_aot_entry(&heap);
    if(result||dnd_exception_pending()) {
        fprintf(stderr,"Offline job AOT result=%ld heap=%zu/%zu collections=%zu exception=%s\n",(long)result,heap.used,heap.capacity,heap.collections,dnd_exception_message());return 1;
    }
    puts("Production job AOT: RNG, 64-bit money, identity, offers, bindings, save/load, completion and replay passed");return 0;
}

static unsigned char *cards[2];static int card_blocks[2];
int job_blocks_io(int card,int op,int block,DndArray *bytes){
    if(card<0||card>=2)return -1;
    if(op==0){if(block<32||block>2048)return -1;free(cards[card]);cards[card]=calloc((size_t)block,8192);card_blocks[card]=block;return cards[card]?0:-1;}
    if(!cards[card]||block<0||block>=card_blocks[card]||!bytes||bytes->length!=8192)return -1;
    if(op==1)memcpy(bytes->data,cards[card]+block*8192,8192);else if(op==2)memcpy(cards[card]+block*8192,bytes->data,8192);else return -1;return 0;
}
