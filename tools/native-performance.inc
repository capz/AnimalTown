/* Included at the end of the pinned SDK draw_resources.c, where the resource
 * table is available. Called only by the application executor, between frames.
 * No allocation, collection, GPU synchronization or texture readback. */
#ifdef DND_GAMECUBE_STORAGE
#include <malloc.h>
#endif
int animaltown_performance_memory(int kind) {
    if(kind==2) {
        int bytes=0;
        /* Include retained/pending allocations until actually released. */
        for(int i=0;i<SLOTS;i++)if(slots[i].kind==TEXTURE)bytes+=slots[i].bytes;
        /* This SDK stat is GLOBAL: read once, not once per model/instance.
         * Includes the native allocator's padding; shared textures count once. */
        for(int i=0;i<DND_MODEL_SLOTS;i++)if(dnd_models[i]) {
            int models=dnd_model_stat(dnd_models[i]->token,6);
            if(models>=0){bytes+=models;break;}
        }
        return bytes;
    }
#ifdef DND_GAMECUBE_STORAGE
    if(kind==1)return (int)SYS_GetPhysicalMem1Size();
    if(kind==0) {
        struct mallinfo info=mallinfo();
        uint32_t total=SYS_GetPhysicalMem1Size();
        uint64_t free_bytes=(uint64_t)SYS_GetArena1Size()+info.fordblks;
        return free_bytes<total?(int)(total-free_bytes):0;
    }
#endif
    return -1;
}
