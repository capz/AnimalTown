#include "dnd_verified_copy.h"
#include "model_residency_alloc.h"
#include "dnd_model_internal.h"
#include "dnd_aram.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#if defined(__GLIBC__)
#include <malloc.h>
#define USABLE_AVAILABLE 1
static size_t host_usable(void *p,size_t requested) {(void)requested;return malloc_usable_size(p);}
#elif defined(__APPLE__)
#include <malloc/malloc.h>
#define USABLE_AVAILABLE 1
static size_t host_usable(void *p,size_t requested) {(void)requested;return malloc_size(p);}
#else
#define USABLE_AVAILABLE 0
static size_t host_usable(void *p,size_t requested) {(void)p;return requested;}
#endif
bool dnd_device_active(void) {return false;}
void dnd_fs_close(int token) {(void)token;assert(!"No fixture file handle.");}
int dnd_buffer_hold(int token) {(void)token;return -1;}
int dnd_buffer_unhold(int token) {(void)token;return -1;}
uint64_t dnd_buffer_held_address(int token,int offset,int count) {
  (void)token;(void)offset;(void)count;return 0;
}
static void no_leaks(void) {
  assert(!dnd_model_live_handles());
  assert(!dnd_aram_broker_stat(0));
  assert(!dnd_aram_stat(1)&&!dnd_aram_stat(4));
}

/* Interpose only production model/model-animation translation units. Fixture
 * inputs/expectations and simulated external ARAM are deliberately excluded. */
