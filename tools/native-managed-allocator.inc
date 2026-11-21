/* Game-owned patch to the pinned runtime. Keep the physical block chain for
 * tracing; free blocks link through their unused payload. No per-block overhead
 * is added. Only collection coalesces blocks and rebuilds the free chain. */
#include "native-frame-counters.h"
#ifdef GEKKO
#include <ogc/system.h>
#endif
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
        if(bytes>=262144u){
            // Keep large transient arrays at the end of their hole. Small
            // retained objects then occupy its prefix, so collecting the large
            // array rejoins the remaining prefix instead of stranding a save
            // buffer behind a tiny live object. Existing objects never move.
            DndHeapBlock *allocated=(DndHeapBlock*)((uint8_t*)b+original-bytes);
            memset(allocated,0,block_header_size());allocated->size=(uint32_t)bytes;allocated->next=b->next;
            b->size=(uint32_t)(original-bytes-block_header_size());b->next=allocated;
            if(heap->allocation_tail==b)heap->allocation_tail=allocated;
            free_link(b,heap->free_list);heap->free_list=b;b=allocated;
        }else{
        DndHeapBlock *tail=(DndHeapBlock*)((uint8_t*)b+block_header_size()+bytes);
        memset(tail,0,block_header_size());tail->size=(uint32_t)(original-bytes-block_header_size());tail->free=1;
        tail->next=b->next;b->next=tail;b->size=(uint32_t)bytes;
        if(heap->allocation_tail==b)heap->allocation_tail=tail;
        free_link(tail,heap->free_list);heap->free_list=tail;
        }
    }
    b->free=b->marked=0;DndObject *object=block_object(b);memset(object,0,b->size);object->type=type;
    if(heap->used>town_frame_counters.heap_peak)town_frame_counters.heap_peak=(uint32_t)heap->used;
    TOWN_COUNT(TOWN_ALLOCATIONS);return object;
}
// Keep one contiguous economic-record buffer inside the existing six-MiB
// game heap. Renderer reloads must not pepper it with small retained objects.
// Other heap sizes retain the ordinary allocator used by SDK fixtures.
static size_t save_reserve_payload(DndManagedHeap *heap) {
    return heap->capacity==6u*1024u*1024u?26u*65536u:0;
}
static uint8_t *save_boundary(DndManagedHeap *heap) {
    size_t reserve=save_reserve_payload(heap);
    return heap->start+heap->capacity-(reserve?block_header_size()+reserve:0);
}
static int save_sized(DndManagedHeap *heap,size_t bytes) {
    size_t reserve=save_reserve_payload(heap);return reserve&&bytes>=1048576u&&bytes<=reserve;
}
static void expose_unused_tail(DndManagedHeap *heap) {
    size_t remaining=heap->capacity-heap->used;
    if(remaining<block_header_size()+align8(sizeof(DndObject)+8))return;
    DndHeapBlock *b=(DndHeapBlock*)(heap->start+heap->used);
    memset(b,0,block_header_size());b->free=1;b->size=(uint32_t)(remaining-block_header_size());
    if(heap->allocation_tail)((DndHeapBlock*)heap->allocation_tail)->next=b;else heap->blocks=b;
    heap->allocation_tail=b;heap->used=heap->capacity;free_link(b,heap->free_list);heap->free_list=b;
}
static DndObject *allocate_from_free(DndManagedHeap *heap,const DndType *type,size_t bytes) {
    size_t reserve=save_reserve_payload(heap),minimum=block_header_size()+align8(sizeof(DndObject)+8);
    uint8_t *boundary=save_boundary(heap);int prefer_reserve=save_sized(heap,bytes);
    // First try the dedicated large-buffer range, then the ordinary range.
    // Small/medium objects never occupy the dedicated range, even at a full
    // heap extent. Collection may merge free blocks across the boundary; split
    // the chosen block back into address ranges without moving live objects.
    for(int area=0;area<(prefer_reserve?2:1);area++){
        int reserved=prefer_reserve&&area==0;
        DndHeapBlock *previous=NULL,*best=NULL,*best_previous=NULL;size_t best_size=SIZE_MAX;
        for(DndHeapBlock *b=heap->free_list;b;previous=b,b=free_next(b)){
            TOWN_COUNT(TOWN_FREE_SEARCHES);uint8_t *begin=(uint8_t*)b,*end=begin+block_header_size()+b->size;
            uint8_t *from=begin,*to=end;
            if(reserve){
                if(reserved){if(to<=boundary)continue;if(from<boundary&&boundary-from>=(ptrdiff_t)minimum)from=boundary;}
                else{if(from>=boundary)continue;if(to>boundary)to=boundary;}
            }
            if(to<=from||to-from<(ptrdiff_t)block_header_size())continue;
            size_t available=(size_t)(to-from)-block_header_size();
            if(available<bytes||available>=best_size)continue;
            best=b;best_previous=previous;best_size=available;if(available==bytes)break;
        }
        if(!best)continue;
        if(!reserve&&bytes<262144u&&best->size>=262144u&&block_header_size()+bytes<=heap->capacity-heap->used)return NULL;
        DndHeapBlock *next=free_next(best);if(best_previous)free_link(best_previous,next);else heap->free_list=next;
        uint8_t *begin=(uint8_t*)best,*end=begin+block_header_size()+best->size;
        if(reserve&&begin<boundary&&end>boundary&&boundary-begin>=(ptrdiff_t)minimum){
            DndHeapBlock *upper=(DndHeapBlock*)boundary;memset(upper,0,block_header_size());
            upper->size=(uint32_t)(end-boundary-block_header_size());upper->free=1;upper->next=best->next;
            best->size=(uint32_t)(boundary-begin-block_header_size());best->next=upper;
            if(heap->allocation_tail==best)heap->allocation_tail=upper;
            DndHeapBlock *unused=reserved?best:upper;
            free_link(unused,heap->free_list);heap->free_list=unused;if(reserved)best=upper;
        }
        return prepare_block(heap,best,type,bytes);
    }
    return NULL;
}
static DndObject *allocate(DndManagedHeap *heap,const DndType *type,size_t bytes) {
    if(!heap||!type)return NULL;
    if(bytes>SIZE_MAX-7u||bytes>UINT32_MAX-7u){dnd_exception_throw(DND_EXCEPTION_OUT_OF_MEMORY,"Managed allocation size overflow.");return NULL;}
    bytes=align8(bytes<sizeof(DndObject)?sizeof(DndObject):bytes);
    size_t requested=bytes;
    // Ordinary heaps use 64-KiB classes for growing terrain/save buffers.
    // The six-MiB heap has a dedicated reusable range instead: keeping exact
    // sizes there avoids wasting up to 64 KiB per concurrent validation buffer
    // in the normal range. Logical array/string lengths are always exact.
    if(!save_reserve_payload(heap)&&bytes>=262144u&&bytes<=UINT32_MAX-65535u)bytes=(bytes+65535u)&~(size_t)65535u;
    if(gc_stress&&heap->blocks)dnd_gc_collect(heap,NULL);
    if(save_sized(heap,bytes))expose_unused_tail(heap);
    DndObject *reused=allocate_from_free(heap,type,bytes);if(reused)return reused;
    size_t total=block_header_size()+bytes;
    size_t limit=(size_t)(save_boundary(heap)-heap->start);
    if(heap->used>limit||total>limit-heap->used){
        dnd_gc_collect(heap,NULL);if(save_sized(heap,bytes)){expose_unused_tail(heap);}reused=allocate_from_free(heap,type,bytes);if(reused)return reused;
        if(heap->used>limit||total>limit-heap->used){
            // Size-class padding must never reject a payload that actually fits.
            bytes=requested;total=block_header_size()+bytes;
            if(save_sized(heap,bytes)){expose_unused_tail(heap);}reused=allocate_from_free(heap,type,bytes);if(reused)return reused;
            if(heap->used>limit||total>limit-heap->used){
#ifdef GEKKO
                size_t largest=0,free_bytes=heap->capacity-heap->used;
                for(DndHeapBlock *hole=heap->free_list;hole;hole=free_next(hole)){free_bytes+=hole->size;if(hole->size>largest)largest=hole->size;}
                SYS_Report("ANIMALTOWN MANAGED OOM request %u extent %u capacity %u free %u largest %u collections %u\n",(unsigned)bytes,(unsigned)heap->used,(unsigned)heap->capacity,(unsigned)free_bytes,(unsigned)largest,(unsigned)heap->collections);
#endif
                dnd_exception_throw(DND_EXCEPTION_OUT_OF_MEMORY,"Managed heap exhausted.");return NULL;
            }
        }
    }
    DndHeapBlock *b=(DndHeapBlock*)(heap->start+heap->used);memset(b,0,block_header_size());b->size=(uint32_t)bytes;
    if(heap->allocation_tail)((DndHeapBlock*)heap->allocation_tail)->next=b;else heap->blocks=b;
    heap->allocation_tail=b;heap->used+=total;return prepare_block(heap,b,type,bytes);
}
void dnd_managed_heap_init(DndManagedHeap *heap,void *memory,size_t size) {
    heap->start=memory;heap->capacity=size;heap->used=0;heap->blocks=heap->free_list=heap->allocation_tail=NULL;heap->collections=0;
}
