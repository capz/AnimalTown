#include "dnd_managed.h"
#include <stdio.h>
#include <stdlib.h>
void dnd_console_write_line(const char *text) { puts(text); }
void dnd_console_write_managed_line(DndString *text) { (void)text; }
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
int main(void) {
    static unsigned char memory[8*1024*1024];DndManagedHeap heap;
    dnd_managed_heap_init(&heap,memory,sizeof(memory));
    dnd_gc_set_stress(getenv("ANIMALTOWN_JOB_GC_STRESS")!=NULL);
    intptr_t result=dnd_aot_entry(&heap);
    if(result||dnd_exception_pending()) {
        fprintf(stderr,"Offline job AOT result=%ld exception=%s\n",(long)result,dnd_exception_message());return 1;
    }
    puts("Production job AOT: RNG, 64-bit money, identity, offers, bindings, save/load, completion and replay passed");return 0;
}
