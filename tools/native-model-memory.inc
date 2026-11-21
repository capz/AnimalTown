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
