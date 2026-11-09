#include "dnd_model_internal.h"
#include "dnd_storage.h"
#include "dnd_aram.h"
#include "dnd_draw.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static bool active;
static unsigned invalidations,publications;
void dnd_draw_backend_invalidate_cache(void){invalidations++;}
void animaltown_shadows_model_reset(void){}
void dnd_draw_backend_publish_texture(void){publications++;}
static void *buffers[8];static unsigned sizes[8];static int holds[8],next_buffer=1;
bool dnd_device_active(void){return active;}
int dnd_buffer_create(int size,int alignment){assert(alignment==32&&next_buffer<8);int n=next_buffer++;buffers[n]=aligned_alloc(32,(size_t)size);assert(buffers[n]);sizes[n]=(unsigned)size;return n;}
int dnd_buffer_hold(int n){if(n<=0||n>=next_buffer||!buffers[n])return -1;holds[n]++;return 0;}
int dnd_buffer_unhold(int n){if(n<=0||n>=next_buffer||holds[n]<=0)return -1;holds[n]--;return 0;}
uint64_t dnd_buffer_address(int n,int offset){return n>0&&n<next_buffer&&buffers[n]&&offset>=0&&(unsigned)offset<sizes[n]?(uintptr_t)buffers[n]+(unsigned)offset:0;}
uint64_t dnd_buffer_held_address(int n,int offset,int count){return n>0&&n<next_buffer&&holds[n]&&offset>=0&&count>0&&(unsigned)(offset+count)<=sizes[n]?(uintptr_t)buffers[n]+(unsigned)offset:0;}
int dnd_draw_release(int n){if(n<=0||n>=next_buffer||holds[n])return -1;free(buffers[n]);buffers[n]=NULL;return 0;}
static FILE *recovery_file;
int dnd_fs_open_utf8_native(const char *path,int mode,int access,int share){assert(mode==3&&access==1&&share==1&&!recovery_file);char name[256];snprintf(name,sizeof(name),"out/disc/%s",path+5);recovery_file=fopen(name,"rb");return recovery_file?1:0;}
int64_t dnd_fs_length_native(int handle){assert(handle==1&&recovery_file);long p=ftell(recovery_file);assert(!fseek(recovery_file,0,SEEK_END));long n=ftell(recovery_file);assert(!fseek(recovery_file,p,SEEK_SET));return n;}
int dnd_fs_read_native(int handle,void *data,int count){assert(handle==1&&recovery_file);return (int)fread(data,1,(size_t)count,recovery_file);}
void dnd_fs_close(int handle){assert(handle==1&&recovery_file);assert(!fclose(recovery_file));recovery_file=NULL;}
bool dnd_fs_path_utf8_native(DndString *s,char *path,unsigned capacity){if(!s||s->length>=capacity)return false;for(unsigned i=0;i<s->length;i++)path[i]=(char)s->chars[i];path[s->length]=0;return true;}
int64_t dnd_timing_now(void){static int64_t tick;return ++tick;}
#include "../tools/native-asset-residency.c"
static DndString *str(const char *p){size_t n=strlen(p);DndString *s=calloc(1,sizeof(*s)+n*2);assert(s);s->length=(unsigned)n;for(size_t i=0;i<n;i++)s->chars[i]=(uint16_t)p[i];return s;}
static int load(const char *name){char path[256];snprintf(path,sizeof(path),"out/disc/%s.dndmodel",name);FILE *f=fopen(path,"rb");assert(f&&!fseek(f,0,SEEK_END));long n=ftell(f);assert(n>0&&!fseek(f,0,SEEK_SET));void *p=malloc((size_t)n);assert(p&&fread(p,1,(size_t)n,f)==(size_t)n&&!fclose(f));int token=dnd_model_begin_data(p,(unsigned)n,3*1024*1024);free(p);assert(token);return token;}
int main(void){
    assert(dnd_aram_initialize(2*1024*1024));int terrain=dnd_aram_allocate(8*1024*1024);assert(terrain&&dnd_aram_retain(terrain)==0);
    DndArray *priorities=calloc(1,sizeof(*priorities)+ASSETS*4);assert(priorities);priorities->length=ASSETS;priorities->element_size=4;int32_t *want=(int32_t*)priorities->data;
    const char *names[]={"fox","bunny","giraffe","cat","hamster","spaniel","cottage-floor-tex128","cottage-north-tex128","cottage-east-tex128","cottage-south-tex128","cottage-west-tex128"};
    int tokens[11],instances[11];unsigned hashes[11];
    for(int i=0;i<11;i++){
        tokens[i]=load(names[i]);DndModel *m=dnd_model_find(tokens[i],false);hashes[i]=m->hash;instances[i]=dnd_model_instance(tokens[i],512*1024);assert(instances[i]);
        char path[256];snprintf(path,sizeof(path),"dvd:/%s.dndmodel",names[i]);DndString *s=str(path);assert(animaltown_assets_capture(i,(int)m->bytes,0,s)==1);free(s);want[i]=2;
    }
    active=true;assert(animaltown_assets_advance(priorities,1)==-1&&animaltown_assets_forget(0)==0);active=false;
    // Loading/verification quanta leave the shared DMA channel idle, and never
    // mutate a resident model or its initial pose. Terrain can bind immediately.
    int quanta=0;
    while(animaltown_assets_stats(0)){
        assert(animaltown_assets_advance(priorities,1)>=0);assert(++quanta<1000);
        assert(dnd_aram_begin_native(terrain,0,stage,0,32,1)==0);while(!dnd_aram_poll_native()){}
        for(int i=0;i<11;i++)assert(dnd_model_find(tokens[i],false)->ready);
    }
    assert(animaltown_assets_stats(3)<6*1024*1024&&animaltown_assets_stats(7)==0);
    unsigned original=(unsigned)animaltown_assets_stats(2);
    assets[0].hold=5;
    for(int i=0;i<11;i++)want[i]=0;
    for(int i=0;i<4;i++)assert(animaltown_assets_advance(priorities,0)>=0);
    assert(animaltown_assets_state(0)&1); // Hysteresis survives a short retreat.
    for(int i=0;i<20;i++)assert(animaltown_assets_advance(priorities,0)>=0);
    assert(animaltown_assets_stats(1)==11&&(unsigned)animaltown_assets_stats(2)<original/2);
    for(int cycle=0;cycle<5;cycle++){
        for(int i=0;i<11;i++)want[i]=2;
        int steps=0;while(animaltown_assets_stats(1)){assert(animaltown_assets_advance(priorities,0)>=0);assert(++steps<1000);}
        for(int i=0;i<11;i++){DndModel *m=dnd_model_find(tokens[i],false);assert(m->ready&&m->hash==hashes[i]&&dnd_model_hash(m->package,m->length)==hashes[i]);assert(dnd_model_evaluate(dnd_instance_find(instances[i]))==0);}
        assert((unsigned)animaltown_assets_stats(2)==original);
        for(int i=0;i<11;i++)want[i]=0;
        for(int i=0;i<20;i++)assert(animaltown_assets_advance(priorities,0)>=0);
    }
    // Failed DMA during restore falls back to the exact original disc package.
    want[0]=2;dnd_aram_test_fail_next();assert(animaltown_assets_advance(priorities,0)>=0);
    int steps=0;while(!(animaltown_assets_state(0)&1)){assert(animaltown_assets_advance(priorities,0)>=0);assert(++steps<1000);}
    assert(animaltown_assets_stats(8)==1&&(animaltown_assets_state(0)&8));
    assert(dnd_model_hash(dnd_model_find(tokens[0],false)->package,assets[0].length)==hashes[0]);
    // A corrupted verified backing is rejected by the SDK hash before publish.
    memset(buffers[stage],0,32);assert(!dnd_aram_begin_native(assets[1].store,0,stage,0,32,1));while(!dnd_aram_poll_native()){}
    want[1]=2;steps=0;while(!(animaltown_assets_state(1)&1)){assert(animaltown_assets_advance(priorities,0)>=0);assert(++steps<1000);}
    assert(animaltown_assets_stats(8)==2&&dnd_model_hash(dnd_model_find(tokens[1],false)->package,assets[1].length)==hashes[1]);
    assert(invalidations==evictions+restores&&publications==restores);
    // Failed backing writes preserve the resident source and disable eviction.
    int safe=load("cottage-floor-tex128");DndString *safe_path=str("dvd:/cottage-floor-tex128.dndmodel");assert(animaltown_assets_capture(22,(int)dnd_model_find(safe,false)->bytes,0,safe_path));free(safe_path);
    dnd_aram_test_fail_next();assert(animaltown_assets_advance(priorities,0)>=0);assert((animaltown_assets_state(22)&9)==9);assert(animaltown_assets_forget(22)&&!dnd_model_dispose(safe));
    // Exhaust all free ARAM holes: allocation failure keeps the original live.
    int occupied[32],occupied_count=0;
    while(dnd_aram_stat(3)>0){assert(occupied_count<32);occupied[occupied_count]=dnd_aram_allocate(dnd_aram_stat(3));assert(occupied[occupied_count++]);}
    safe=load("cottage-floor-tex128");safe_path=str("dvd:/cottage-floor-tex128.dndmodel");assert(animaltown_assets_capture(22,(int)dnd_model_find(safe,false)->bytes,0,safe_path));free(safe_path);
    assert(animaltown_assets_advance(priorities,0)>=0&&(animaltown_assets_state(22)&9)==9);assert(animaltown_assets_forget(22)&&!dnd_model_dispose(safe));
    for(int i=0;i<occupied_count;i++)assert(!dnd_aram_dispose(occupied[i]));
    // Corrupt the initial backing before readback; it must never become cold.
    safe=load("cottage-floor-tex128");safe_path=str("dvd:/cottage-floor-tex128.dndmodel");assert(animaltown_assets_capture(22,(int)dnd_model_find(safe,false)->bytes,0,safe_path));free(safe_path);
    while(assets[22].phase!=2)assert(animaltown_assets_advance(priorities,0)>=0);
    memset(buffers[stage],0,32);assert(!dnd_aram_begin_native(assets[22].store,0,stage,0,32,1));while(!dnd_aram_poll_native()){}
    while(!(animaltown_assets_state(22)&8))assert(animaltown_assets_advance(priorities,0)>=0);
    assert(animaltown_assets_state(22)&1);assert(animaltown_assets_forget(22)&&!dnd_model_dispose(safe));
    // Native retention protects lifetime even if the managed owner closes early.
    safe=load("cottage-floor-tex128");safe_path=str("dvd:/cottage-floor-tex128.dndmodel");assert(animaltown_assets_capture(22,(int)dnd_model_find(safe,false)->bytes,0,safe_path));free(safe_path);
    assert(!dnd_model_dispose(safe)&&!(animaltown_assets_state(22)&1));assert(animaltown_assets_forget(22)&&!dnd_model_find(safe,true));
    // Registration is explicit and rejects simultaneous unclaimed loads.
    int a=load("fox"),b=load("bunny");DndString *path=str("dvd:/fox.dndmodel");assert(!animaltown_assets_capture(20,(int)dnd_model_find(a,false)->bytes,0,path));free(path);assert(!dnd_model_dispose(a)&&!dnd_model_dispose(b));
    for(int i=0;i<11;i++){assert(animaltown_assets_forget(i)==1);assert(!dnd_model_instance_dispose(instances[i])&&!dnd_model_dispose(tokens[i]));}
    assert(!animaltown_assets_stats(3));assert(!dnd_aram_release(terrain)&&!dnd_aram_dispose(terrain));
    printf("Asset residency: %d verified quanta; eleven shared models, five exact cold/warm cycles, memory reclamation, interleaved terrain DMA, disc recovery and active-frame guards passed\n",quanta);
    free(priorities);assert(!dnd_buffer_unhold(stage)&&!dnd_draw_release(stage));return 0;
}
