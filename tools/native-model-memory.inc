/* Count real model allocator requests, including their existing 32-byte
 * headers/alignment, once per allocation. Shared texture charges in m->bytes
 * are not allocations and are deliberately excluded. No pointer registry. */
static atomic_uint town_model_live,town_model_peak;
static void town_model_allocated(unsigned bytes){
    unsigned live=atomic_fetch_add_explicit(&town_model_live,bytes,memory_order_relaxed)+bytes;
    unsigned peak=atomic_load_explicit(&town_model_peak,memory_order_relaxed);
    while(live>peak&&!atomic_compare_exchange_weak_explicit(&town_model_peak,&peak,live,memory_order_relaxed,memory_order_relaxed)){}
}
unsigned animaltown_model_memory(int peak){return atomic_load_explicit(peak?&town_model_peak:&town_model_live,memory_order_relaxed);}

static inline void town_model_allocation_failed(const char *reason,unsigned size,unsigned used,unsigned budget){
#ifdef DND_GAMECUBE_STORAGE
    struct mallinfo memory=mallinfo();
    SYS_Report("ANIMALTOWN MODEL ALLOCATION FAILED reason %s requested %u used %u budget %u model_live %u arena_free %u malloc_free %u\n",
        reason,size,used,budget,animaltown_model_memory(0),(unsigned)SYS_GetArena1Size(),(unsigned)memory.fordblks);
#else
    (void)reason;(void)size;(void)used;(void)budget;
#endif
}
