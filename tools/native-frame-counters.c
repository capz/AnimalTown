#include "dnd_managed.h"
#include "dnd_async.h"
#include "native-frame-counters.h"
#include "dnd_draw.h"
#include "dnd_aram.h"
#include <string.h>
#include <stdio.h>
#ifdef GEKKO
#include <ogc/system.h>
#include <malloc.h>
#include <ogc/lwp_watchdog.h>
#endif
TownFrameCounters town_frame_counters;
static size_t last_collections;
void animaltown_frame_reset(int enabled,DndManagedHeap *heap){
#ifdef GEKKO
    if(enabled&&!town_frame_counters.enabled)SYS_Report("ANIMALTOWN MEMORY managed_used %u managed_peak %u managed_capacity %u collections %u\n",(unsigned)heap->used,town_frame_counters.heap_peak,(unsigned)heap->capacity,(unsigned)heap->collections);
    if(enabled&&!town_frame_counters.enabled){
        struct mallinfo memory=mallinfo();uint32_t aram_used=0,aram_total=0;
        unsigned arena=SYS_GetArena1Size(),free_bytes=arena+(unsigned)memory.fordblks;
        dnd_aram_memory_usage(&aram_used,&aram_total);
        SYS_Report("ANIMALTOWN SYSTEM MEMORY mem1_used %u mem1_total %u arena_free %u malloc_free %u aram_used %u aram_total %u\n",SYS_GetPhysicalMem1Size()-free_bytes,SYS_GetPhysicalMem1Size(),arena,(unsigned)memory.fordblks,aram_used,aram_total);
    }
#endif
    town_frame_counters.enabled=(unsigned)enabled;memset(town_frame_counters.count,0,sizeof(town_frame_counters.count));last_collections=heap->collections;
}
void animaltown_frame_snapshot(DndArray *data,DndManagedHeap *heap){
    if(!data||data->elements_are_references||data->element_size!=4||data->length<8)return;
    int32_t *out=(int32_t*)data->data;
    for(int i=0;i<TOWN_COUNTERS;i++)out[i]=(int32_t)town_frame_counters.count[i];
    out[7]=(int32_t)(heap->collections-last_collections);
}
void animaltown_frame_service(void){dnd_async_service();}
extern unsigned animaltown_model_memory(int peak);
extern int animaltown_terrain_stat(int kind);
static uint32_t physical_peak,graphics_peak;
uint32_t animaltown_profile_clock(void){
#ifdef GEKKO
    return (uint32_t)ticks_to_microsecs(gettime());
#else
    return 0;
#endif
}
void animaltown_frame_memory(DndArray *data,DndManagedHeap *heap){
    if(!data||data->elements_are_references||data->element_size!=4||data->length<32)return;
    uint32_t at=animaltown_profile_clock(),*out=(uint32_t*)data->data;
    memset(out,0,32*sizeof(*out));out[0]=1;
#ifdef GEKKO
    struct mallinfo memory=mallinfo();uint32_t aram_used=0,aram_total=0;
    out[1]=1;out[2]=SYS_GetPhysicalMem1Size();out[3]=SYS_GetArena1Size();out[4]=(uint32_t)memory.fordblks;
    uint64_t free_bytes=(uint64_t)out[3]+out[4];out[5]=free_bytes<out[2]?out[2]-(uint32_t)free_bytes:0;
    if(out[5]>physical_peak)physical_peak=out[5];
    if(dnd_aram_memory_usage(&aram_used,&aram_total)){out[1]|=2;out[15]=aram_total;out[16]=aram_used;out[17]=(uint32_t)dnd_aram_stat(3);out[18]=(uint32_t)dnd_aram_stat(4);}
#endif
    out[6]=physical_peak;out[7]=(uint32_t)heap->used;out[8]=town_frame_counters.heap_peak;out[9]=(uint32_t)heap->capacity;out[10]=(uint32_t)heap->collections;
    out[11]=(uint32_t)dnd_draw_total_bytes();if(out[11]>graphics_peak)graphics_peak=out[11];out[12]=graphics_peak;
    out[13]=animaltown_model_memory(0);out[14]=animaltown_model_memory(1);
    out[19]=(uint32_t)animaltown_terrain_stat(0);out[20]=(uint32_t)animaltown_terrain_stat(10);out[21]=(uint32_t)animaltown_terrain_stat(7);out[22]=(uint32_t)animaltown_terrain_stat(11);
    out[23]=(uint32_t)animaltown_terrain_stat(5);out[24]=(uint32_t)animaltown_terrain_stat(12);out[25]=(uint32_t)animaltown_terrain_stat(13);out[26]=(uint32_t)animaltown_terrain_stat(6)-out[24];out[27]=(uint32_t)animaltown_terrain_stat(14);
    out[28]=animaltown_profile_clock()-at;out[29]=at;
    // Diagnostic residual, not a claim that all allocator ownership is known.
    // The managed arena is counted once; terrain is a subset of draw bytes.
    uint64_t known=(uint64_t)out[9]+out[11]+out[13];out[30]=out[5]>known?out[5]-(uint32_t)known:0;
    out[31]=1; // model requests are tracked from boot; physical/draw peaks sampled
}
static unsigned profile_crc(unsigned crc,uint32_t word){
    for(int octet=0;octet<4;octet++){crc^=((word>>(octet*8))&255u)<<8;for(int bit=0;bit<8;bit++)crc=crc&32768u?((crc<<1)^0x1021u)&65535u:(crc<<1)&65535u;}return crc;
}
static void profile_line(const char *kind,uint32_t session,const uint32_t *words,int count){
    // One complete bounded log call: Dolphin may prefix each OSReport call,
    // whereas USB Gecko is a byte stream. Never split a record across calls.
    char line[384];int at=snprintf(line,sizeof(line),"ANIMALTOWN PROFILE %s %08x",kind,session);unsigned crc=65535;
    if(count<0||count>32||at<0)return;
    for(int i=0;i<count;i++){crc=profile_crc(crc,words[i]);at+=snprintf(line+at,sizeof(line)-(size_t)at," %08x",words[i]);}
    (void)snprintf(line+at,sizeof(line)-(size_t)at," %04x\n",crc);
#ifdef GEKKO
    SYS_Report("%s",line);
#else
    printf("%s",line);
#endif
}
void animaltown_frame_dump(DndArray *words,int count,DndArray *memory,uint32_t session,uint32_t total){
    if(!words||words->elements_are_references||words->element_size!=4||words->length<4096||count<0||count>256||!memory||memory->elements_are_references||memory->element_size!=4||memory->length<32)return;
#ifdef GEKKO
#define PROFILE_PRINT SYS_Report
#else
#define PROFILE_PRINT printf
#endif
    PROFILE_PRINT("ANIMALTOWN PROFILE BEGIN 1 %08x %u %u\n",session,total,total>(uint32_t)count?total-(uint32_t)count:0);
    uint32_t *data=(uint32_t*)words->data,*snapshot=(uint32_t*)memory->data;
    for(int i=0;i<count;i++){
        unsigned offset=((total-(uint32_t)count+(uint32_t)i)%256u)*16u;
        profile_line("FRAME",session,data+offset,16);
    }
    profile_line("MEMORY",session,snapshot,32);
    PROFILE_PRINT("ANIMALTOWN PROFILE END %08x\n",session);
#undef PROFILE_PRINT
}
void animaltown_frame_dump_metrics(DndArray *histogram,DndArray *peaks,DndArray *events,int count,int dropped,uint32_t session){
    if(!histogram||histogram->elements_are_references||histogram->element_size!=4||histogram->length<32||!peaks||peaks->elements_are_references||peaks->element_size!=4||peaks->length<16||!events||events->elements_are_references||events->element_size!=4||events->length<256||count<0||count>64)return;
#ifdef GEKKO
#define PROFILE_PRINT SYS_Report
#else
#define PROFILE_PRINT printf
#endif
    profile_line("HISTOGRAM",session,(uint32_t*)histogram->data,32);
    profile_line("PEAKS",session,(uint32_t*)peaks->data,16);
    for(int i=0;i<count;i++)profile_line("EVENT",session,(uint32_t*)events->data+i*4,4);
    PROFILE_PRINT("ANIMALTOWN PROFILE DROPPED_EVENTS %08x %d\n",session,dropped);
#undef PROFILE_PRINT
}
