#include "dnd_managed.h"
#include "dnd_launch.h"
#include "dnd_storage.h"
#include <stdio.h>
#include <stdlib.h>
void dnd_console_write_line(const char *text) { (void)text; }
void dnd_console_write_managed_line(DndString *text) { (void)text; }
uint16_t dnd_input_buttons_down(unsigned port) { (void)port; return 0; }
extern intptr_t dnd_aot_entry(DndManagedHeap *heap);
int main(int argc,char **argv) {
    dnd_launch_capture(argc-1,(const char *const *)(argv+1));
    static unsigned char memory[2*1024*1024];DndManagedHeap heap;
    dnd_managed_heap_init(&heap,memory,sizeof(memory));
    dnd_gc_set_stress(getenv("ANIMALTOWN_CONTENT_GC_STRESS")!=NULL);
    intptr_t result=dnd_aot_entry(&heap);
    int handles=dnd_fs_open_handles();
    if(result||dnd_exception_pending()||handles) {
        fprintf(stderr,"Content startup AOT result=%ld handles=%d exception=%s\n",(long)result,handles,dnd_exception_message());return 1;
    }
    puts("Production app bootstrap: SDK argv, real host-volume sync/async reads, closed handles and ISO-updater rejection passed");return 0;
}
