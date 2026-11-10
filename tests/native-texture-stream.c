#include "dnd_model_internal.h"
#include "dnd_draw.h"
#include "dnd_storage.h"
#include "dnd_aram.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
static int frame_active,corrupt_next,fail_next,reads,invalidations;
static FILE *files[32];static int next_file=1;
static void *buffers[128];static unsigned lengths[128];static int pins[128],next_buffer=1;
bool dnd_device_active(void){return frame_active!=0;}
void dnd_draw_backend_invalidate_cache(void){invalidations++;}
void dnd_draw_backend_publish_texture(void){invalidations++;}
void animaltown_shadows_model_reset(void){}
int64_t dnd_timing_now(void){static int64_t tick;return ++tick;}
int dnd_buffer_create(int bytes,int align){assert(bytes>0&&align==32&&next_buffer<128);int token=next_buffer++;buffers[token]=aligned_alloc(32,((unsigned)bytes+31u)&~31u);assert(buffers[token]);memset(buffers[token],0,(unsigned)bytes);lengths[token]=(unsigned)bytes;return token;}
int dnd_buffer_hold(int token){assert(token>0&&token<next_buffer);pins[token]++;return 0;}
int dnd_buffer_unhold(int token){assert(pins[token]>0);pins[token]--;return 0;}
uint64_t dnd_buffer_address(int token,int at){return token>0&&token<next_buffer&&buffers[token]&&at>=0&&(unsigned)at<lengths[token]?(uintptr_t)buffers[token]+(unsigned)at:0;}
uint64_t dnd_buffer_held_address(int token,int at,int count){return pins[token]&&at>=0&&count>0&&(unsigned)(at+count)<=lengths[token]?dnd_buffer_address(token,at):0;}
int dnd_draw_release(int token){assert(!pins[token]);free(buffers[token]);buffers[token]=NULL;return 0;}
int dnd_fs_open_utf8_native(const char *path,int mode,int access,int share){assert(mode==3&&access==1&&share==1&&next_file<32);char name[256];snprintf(name,sizeof(name),"out/disc/%s",path+5);FILE *f=fopen(name,"rb");if(!f)return 0;int token=next_file++;files[token]=f;return token;}
int64_t dnd_fs_length_native(int token){assert(files[token]);long p=ftell(files[token]);assert(!fseek(files[token],0,SEEK_END));long n=ftell(files[token]);assert(!fseek(files[token],p,SEEK_SET));return n;}
int dnd_fs_read_native(int token,void *data,int count){assert(files[token]);return (int)fread(data,1,(unsigned)count,files[token]);}
void dnd_fs_close(int token){assert(files[token]);assert(!fclose(files[token]));files[token]=NULL;}
bool dnd_fs_path_utf8_native(DndString *s,char *path,unsigned capacity){if(!s||s->length>=capacity)return false;for(unsigned i=0;i<s->length;i++)path[i]=(char)s->chars[i];path[s->length]=0;return true;}
int dnd_asset_read(int token,int64_t offset,int buffer,int destination,int count){
 assert(files[token]&&count>0&&count<=32768&&destination==0&&!pins[buffer]);reads++;if(fail_next){fail_next=0;return -1;}
 assert(!fseek(files[token],(long)offset,SEEK_SET));unsigned char *p=(void*)(uintptr_t)dnd_buffer_address(buffer,destination);assert(p);int n=(int)fread(p,1,(unsigned)count,files[token]);if(corrupt_next&&n){p[0]^=1;corrupt_next=0;}return n;
}
#include "../tools/native-texture-stream.c"
#define ANIMALTOWN_TEXTURE_STREAM 1
#include "../tools/native-asset-residency.c"
static void *bytes(const char *path,unsigned *length){FILE *f=fopen(path,"rb");assert(f&&!fseek(f,0,SEEK_END));long n=ftell(f);assert(n>0&&!fseek(f,0,SEEK_SET));void *p=malloc((size_t)n);assert(p&&fread(p,1,(size_t)n,f)==(size_t)n&&!fclose(f));*length=(unsigned)n;return p;}
static DndString *text_string(const char *p){unsigned n=(unsigned)strlen(p);DndString *s=calloc(1,sizeof(*s)+n*2);assert(s);s->length=n;for(unsigned i=0;i<n;i++)s->chars[i]=(uint16_t)p[i];return s;}
static void texture_work(void){for(int i=0;i<16;i++){int a=animaltown_textures_step(1);assert(a>=0);if(a==0)break;if(a==1){assert(animaltown_textures_stats(13));int count=animaltown_textures_read();animaltown_textures_complete(count);}}}
extern int town_pose_stats(int);
static unsigned pose_hash(DndInstance *in){
 unsigned hash=dnd_model_hash(in->global,in->model->nodes*64);
 for(unsigned i=0;i<in->model->meshes;i++)if(in->vertices[i])hash^=dnd_model_hash(in->vertices[i],in->model->mesh[i].vertices*24);
 return hash;
}
static void pose_checks(int model,int original){
 unsigned expected[60];assert(animaltown_textures_flags(127&~64)>=0);
 int baseline=dnd_model_instance(model,512*1024);assert(baseline);
 assert(dnd_model_play(baseline,0,1,0)==0);
 for(int i=0;i<60;i++){assert(!dnd_model_control(baseline,0,.016f));expected[i]=pose_hash(dnd_instance_find(baseline));}
 assert(!dnd_model_instance_dispose(baseline));assert(animaltown_textures_flags(127)>=0);
 int twin=dnd_model_instance(model,512*1024);assert(twin);
 DndInstance *a=dnd_instance_find(original),*b=dnd_instance_find(twin);
 unsigned old=b->bytes;b->bytes=a->bytes;assert(animaltown_textures_poseregister(0,0,(int)a->bytes)==0);b->bytes=old; // Ambiguous: explicit actor mapping refuses to guess.
 // Register before the second instance exists in the real startup path; here
 // account for an explicit temporary native budget difference.
 b->bytes=a->bytes+1;assert(animaltown_textures_poseregister(0,0,(int)a->bytes)==1);b->bytes=old;
 assert(animaltown_textures_poseregister(1,0,(int)b->bytes)==1);
 assert(animaltown_textures_posebatch(1)==1);
 assert(!dnd_model_play(original,0,1,0)&&!dnd_model_play(twin,0,1,0));
 assert(animaltown_textures_posebatch(0)==1);
 unsigned splits=(unsigned)town_pose_stats(3),evals=(unsigned)town_pose_stats(0);
 for(int i=0;i<60;i++){
  assert(animaltown_textures_posebatch(1)==1);
  assert(animaltown_textures_poseactor(0)==1&&animaltown_textures_poseactor(1)==1);
  assert(!dnd_model_control(original,0,.016f)&&!dnd_model_control(twin,0,.016f));
  assert(animaltown_textures_posebatch(0)==1);
  assert(pose_hash(a)==expected[i]&&pose_hash(b)==expected[i]);
  for(unsigned m=0;m<a->model->meshes;m++)if(a->model->mesh[m].skin>=0)assert(a->vertices[m]==b->vertices[m]);
 }
 assert((unsigned)town_pose_stats(0)-evals==60&&(unsigned)town_pose_stats(3)==splits&&town_pose_stats(4)>0);
 assert(!dnd_model_control(twin,4,.123f)&&pose_hash(a)==expected[59]);
 for(unsigned m=0;m<a->model->meshes;m++)if(a->model->mesh[m].skin>=0)assert(a->vertices[m]!=b->vertices[m]);
 // Blending and overrides remain independent; compare against the original evaluator.
 assert(!dnd_model_play(twin,1,1,.2f)&&!dnd_model_control(twin,0,.03f));
 unsigned blended=pose_hash(b);assert(animaltown_textures_flags(127&~64)>=0);assert(!dnd_model_evaluate(b)&&pose_hash(b)==blended);
 assert(animaltown_textures_flags(127)>=0);assert(!dnd_model_control(original,3,0)&&!dnd_model_control(twin,3,0));
 assert(town_pose_stats(4)>0);assert(!dnd_model_instance_dispose(twin));assert(!dnd_model_evaluate(a));
 puts("Exact live poses: independent reference hashes, synchronized buffer reuse, divergence COW, blends, disabled path and owner disposal passed");
}
int main(void){
 unsigned size;void *data=bytes("out/disc/textures.idx",&size);DndArray *index=calloc(1,sizeof(*index)+size);assert(index);index->element_size=1;index->length=size;memcpy(index->data,data,size);free(data);
 assert(dnd_aram_initialize(2*1024*1024));int terrain=dnd_aram_allocate(8*1024*1024);assert(terrain&&dnd_aram_retain(terrain)==0);
 assert(animaltown_textures_init(index)==1);free(index);int ticks=0;
 while(!animaltown_textures_stats(0)){animaltown_textures_begin(1);texture_work();assert(++ticks<1000);}
 assert(texture_count==9&&texture_bytes<1024*1024);
 frame_active=1;assert(animaltown_textures_begin(1)==0&&animaltown_textures_step(1)==-1);frame_active=0;
 const char *names[]={"fox","bunny","giraffe","cat","hamster","spaniel","cottage-exterior-tex128","cottage-door-tex128","cottage-floor-tex128","cottage-north-tex128","cottage-east-tex128","cottage-south-tex128","cottage-west-tex128"};
 int tokens[13],instances[13];unsigned payload_hash[13][8],payload_size[13][8];
 DndArray *demand=calloc(1,sizeof(*demand)+ASSETS*4);assert(demand);demand->element_size=4;demand->length=ASSETS;int32_t *want=(int32_t*)demand->data;
 for(int i=0;i<13;i++){
  char path[256];snprintf(path,sizeof(path),"out/disc/%s.dndmodel",names[i]);unsigned n;unsigned char *legacy=bytes(path,&n);unsigned at=dnd_model_u32(legacy+68)+4,count=dnd_model_u32(legacy+dnd_model_u32(legacy+68));assert(count<=8);
  for(unsigned t=0;t<count;t++){unsigned length=dnd_model_u32(legacy+at+24);payload_hash[i][t]=dnd_model_hash(legacy+at+28,length);payload_size[i][t]=length;at+=28+length;}free(legacy);
  snprintf(path,sizeof(path),"out/disc/%s-stream.dndmodel",names[i]);data=bytes(path,&n);tokens[i]=dnd_model_begin_data(data,n,3*1024*1024);free(data);assert(tokens[i]);DndModel *m=dnd_model_find(tokens[i],false);assert(m&&m->ready&&dnd_model_u32(m->package+4)==3);instances[i]=dnd_model_instance(tokens[i],512*1024);assert(instances[i]);
  snprintf(path,sizeof(path),"dvd:/%s-stream.dndmodel",names[i]);DndString *path_string=text_string(path);assert(animaltown_assets_capture(i,(int)m->bytes,0,path_string)==1);free(path_string);want[i]=2;
 }
 while(animaltown_assets_stats(0)){animaltown_textures_begin(1);assert(animaltown_assets_advance(demand,1)>=0);texture_work();assert(++ticks<3000);}
 pose_checks(tokens[0],instances[0]);
 assert(animaltown_assets_stats(3)<1500*1024); // Geometry backing no longer duplicates 2.64 MiB of images.
 for(int cycle=0;cycle<5;cycle++){
  ticks=0;
  while(1){
   int complete=1;animaltown_textures_begin(0);assert(animaltown_assets_advance(demand,0)>=0);
   for(int i=0;i<13;i++)assert(animaltown_textures_demand(i,256,2)==1);
   texture_work();
   for(int i=0;i<13;i++){
    DndModel *m=dnd_model_find(tokens[i],false);if(!m->ready){complete=0;continue;}
    for(unsigned t=0;t<m->textures;t++){DndTexture view;int format;assert(town_texture_view(m->texture+t,&view,&format));if(view.size!=payload_size[i][t])complete=0;else assert(format==6&&dnd_model_hash(view.data,view.size)==payload_hash[i][t]);}
   }
   assert(++ticks<3000);if(complete)break;
  }
  assert(texture_bytes<TEX_BUDGET);
  for(int i=0;i<13;i++)want[i]=0;
  for(int frame=0;frame<32;frame++){animaltown_textures_begin(0);assert(animaltown_assets_advance(demand,0)>=0);texture_work();}
  assert(animaltown_assets_stats(1)==13&&texture_bytes==0);
  for(int i=0;i<13;i++)want[i]=2;
 }
 // Optional detail corruption preserves an already verified minimum image.
 for(int n=0;n<3000&&animaltown_assets_stats(1);n++){animaltown_textures_begin(0);assert(animaltown_assets_advance(demand,0)>=0);texture_work();}
 assert(animaltown_assets_stats(1)==0);
 TownTexture *fox=textures+(unsigned)(-dnd_model_find(tokens[0],false)->texture[0].cache-2);assert(fox->base);
 unsigned failures_before=checksum_failures;corrupt_next=1;
 for(int n=0;n<1000&&checksum_failures==failures_before;n++){animaltown_textures_begin(0);animaltown_textures_demand(0,256,2);texture_work();}
 assert(checksum_failures>failures_before&&fox->base&&!fox->fatal);
 // Warm a single repeated image; the bounded ARAM cache must avoid some DVD reads.
 for(int n=0;n<2000&&!fox->detail;n++){animaltown_textures_begin(0);animaltown_textures_demand(0,256,2);texture_work();}
 assert(fox->detail);for(int n=0;n<32;n++){animaltown_textures_begin(0);animaltown_textures_demand(0,256,2);texture_work();}
 assert(texture_cache_store&&texture_cache_stores);
 fox->last_high=texture_frame-121;animaltown_textures_begin(0);texture_work();assert(!fox->detail&&fox->base);
 unsigned hits_before=texture_cache_hits;int reads_before=reads;
 for(int n=0;n<1000&&!fox->detail;n++){animaltown_textures_begin(0);animaltown_textures_demand(0,256,2);texture_work();}
 assert(fox->detail&&texture_cache_hits>hits_before&&reads-reads_before<8);
 // Poison a genuinely cached ARAM block; verification must reread DVD, never publish it.
 int poisoned=-1;const unsigned char *fox_range=tex_range(fox,0);
 for(int i=0;i<32;i++)if(texture_cache_blocks[i].valid&&texture_cache_blocks[i].bank==1&&texture_cache_blocks[i].offset>=tex_u32(fox_range+4)&&texture_cache_blocks[i].offset<tex_u32(fox_range+4)+tex_u32(fox_range+8)){poisoned=i;break;}
 assert(poisoned>=0);texture_cache_phase=0;texture_aram_used=0;assert(tex_cache_dma(poisoned,texture_stage[1],texture_cache_blocks[poisoned].count,0)==1);
 ((unsigned char*)(uintptr_t)dnd_buffer_address(texture_stage[1],0))[0]^=1;texture_aram_used=0;assert(tex_cache_dma(poisoned,texture_stage[1],texture_cache_blocks[poisoned].count,1)==1);
 unsigned cache_errors_before=texture_cache_errors;fox->last_high=texture_frame-121;animaltown_textures_begin(0);texture_work();assert(!fox->detail);
 for(int n=0;n<1000&&!fox->detail;n++){animaltown_textures_begin(0);animaltown_textures_demand(0,256,2);texture_work();}
 assert(fox->detail&&texture_cache_errors>cache_errors_before&&dnd_model_hash(fox->detail,fox->detail_size)==payload_hash[0][0]);
 unsigned cache_bytes_before=(unsigned)dnd_aram_stat(1);town_texture_reclaim_aram();assert(!texture_cache_store&&(unsigned)dnd_aram_stat(1)+1024*1024==cache_bytes_before);
 printf("Texture streaming: 13 external models, nine shared images, five exact cold/warm cycles; %d bounded reads, CRC rejection and active-frame guards passed\n",reads);
 return 0;
}
