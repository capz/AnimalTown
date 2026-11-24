/* Included after the pinned SDK resource table. Report only rejected mesh
 * creation; preserve return values, allocation policy and successful paths. */
#include <stdio.h>
#ifdef DND_GAMECUBE_STORAGE
#include <malloc.h>
#endif
/* Graphics creation belongs to the application executor. This consumed flag
 * distinguishes one actual mesh malloc rejection from validation/budget errors.
 * The generic mesh-resource diagnostic must not overwrite it. */
static int town_mesh_malloc_failed;
int animaltown_take_mesh_allocation_failure(void){
    int failed=town_mesh_malloc_failed;town_mesh_malloc_failed=0;return failed;
}
static int town_mesh_failure(const char *reason,int vertices,int indices,int format,int bytes){
    int live=0;
    for(int i=0;i<SLOTS;i++)if(slots[i].kind)live++;
#ifdef DND_GAMECUBE_STORAGE
    struct mallinfo memory=mallinfo();
    SYS_Report("ANIMALTOWN MESH FAILED reason %s vertices %d indices %d format %d bytes %d graphics_bytes %d graphics_budget %d slots %d slot_limit %d active %d raw %d arena_free %u malloc_free %u\n",
        reason,vertices,indices,format,bytes,total_bytes,BUDGET,live,SLOTS,
        dnd_device_active(),dnd_raw_depth(),(unsigned)SYS_GetArena1Size(),(unsigned)memory.fordblks);
#else
    fprintf(stderr,"ANIMALTOWN MESH FAILED reason %s vertices %d indices %d format %d bytes %d graphics_bytes %d graphics_budget %d slots %d slot_limit %d active %d raw %d\n",
        reason,vertices,indices,format,bytes,total_bytes,BUDGET,live,SLOTS,
        dnd_device_active(),dnd_raw_depth());
#endif
    return 0;
}
static int town_mesh_resource_failure(int kind,const char *reason,int bytes){
    return kind==MESH?town_mesh_failure(reason,-1,-1,-1,bytes):0;
}
