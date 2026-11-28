#include "dnd_managed.h"
#include <stdio.h>
#include <stdlib.h>
void dnd_console_write_line(const char *text) { puts(text); }
void dnd_console_write_managed_line(DndString *text) {
    if(text){for(uint32_t i=0;i<text->length;i++)putchar(text->chars[i]<128?(char)text->chars[i]:'?');putchar('\n');}
}
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
int main(void) {
    static unsigned char memory[4*1024*1024]; DndManagedHeap heap;
    dnd_managed_heap_init(&heap,memory,sizeof(memory));
    dnd_gc_set_stress(getenv("ANIMALTOWN_ONLINE_GC_STRESS")!=NULL);
    intptr_t result=dnd_aot_entry(&heap);
    if(result||dnd_exception_pending()){
        fprintf(stderr,"Online AOT result=%ld heap=%zu/%zu exception=%s\n",(long)result,heap.used,heap.capacity,dnd_exception_message());return 1;
    }
    return 0;
}
