/* Game-owned patch to the pinned runtime. Keep the physical block chain for
 * tracing; free blocks link through their unused payload. No per-block overhead
 * is added. Only collection coalesces blocks and rebuilds the free chain. */
#include "native-frame-counters.h"
static DndHeapBlock *free_next(DndHeapBlock *b) { DndHeapBlock *n;memcpy(&n,block_object(b),sizeof(n));return n; }
static void free_link(DndHeapBlock *b,DndHeapBlock *n) { memcpy(block_object(b),&n,sizeof(n)); }
static void rebuild_free_list(DndManagedHeap *heap) {
    heap->free_list=NULL;heap->allocation_tail=NULL;
    DndHeapBlock *previous=NULL;
    for(DndHeapBlock *b=first_block(heap);b;b=b->next){
        if(b->free){
            while(b->next&&b->next->free&&(uint8_t*)b+block_header_size()+b->size==(uint8_t*)b->next){
                b->size+=(uint32_t)(block_header_size()+b->next->size);b->next=b->next->next;
            }
            free_link(b,NULL);if(previous)free_link(previous,b);else heap->free_list=b;previous=b;
        }
        heap->allocation_tail=b;
    }
}
static DndObject *prepare_block(DndManagedHeap *heap,DndHeapBlock *b,const DndType *type,size_t bytes) {
    size_t original=b->size,minimum_tail=block_header_size()+align8(sizeof(DndObject)+8);
    if(original>=bytes+minimum_tail){
        DndHeapBlock *tail=(DndHeapBlock*)((uint8_t*)b+block_header_size()+bytes);
        memset(tail,0,block_header_size());tail->size=(uint32_t)(original-bytes-block_header_size());tail->free=1;
        tail->next=b->next;b->next=tail;b->size=(uint32_t)bytes;
        if(heap->allocation_tail==b)heap->allocation_tail=tail;
        free_link(tail,heap->free_list);heap->free_list=tail;
    }
    b->free=b->marked=0;DndObject *object=block_object(b);memset(object,0,b->size);object->type=type;
    TOWN_COUNT(TOWN_ALLOCATIONS);return object;
}
static DndObject *allocate_from_free(DndManagedHeap *heap,const DndType *type,size_t bytes) {
    // Preserve the few large holes for terrain/save arrays. First-fit reused
    // their prefixes for tiny retained objects even with smaller holes free.
    DndHeapBlock *previous=NULL,*best=NULL,*best_previous=NULL;
    for(DndHeapBlock *b=heap->free_list;b;previous=b,b=free_next(b)){
        TOWN_COUNT(TOWN_FREE_SEARCHES);
        if(b->size<bytes||(best&&b->size>=best->size))continue;
        best=b;best_previous=previous;if(b->size==bytes)break;
    }
    if(!best)return NULL;
    DndHeapBlock *next=free_next(best);if(best_previous)free_link(best_previous,next);else heap->free_list=next;
    return prepare_block(heap,best,type,bytes);
}
static DndObject *allocate(DndManagedHeap *heap,const DndType *type,size_t bytes) {
    if(!heap||!type)return NULL;
    if(bytes>SIZE_MAX-7u||bytes>UINT32_MAX-7u){dnd_exception_throw(DND_EXCEPTION_OUT_OF_MEMORY,"Managed allocation size overflow.");return NULL;}
    bytes=align8(bytes<sizeof(DndObject)?sizeof(DndObject):bytes);
    if(gc_stress&&heap->blocks)dnd_gc_collect(heap,NULL);
    DndObject *reused=allocate_from_free(heap,type,bytes);if(reused)return reused;
    size_t total=block_header_size()+bytes;
    if(total>heap->capacity-heap->used){
        dnd_gc_collect(heap,NULL);reused=allocate_from_free(heap,type,bytes);if(reused)return reused;
        if(total>heap->capacity-heap->used){dnd_exception_throw(DND_EXCEPTION_OUT_OF_MEMORY,"Managed heap exhausted.");return NULL;}
    }
    DndHeapBlock *b=(DndHeapBlock*)(heap->start+heap->used);memset(b,0,block_header_size());b->size=(uint32_t)bytes;
    if(heap->allocation_tail)((DndHeapBlock*)heap->allocation_tail)->next=b;else heap->blocks=b;
    heap->allocation_tail=b;heap->used+=total;return prepare_block(heap,b,type,bytes);
}
void dnd_managed_heap_init(DndManagedHeap *heap,void *memory,size_t size) {
    heap->start=memory;heap->capacity=size;heap->used=0;heap->blocks=heap->free_list=heap->allocation_tail=NULL;heap->collections=0;
}
