/* Fault each cold-reset boundary in the production archive. The full SDK
 * archive fixture covers shared bake scratch, DMA and live geometry. */
#include <assert.h>
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "dnd_draw.h"
#include "dnd_aram.h"

#define SLOTS 4
#define BUDGET (16*1024*1024)
enum {BUFFER=1,MESH=3};
typedef struct {
    void *allocation;
    unsigned char *data,*commands;
    int kind,bytes,generation,holds,geometry,terrain_parent,terrain_entry;
    int vertices,indices,format,dynamic;
    bool pending,uniform_color;
    uint32_t mesh_color;
} Resource;
static Resource slots[SLOTS];
static int total_bytes,raw;
static bool active;
static Resource *find(int token,int kind);
static int create(int kind,int bytes,int alignment);
static void release(Resource *resource);
static int capture_report(FILE *stream,const char *format,...);
#define fprintf capture_report
#include "../tools/native-terrain-archive.inc"
#undef fprintf

enum {FAIL_ARAM=1,FAIL_RETAIN=2,FAIL_CACHE=4,FAIL_STAGE=8,FAIL_HOLD=16};
static int faults,aram_bytes,aram_holds,allocations,retains,holds,creates;
static int created_bytes[4],report_count,reported_graphics[4],reported_aram[4];
static char reports[4][1024];
static const int aram_capacity=14*1024*1024-0x4000;
bool dnd_device_active(void){return active;}
int dnd_raw_depth(void){return raw;}
static Resource *find(int token,int kind){
    int index=(token&1023)-1;
    if(token<=0||index<0||index>=SLOTS)return NULL;
    Resource *r=&slots[index];
    return r->kind&&r->generation==(token>>10)&&(!kind||kind==r->kind)?r:NULL;
}
static int create(int kind,int bytes,int alignment){
    assert(kind==BUFFER&&alignment==32&&creates<4);created_bytes[creates++]=bytes;
    if((bytes==TA_CACHE&&(faults&FAIL_CACHE))||(bytes==TA_STAGE&&(faults&FAIL_STAGE)))return 0;
    for(int i=0;i<SLOTS;i++)if(!slots[i].kind){
        Resource *r=&slots[i];r->allocation=calloc(1,(size_t)bytes);assert(r->allocation);
        r->data=r->allocation;r->kind=kind;r->bytes=bytes;r->generation++;
        total_bytes+=bytes;return (r->generation<<10)|(i+1);
    }
    assert(false);return 0;
}
static void release(Resource *r){
    assert(r&&r->kind);total_bytes-=r->bytes;free(r->allocation);
    int generation=r->generation;memset(r,0,sizeof(*r));r->generation=generation;
}
int dnd_draw_release(int token){Resource *r=find(token,0);if(!r)return -1;release(r);return 0;}
int dnd_buffer_hold(int token){
    holds++;Resource *r=find(token,BUFFER);assert(r);
    if(faults&FAIL_HOLD)return -1;
    r->holds++;return 0;
}
int dnd_buffer_unhold(int token){Resource *r=find(token,BUFFER);if(!r||!r->holds)return -1;r->holds--;return 0;}
int dnd_aram_allocate(int bytes){
    allocations++;assert(!aram_bytes&&bytes==TA_BYTES);
    if(faults&FAIL_ARAM)return 0;
    aram_bytes=bytes;return 17;
}
int dnd_aram_retain(int token){
    retains++;assert(token==17&&aram_bytes);
    if(faults&FAIL_RETAIN)return -EINVAL;
    aram_holds++;return 0;
}
int dnd_aram_release(int token){assert(token==17&&aram_holds);aram_holds--;return 0;}
int dnd_aram_dispose(int token){assert(token==17&&aram_bytes&&!aram_holds);aram_bytes=0;return 0;}
int dnd_aram_stat(int kind){
    return kind==0?aram_capacity:kind==1?aram_bytes:kind==2||kind==3?aram_capacity-aram_bytes:kind==4?(aram_bytes?1:0):-EINVAL;
}
static int capture_report(FILE *stream,const char *format,...){
    assert(stream==stderr&&report_count<4);
    va_list args;va_start(args,format);
    int size=vsnprintf(reports[report_count],sizeof(reports[0]),format,args);va_end(args);
    assert(size>0&&(size_t)size<sizeof(reports[0]));
    reported_graphics[report_count]=total_bytes;reported_aram[report_count]=aram_bytes;
    report_count++;return size;
}
static void start(int failures){
    assert(animaltown_terrain_reset(0)==1&&!total_bytes&&!aram_bytes&&!aram_holds);
    faults=failures;allocations=retains=holds=creates=report_count=0;
    memset(reports,0,sizeof(reports));
}
static void report(int index,const char *reason,int bytes,int result,int graphics,int aram){
    assert(index<report_count&&reported_graphics[index]==graphics&&reported_aram[index]==aram);
    char expected[160];
    snprintf(expected,sizeof(expected),"ANIMALTOWN TERRAIN RESET FAILED reason %s bytes %d result %d sections 3 graphics_bytes %d",reason,bytes,result,graphics);
    assert(strstr(reports[index],expected)==reports[index]);
    snprintf(expected,sizeof(expected),"aram_free %d aram_largest %d aram_allocations %d",aram_capacity-aram,aram_capacity-aram,aram?1:0);
    assert(strstr(reports[index],expected));
}
static void rolled_back(void){
    assert(!total_bytes&&!aram_bytes&&!aram_holds&&!ta_store&&!ta_cache&&!ta_stage);
    for(int i=0;i<SLOTS;i++)assert(!slots[i].kind);
}
int main(void){
    start(0);
    active=true;assert(animaltown_terrain_reset(3)==-1);active=false;
    raw=1;assert(animaltown_terrain_reset(3)==-1);raw=0;
    assert(animaltown_terrain_reset(-1)==-1&&!report_count&&!allocations&&!creates);
    assert(animaltown_terrain_reset(3)==1&&!report_count&&allocations==1&&retains==1&&holds==1&&creates==1);
    assert(total_bytes==TA_CACHE&&aram_bytes==TA_BYTES&&ta_stage==ta_cache&&find(ta_cache,BUFFER)->holds==1);
    assert(animaltown_terrain_reset(0)==1&&!report_count);rolled_back();

    start(FAIL_ARAM);assert(animaltown_terrain_reset(3)==-2&&report_count==1&&!retains&&!creates);
    report(0,"aram-allocate",TA_BYTES,0,0,0);rolled_back();
    start(FAIL_RETAIN);assert(animaltown_terrain_reset(3)==-2&&report_count==1&&retains==1&&!creates);
    report(0,"aram-retain",TA_BYTES,-EINVAL,0,TA_BYTES);rolled_back();
    start(FAIL_CACHE);assert(animaltown_terrain_reset(3)==-2&&report_count==1&&creates==1&&!holds);
    assert(created_bytes[0]==TA_CACHE);
    report(0,"cache-buffer",TA_CACHE,0,0,TA_BYTES);rolled_back();
    /* A heap that rejects the former separate staging allocation still admits
     * the whole reset: only the cache is allocated and its bake lease is held. */
    start(FAIL_STAGE);assert(animaltown_terrain_reset(3)==1&&!report_count&&creates==1&&holds==1);
    assert(created_bytes[0]==TA_CACHE&&ta_stage==ta_cache&&total_bytes==TA_CACHE);
    assert(animaltown_terrain_reset(0)==1);rolled_back();
    start(FAIL_HOLD);assert(animaltown_terrain_reset(3)==-2&&report_count==1&&creates==1&&holds==1);
    report(0,"stage-hold",TA_STAGE,-1,TA_CACHE,TA_BYTES);rolled_back();
    start(FAIL_CACHE|FAIL_STAGE);assert(animaltown_terrain_reset(3)==-2&&report_count==1&&creates==1&&!holds);
    report(0,"cache-buffer",TA_CACHE,0,0,TA_BYTES);rolled_back();
    start(0);assert(animaltown_terrain_reset(3)==1&&animaltown_terrain_reset(0)==1&&!report_count);rolled_back();
    puts("Terrain reset: shared staging lease, four failure boundaries, pre-cleanup counters, rollback and silent success/guards passed");
    return 0;
}
