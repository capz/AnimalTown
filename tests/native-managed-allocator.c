#include "dnd_managed.h"
#include "native-frame-counters.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
TownFrameCounters town_frame_counters;
typedef struct {DndObject base;DndObject *next;int value;} Node;
static const uint32_t refs[]={(uint32_t)offsetof(Node,next)};
static const DndType type={"Node",&DND_TYPE_OBJECT,sizeof(Node),0,NULL,1,refs,0,0,NULL,0,NULL};
int main(void){
    const int size=4*1024*1024;void *memory=malloc(size);DndManagedHeap heap;dnd_managed_heap_init(&heap,memory,size);
    DndObject *root=NULL;DndObject **slots[]={&root};DndRootSet roots;dnd_roots_init(&roots,slots,1);assert(dnd_root_add(&roots,&root));
    for(int i=0;i<16000;i++){Node *n=(Node*)dnd_object_new(&heap,&type);assert(n);n->value=i;n->next=root;root=(DndObject*)n;}
    town_frame_counters.enabled=1;
    for(int i=0;i<100;i++)assert(dnd_object_new(&heap,&DND_TYPE_OBJECT));
    assert(town_frame_counters.count[TOWN_ALLOCATIONS]==100&&town_frame_counters.count[TOWN_FREE_SEARCHES]==0);
    dnd_gc_collect(&heap,&roots);Node *n=(Node*)root;for(int i=15999;i>=0;i--){assert(n&&n->value==i);n=(Node*)n->next;}assert(!n);
    root=NULL;dnd_gc_collect(&heap,&roots);assert(heap.used==0&&!heap.blocks&&!heap.free_list&&!heap.allocation_tail);
    // Keep isolated live sentinels around holes, exercising split/free-chain
    // removal, coalescing and append after reclaiming the physical tail.
    DndObject *live[8]={0};DndObject **liveSlots[8];for(int i=0;i<8;i++)liveSlots[i]=&live[i];DndRootSet liveRoots;dnd_roots_init(&liveRoots,liveSlots,8);for(int i=0;i<8;i++)assert(dnd_root_add(&liveRoots,&live[i]));
    for(int pass=0;pass<200;pass++){
        for(int i=0;i<8;i++){live[i]=dnd_object_new(&heap,&type);assert(live[i]);((Node*)live[i])->value=i;assert(dnd_managed_array_new(&heap,64+i*7,1));}
        dnd_gc_collect(&heap,&liveRoots);
        for(int i=0;i<8;i++){assert(((Node*)live[i])->value==i);for(int j=0;j<i;j++)assert(live[i]!=live[j]);}
        for(int i=0;i<30;i++)assert(dnd_object_new(&heap,&DND_TYPE_OBJECT));
        for(int i=0;i<8;i++)live[i]=NULL;
        dnd_gc_collect(&heap,&liveRoots);assert(heap.used==0);
    }
    // Automatic collections retain registered roots and exception machinery.
    DndGcRootRegistration registration={0};assert(dnd_gc_register_roots(&registration,&roots));
    root=dnd_object_new(&heap,&type);((Node*)root)->value=73;dnd_gc_set_stress(true);
    for(int i=0;i<100;i++){assert(dnd_object_new(&heap,&DND_TYPE_OBJECT));assert(((Node*)root)->value==73);}
    dnd_gc_set_stress(false);dnd_gc_unregister_roots(&registration);root=NULL;dnd_gc_collect(&heap,NULL);assert(!heap.used);
    // A small retained allocation must use the smaller hole. Consuming the
    // large hole first strands enough total free memory for the next save-sized
    // array, but leaves no contiguous block that can hold it.
    DndGcRootRegistration fragments={0};assert(dnd_gc_register_roots(&fragments,&liveRoots));
    assert(dnd_managed_array_new(&heap,32000,1));live[0]=dnd_object_new(&heap,&type);
    assert(dnd_managed_array_new(&heap,4000,1));live[1]=dnd_object_new(&heap,&type);
    live[2]=(DndObject*)dnd_managed_array_new(&heap,(int)(heap.capacity-heap.used-256),1);assert(live[2]);
    dnd_gc_collect(&heap,&liveRoots);
    live[3]=(DndObject*)dnd_managed_array_new(&heap,3000,1);assert(live[3]);
    live[4]=(DndObject*)dnd_managed_array_new(&heap,32000,1);assert(live[4]);
    for(int i=0;i<8;i++)live[i]=NULL;
    dnd_gc_unregister_roots(&fragments);dnd_gc_collect(&heap,NULL);assert(!heap.used);
    // Preserve an existing large size class while smaller retained objects can
    // still append into unused tail space.
    assert(dnd_gc_register_roots(&fragments,&liveRoots));
    DndArray *preserved=dnd_managed_array_new(&heap,1600000,1);assert(preserved);
    live[0]=dnd_object_new(&heap,&type);assert(live[0]);
    live[1]=(DndObject*)dnd_managed_array_new(&heap,2100000,1);assert(live[1]);
    dnd_gc_collect(&heap,&liveRoots);size_t used_before_small=heap.used;
    live[2]=(DndObject*)dnd_managed_array_new(&heap,4096,1);assert(live[2]&&(uint8_t*)live[2]>=heap.start+used_before_small);
    live[3]=(DndObject*)dnd_managed_array_new(&heap,1600300,1);assert(live[3]==(DndObject*)preserved);
    for(int i=0;i<8;i++)live[i]=NULL;
    dnd_gc_unregister_roots(&fragments);dnd_gc_collect(&heap,NULL);assert(!heap.used);
    // A transient array followed by a small retained allocation must leave a
    // reusable large region when collected. Carving the array from the front
    // strands two individually insufficient holes on either side of the node.
    assert(dnd_gc_register_roots(&fragments,&liveRoots));
    DndArray *region=dnd_managed_array_new(&heap,700000,1);assert(region);
    live[0]=dnd_object_new(&heap,&type);assert(live[0]);
    live[1]=(DndObject*)dnd_managed_array_new(&heap,(int)(heap.capacity-heap.used-256),1);assert(live[1]);
    dnd_gc_collect(&heap,&liveRoots);
    live[2]=(DndObject*)dnd_managed_array_new(&heap,300000,1);assert(live[2]&&(uintptr_t)live[2]>(uintptr_t)region);
    live[3]=dnd_object_new(&heap,&type);assert(live[3]);((Node*)live[3])->value=91;
    live[2]=NULL;dnd_gc_collect(&heap,&liveRoots);
    live[4]=(DndObject*)dnd_managed_array_new(&heap,500000,1);assert(live[4]&&((Node*)live[3])->value==91);
    for(int i=0;i<8;i++)live[i]=NULL;
    dnd_gc_unregister_roots(&fragments);dnd_gc_collect(&heap,NULL);assert(!heap.used);
    // A growing save can reuse the previous buffer with live data on both
    // sides, rather than needing another megabyte-sized hole for a few bytes.
    assert(dnd_gc_register_roots(&fragments,&liveRoots));
    DndArray *old_save=dnd_managed_array_new(&heap,1600000,1);assert(old_save&&old_save->length==1600000);
    live[0]=dnd_object_new(&heap,&type);assert(live[0]);
    live[1]=(DndObject*)dnd_managed_array_new(&heap,(int)(heap.capacity-heap.used-256),1);assert(live[1]);
    dnd_gc_collect(&heap,&liveRoots);
    DndArray *grown=dnd_managed_array_new(&heap,1600300,1);assert(grown&&grown==old_save&&grown->length==1600300);
    for(int i=0;i<8;i++)live[i]=NULL;
    dnd_gc_unregister_roots(&fragments);dnd_gc_collect(&heap,NULL);assert(!heap.used);
    free(memory);puts("Managed allocator: 16000 live objects, zero live-block searches, rooted graph, holes/splits/coalescing, tail reclamation, 200 reuse cycles, GC stress and contiguous save-sized buffers passed");
}