typedef struct {void *pointer;size_t requested,usable;} Cell;
static Cell cells[8192];
static size_t live_requested,live_usable,peak_requested,peak_usable,live_cells;
static unsigned long alloc_calls,free_calls;
static void record(void *p,size_t requested) {
  if(!p)return;
  for(unsigned i=0;i<8192;i++)if(!cells[i].pointer){
    cells[i]=(Cell){p,requested,host_usable(p,requested)};
    live_requested+=requested;live_usable+=cells[i].usable;live_cells++;alloc_calls++;
    if(live_requested>peak_requested)peak_requested=live_requested;
    if(live_usable>peak_usable)peak_usable=live_usable;
    return;
  }
  assert(!"Allocation audit table exhausted.");
}
void *town_audit_calloc(size_t count,size_t size){void *p=calloc(count,size);record(p,count*size);return p;}
void *town_audit_aligned_alloc(size_t alignment,size_t size){void *p=aligned_alloc(alignment,size);record(p,size);return p;}
void town_audit_free(void *p){
  if(!p)return;
  for(unsigned i=0;i<8192;i++)if(cells[i].pointer==p){
    live_requested-=cells[i].requested;live_usable-=cells[i].usable;live_cells--;free_calls++;
    cells[i].pointer=NULL;free(p);return;
  }
  assert(!"Production freed an untracked allocation.");
}
static void digest(const void *p,size_t size,unsigned char out[32]){
  DndSha256 s;dnd_sha256_init(&s);if(size)dnd_sha256_update(&s,p,size);dnd_sha256_finish(&s,out);
}
static void hex(const unsigned char d[32],char out[65]){
  for(unsigned i=0;i<32;i++)snprintf(out+i*2,3,"%02x",d[i]);
}
static void skin_digest(DndInstance *in,unsigned char out[32]){
  DndSha256 s;dnd_sha256_init(&s);
  for(unsigned i=0;i<in->model->meshes;i++)if(in->model->mesh[i].skin>=0){
    assert(in->vertices[i]);dnd_sha256_update(&s,in->vertices[i],in->model->mesh[i].vertices*6*sizeof(float));
  }
  dnd_sha256_finish(&s,out);
}
static void texture_digest(DndModel *m,unsigned char out[32]){
  DndSha256 s;dnd_sha256_init(&s);
  for(unsigned i=0;i<m->textures;i++){
    assert(m->texture[i].data);dnd_sha256_update(&s,m->texture[i].data,m->texture[i].size);
  }
  dnd_sha256_finish(&s,out);
}
static unsigned steps,maximum_bytes,maximum_work;
static void complete_model(int token){
  for(unsigned tries=0;tries<1000000;tries++){
    int rc=dnd_model_residency_step(token,4096,32,0);
    assert(rc>=0);steps++;
    unsigned bytes=(unsigned)dnd_model_residency_stat(token,1),work=(unsigned)dnd_model_residency_stat(token,2);
    assert(bytes<=4096&&work<=32);
    if(bytes>maximum_bytes)maximum_bytes=bytes;
    if(work>maximum_work)maximum_work=work;
    if(rc==1)return;
  }
  assert(!"Actual model residency stalled.");
}
typedef struct {
  int token,store,instance;
  unsigned length,materials,authored;
  unsigned char *source;
  unsigned char package_hash[32],material_hash[32],authored_hash[32],texture_hash[32],skin_hash[32],global_hash[32],pose_hash[32];
  float time,next_time,elapsed,speed;
  int clip,next;
  bool paused;
  unsigned warm_model_bytes,warm_instance_bytes;
  size_t requested_freed,usable_freed,cells_freed;
} Resource;
static const char *names[6]={"fox","bunny","giraffe","cat","hamster","spaniel"};
static void capture_actor(Resource *r){
  DndInstance *in=dnd_instance_find(r->instance);assert(in);
  DndModel *m=in->model;
  if(m->clips){
    int walk=0;for(unsigned i=0;i<m->clips;i++)if(!strcmp(m->clip[i].name,"Walking"))walk=(int)i;
    assert(dnd_model_play(in->token,walk,1,0)==0);
    assert(dnd_model_control(in->token,0,.375f)==0);
    if(m->clips>=2){assert(dnd_model_play(in->token,walk?0:1,1,.75f)==0);assert(dnd_model_control(in->token,0,.125f)==0);}
  }
  assert(dnd_model_control(in->token,5,1.25f)==0);
  assert(dnd_model_control(in->token,1,0)==0);
  r->time=in->time;r->next_time=in->next_time;r->elapsed=in->elapsed;r->speed=in->speed;r->paused=in->paused;r->clip=in->clip;r->next=in->next;
  r->warm_instance_bytes=in->bytes;
  skin_digest(in,r->skin_hash);digest(in->global,m->nodes*64,r->global_hash);digest(in->pose,m->nodes*sizeof(DndPose),r->pose_hash);
}
static void verify_actor(Resource *r){
  DndInstance *in=dnd_instance_find(r->instance);assert(in&&in->model->ready);
  unsigned char hash[32];skin_digest(in,hash);assert(!memcmp(hash,r->skin_hash,32));
  digest(in->global,in->model->nodes*64,hash);assert(!memcmp(hash,r->global_hash,32));
  digest(in->pose,in->model->nodes*sizeof(DndPose),hash);assert(!memcmp(hash,r->pose_hash,32));
  assert(in->time==r->time&&in->next_time==r->next_time&&in->elapsed==r->elapsed&&in->speed==r->speed&&in->paused==r->paused&&in->clip==r->clip&&in->next==r->next);
  assert(in->bytes==r->warm_instance_bytes);
}
int main(int argc,char **argv){
  assert(argc==3);int edge=atoi(argv[2]);assert(edge==128||edge==256);
  assert(dnd_aram_initialize(2*1024*1024));
  int terrain=dnd_aram_allocate(8*1024*1024);assert(terrain);
  Resource resource[6]={{0}},player={0};
  unsigned total_packages=0,backing_bytes=0;
  for(unsigned i=0;i<6;i++){
    Resource *r=resource+i;char path[4096];
    snprintf(path,sizeof(path),"%s/%s%s.dndmodel",argv[1],names[i],edge==128?"-tex128":"");
    FILE *f=fopen(path,"rb");assert(f);assert(!fseek(f,0,SEEK_END));long n=ftell(f);assert(n>=160&&!fseek(f,0,SEEK_SET));
    r->length=(unsigned)n;r->source=malloc(r->length);assert(r->source);
    assert(fread(r->source,1,r->length,f)==r->length);assert(!fclose(f));
    r->token=dnd_model_begin_data(r->source,r->length,3*1024*1024);assert(r->token);
    DndModel *m=dnd_model_find(r->token,false);assert(m);
    r->materials=m->materials;r->authored=m->authored_count;r->warm_model_bytes=m->bytes;
    digest(m->package,m->length,r->package_hash);digest(m->material,m->materials*sizeof(DndMaterial),r->material_hash);
    digest(m->authored,m->authored_count*sizeof(DndAuthoredMaterial),r->authored_hash);texture_digest(m,r->texture_hash);
    r->instance=dnd_model_instance(r->token,512*1024);assert(r->instance);capture_actor(r);
    total_packages+=r->length;
  }
  player.token=resource[0].token;player.instance=dnd_model_instance(player.token,512*1024);assert(player.instance);capture_actor(&player);
  size_t warm_requested=live_requested,warm_usable=live_usable,warm_cells=live_cells;
  peak_requested=live_requested;peak_usable=live_usable;
  for(unsigned i=1;i<6;i++){
    Resource *r=resource+i;r->store=dnd_aram_allocate((int)((r->length+31)&~31u));assert(r->store);
    backing_bytes+=(r->length+31)&~31u;assert(backing_bytes<=4*1024*1024);
    size_t before_requested=live_requested,before_usable=live_usable,before_cells=live_cells;
    assert(!dnd_model_residency_begin(r->token,r->store,0));complete_model(r->token);
    DndModel *m=dnd_model_find(r->token,false);assert(m&&!m->ready&&!m->package&&!m->residency);
    r->requested_freed=before_requested-live_requested;r->usable_freed=before_usable-live_usable;r->cells_freed=before_cells-live_cells;
    assert(r->requested_freed>=r->length&&r->usable_freed>=r->length);
    verify_actor(resource);verify_actor(&player);
  }
  size_t cold_requested=live_requested,cold_usable=live_usable,cold_cells=live_cells;
  unsigned aram_with_backing=(unsigned)dnd_aram_stat(1);
  assert(aram_with_backing==8*1024*1024+backing_bytes);
  for(unsigned i=1;i<6;i++){
    Resource *r=resource+i;assert(!dnd_model_residency_begin(r->token,r->store,1));complete_model(r->token);
    DndModel *m=dnd_model_find(r->token,false);assert(m&&m->ready&&m->package&&!m->residency);
    unsigned char hash[32];digest(m->package,m->length,hash);assert(!memcmp(hash,r->package_hash,32));assert(!memcmp(m->package,r->source,r->length));
    digest(m->material,m->materials*sizeof(DndMaterial),hash);assert(!memcmp(hash,r->material_hash,32));
    digest(m->authored,m->authored_count*sizeof(DndAuthoredMaterial),hash);assert(!memcmp(hash,r->authored_hash,32));
    texture_digest(m,hash);assert(!memcmp(hash,r->texture_hash,32));
    assert(m->bytes==r->warm_model_bytes);verify_actor(r);verify_actor(resource);verify_actor(&player);
  }
  assert(live_requested==warm_requested&&live_cells==warm_cells);
  size_t restored_usable=live_usable;
  printf("{\"edge\":%d,\"hostPointerBits\":%zu,\"hostUsableMeasurementAvailable\":%s,\"packagesBytes\":%u,\"coldBackingBytes\":%u,",edge,sizeof(void *)*8,USABLE_AVAILABLE?"true":"false",total_packages,backing_bytes);
  printf("\"warmNativeRequestedBytes\":%zu,\"coldNativeRequestedBytes\":%zu,\"requestedBytesFreed\":%zu,",warm_requested,cold_requested,warm_requested-cold_requested);
  printf("\"warmHostNativeUsableBytes\":%zu,\"coldHostNativeUsableBytes\":%zu,\"hostNativeUsableBytesFreed\":%zu,\"restoredHostNativeUsableBytes\":%zu,",warm_usable,cold_usable,warm_usable-cold_usable,restored_usable);
  printf("\"warmNativeAllocations\":%zu,\"coldNativeAllocations\":%zu,\"nativeAllocationsFreed\":%zu,\"peakNativeRequestedBytes\":%zu,\"peakHostNativeUsableBytes\":%zu,",warm_cells,cold_cells,warm_cells-cold_cells,peak_requested,peak_usable);
  printf("\"steps\":%u,\"maximumStepBytes\":%u,\"maximumStepWork\":%u,\"aramCommittedBytes\":%u,\"dmaReadBytes\":%d,\"dmaWriteBytes\":%d,\"models\":[",steps,maximum_bytes,maximum_work,aram_with_backing,dnd_aram_broker_stat(3),dnd_aram_broker_stat(4));
  for(unsigned i=0;i<6;i++){
    Resource *r=resource+i;char h[65];hex(r->package_hash,h);
    printf("%s{\"species\":\"%s\",\"packageBytes\":%u,\"sha256\":\"%s\",\"nativeRequestedBytesFreed\":%zu,\"hostNativeUsableBytesFreed\":%zu,\"nativeAllocationsFreed\":%zu}",i?",":"",names[i],r->length,h,r->requested_freed,r->usable_freed,r->cells_freed);
  }
  printf("],\"packageMaterialTextureSkinPoseAndAnimationParity\":true}");
  assert(!dnd_model_instance_dispose(player.instance));
  for(unsigned i=0;i<6;i++){
    Resource *r=resource+i;assert(!dnd_model_instance_dispose(r->instance));assert(!dnd_model_dispose(r->token));
    if(r->store)assert(!dnd_aram_dispose(r->store));
    free(r->source);
  }
  assert(!dnd_aram_dispose(terrain));no_leaks();
  assert(!live_requested&&!live_usable&&!live_cells&&alloc_calls==free_calls);
  puts("");return 0;
}
