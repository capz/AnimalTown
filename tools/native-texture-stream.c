/* Application-thread ownership, detached native DVD reads, bounded publication.
 * No managed pointers survive a call and no GX work runs on the I/O executor. */
#include "native-texture-stream.h"
#include "dnd_storage.h"
#include "dnd_draw.h"
#include "dnd_aram.h"
#include <stdlib.h>
#include <string.h>
#include <limits.h>
#ifdef DND_GAMECUBE_STORAGE
#include <gccore.h>
#include <malloc.h>
#endif
#define TEX_CAP 512
#define TEX_MODEL_CAP 34
#define TEX_QUANTUM 32768u
#define TEX_BUDGET (4u*1024u*1024u)
#define TEX_MIN(a,b) ((a)<(b)?(a):(b))
typedef struct {
 unsigned char *base,*detail;
 unsigned base_size,detail_size,refs,generation,want,last_high,retry,errors;
 int detail_level,priority,fatal;
 const unsigned char *record;
} TownTexture;
typedef struct {
 int texture,level,first,end,minimum,read_ready,cancel,cache_hit,cache_candidate;
 unsigned char *data;
 unsigned size,destination,level_position,block_position,block_count,block_crc,level_crc,previous_crc;
 int bank;unsigned read_offset,read_count;
} TownTextureJob;
static TownTexture textures[TEX_CAP];
static DndModel *texture_models[TEX_MODEL_CAP];
static TownTextureJob texture_job={.texture=-1};
static unsigned char *texture_catalog;
static unsigned texture_count,texture_model_count,texture_blocks,model_offset,block_offset,texture_frame;
static unsigned texture_bytes,texture_peak,promotions,demotions,io_reads,io_bytes,io_failures,checksum_failures,publication_count;
static int texture_banks[2],texture_stage[2],texture_initialized,texture_warm=1,texture_io_pending,texture_aram_used;
static int texture_features=127;
static unsigned texture_crc_table[256];
extern void town_model_texture_reset(void) __attribute__((weak));
extern int town_geometry_pump(void) __attribute__((weak));
extern void town_geometry_begin(unsigned) __attribute__((weak));
extern void town_geometry_clear(void) __attribute__((weak));
extern int town_geometry_stats(int) __attribute__((weak));
extern int town_geometry_scope(int) __attribute__((weak));
extern int town_pose_register_actor(int,DndModel*,unsigned) __attribute__((weak));
extern int town_pose_actor(int) __attribute__((weak));
extern int town_pose_batch(int) __attribute__((weak));
extern int town_pose_stats(int) __attribute__((weak));
static unsigned tex_u32(const unsigned char *p){return (unsigned)p[0]|(unsigned)p[1]<<8|(unsigned)p[2]<<16|(unsigned)p[3]<<24;}
static unsigned tex_crc(unsigned c,const unsigned char *p,unsigned n){while(n--)c=texture_crc_table[(c^*p++)&255u]^(c>>8);return c;}
static unsigned tex_field(const TownTexture *t,unsigned k){return tex_u32(t->record+k*4);}
static const unsigned char *tex_range(const TownTexture *t,int level){return t->record+32+(unsigned)level*24;}
static unsigned tex_charge(unsigned n){return (n+63u)&~31u;}
static int tex_memory_available(unsigned charge){
#ifdef DND_GAMECUBE_STORAGE
 struct mallinfo info=mallinfo();uint64_t free=(uint64_t)SYS_GetArena1Size()+info.fordblks,total=SYS_GetPhysicalMem1Size();
 if(total>free&&total-free+charge>22u*1024u*1024u)return 0;
#else
 (void)charge;
#endif
 return 1;
}
static unsigned char *tex_allocate(unsigned size,int required){
 if(size>UINT_MAX-63)return NULL;
 unsigned charge=tex_charge(size);if(charge>TEX_BUDGET||texture_bytes>TEX_BUDGET-charge||(!required&&!tex_memory_available(charge)))return NULL;
#ifdef DND_GAMECUBE_STORAGE
 unsigned *p=memalign(32,charge);
#else
 unsigned *p=aligned_alloc(32,charge);
#endif
 if(!p)return NULL;
 p[0]=charge;texture_bytes+=charge;if(texture_bytes>texture_peak)texture_peak=texture_bytes;return (unsigned char*)p+32;
}
static void tex_free(unsigned char *p){if(p){unsigned *allocation=(unsigned*)(p-32);texture_bytes-=allocation[0];free(allocation);}}
static void tex_publish(void){dnd_draw_backend_publish_texture();if(town_model_texture_reset)town_model_texture_reset();publication_count++;}
static unsigned tex_size(const TownTexture *t,int level,int end){unsigned n=0;for(int l=level;l<end;l++)n+=tex_u32(tex_range(t,l)+8);return n;}
static void tex_flush(void *p,unsigned n){
#ifdef DND_GAMECUBE_STORAGE
 DCFlushRange(p,n);
#else
 (void)p;(void)n;
#endif
}
static void tex_abort(int failure){
 TownTextureJob *j=&texture_job;if(j->texture<0)return;TownTexture *t=textures+j->texture;
 tex_free(j->data);j->data=NULL;
 if(failure){t->errors++;t->retry=texture_frame+120;if(j->minimum&&t->errors>=2)t->fatal=1;}
 j->texture=-1;
}
#include "native-texture-cache.inc"
static int tex_catalog_valid(const unsigned char *p,unsigned n){
 if(n<32||tex_u32(p)!=0x58545441u||tex_u32(p+4)!=1)return 0;
 unsigned count=tex_u32(p+8),models=tex_u32(p+12),blocks=tex_u32(p+16),records=tex_u32(p+20),mo=tex_u32(p+24),bo=tex_u32(p+28);
 if(!count||count>TEX_CAP||models>128||records!=32||mo!=32+count*256||bo!=mo+models*128||bo>n||blocks>(n-bo)/4||bo+blocks*4!=n)return 0;
 for(unsigned i=0;i<models;i++)if(!memchr(p+mo+i*128,0,128))return 0;
 for(unsigned i=0;i<count;i++){
  const unsigned char *r=p+32+i*256;unsigned w=tex_u32(r),h=tex_u32(r+4),format=tex_u32(r+8),minimum=tex_u32(r+12),safe=tex_u32(r+16),levels=tex_u32(r+20);
  if(w<4||h<4||w>1024||h>1024||(w&(w-1))||(h&(h-1))||format!=6||!levels||levels>9||!safe||safe>levels||minimum>=safe)return 0;
  for(unsigned l=0;l<levels;l++){
   const unsigned char *q=r+32+l*24;unsigned bank=tex_u32(q),offset=tex_u32(q+4),size=tex_u32(q+8),block=tex_u32(q+16),bc=tex_u32(q+20);
   if((w>>l)<4||(h>>l)<4||bank>1||(offset&31)||size!=(w>>l)*(h>>l)*4||bc!=(size+TEX_QUANTUM-1)/TEX_QUANTUM||block>blocks||bc>blocks-block)return 0;
   int64_t length=dnd_fs_length_native(texture_banks[bank]);if(length<0||offset>(uint64_t)length||size>(uint64_t)length-offset)return 0;
  }
 }
 return 1;
}
int animaltown_textures_init(DndArray *catalog){
 if(texture_initialized||dnd_device_active()||!catalog||catalog->element_size!=1||catalog->length>1024u*1024u)return 0;
 texture_banks[0]=dnd_fs_open_utf8_native("dvd:/textures-base.bank",3,1,1);texture_banks[1]=dnd_fs_open_utf8_native("dvd:/textures-detail.bank",3,1,1);
 if(!texture_banks[0]||!texture_banks[1]||!tex_catalog_valid(catalog->data,catalog->length))goto fail;
 texture_catalog=malloc(catalog->length);if(!texture_catalog)goto fail;memcpy(texture_catalog,catalog->data,catalog->length);
 for(unsigned i=0;i<256;i++){unsigned c=i;for(int b=0;b<8;b++)c=(c>>1)^((c&1)?0xedb88320u:0);texture_crc_table[i]=c;}
 texture_count=tex_u32(texture_catalog+8);texture_model_count=tex_u32(texture_catalog+12);texture_blocks=tex_u32(texture_catalog+16);model_offset=tex_u32(texture_catalog+24);block_offset=tex_u32(texture_catalog+28);
 for(unsigned i=0;i<texture_count;i++){TownTexture *t=textures+i;t->record=texture_catalog+32+i*256;t->want=tex_field(t,3);t->detail_level=-1;}
 for(int i=0;i<2;i++){texture_stage[i]=dnd_buffer_create(TEX_QUANTUM,32);if(!texture_stage[i]||dnd_buffer_hold(texture_stage[i]))goto fail;}
 texture_initialized=1;return 1;
 fail:
 for(int i=0;i<2;i++)if(texture_stage[i]){(void)dnd_buffer_unhold(texture_stage[i]);(void)dnd_draw_release(texture_stage[i]);texture_stage[i]=0;}
 for(int i=0;i<2;i++)if(texture_banks[i]){dnd_fs_close(texture_banks[i]);texture_banks[i]=0;}
 free(texture_catalog);texture_catalog=NULL;return 0;
}
int animaltown_textures_hasmodel(DndString *path){
 if(!texture_initialized||!path)return 0;
 char name[128];if(!dnd_fs_path_utf8_native(path,name,sizeof(name)))return 0;
 for(unsigned i=0;i<texture_model_count;i++)if(!strcmp(name,(char*)texture_catalog+model_offset+i*128))return 1;
 return 0;
}
int animaltown_textures_begin(int startup){
 if(!texture_initialized||dnd_device_active())return 0;
 texture_frame++;texture_aram_used=0;texture_warm=startup;if(town_geometry_begin)town_geometry_begin(texture_frame);
 for(unsigned i=0;i<texture_count;i++){TownTexture *t=textures+i;t->want=tex_field(t,3);t->priority=0;}
 return 1;
}
void town_texture_aram_used(void){texture_aram_used=1;}
int town_texture_io_busy(void){return texture_io_pending;}
void town_texture_model(int slot,DndModel *m){if(slot>=0&&slot<TEX_MODEL_CAP)texture_models[slot]=m;}
int animaltown_textures_demand(int slot,int pixels,int priority){
 if(slot<0||slot>=TEX_MODEL_CAP||pixels<0||priority<0||priority>2)return 0;
 DndModel *m=texture_models[slot];if(!m)return 1;
 for(unsigned i=0;i<m->textures;i++){
  DndTexture *tx=m->texture+i;if(tx->cache> -2)continue;unsigned id=(unsigned)(-tx->cache-2);if(id>=texture_count)return 0;
  TownTexture *t=textures+id;unsigned level=0;while(level<tex_field(t,3)&&TEX_MIN(tex_field(t,0)>>(level+1),tex_field(t,1)>>(level+1))>=(unsigned)pixels)level++;
  if(priority&&level<t->want)t->want=level;
  if(priority>t->priority)t->priority=priority;
  if(priority&&level<tex_field(t,3))t->last_high=texture_frame;
 }
 return 1;
}
int town_texture_acquire(DndTexture *tx,unsigned id){
 if(id>=texture_count)return 0;
 TownTexture *t=textures+id;
 if(!t->base){t->priority=2;return t->fatal?-1:0;}
 unsigned minimum=tex_field(t,3);tx->width=tex_field(t,0)>>minimum;tx->height=tex_field(t,1)>>minimum;tx->levels=tex_field(t,4)-minimum;tx->size=t->base_size;tx->data=t->base;tx->cache=-(int)id-2;t->refs++;return 1;
}
void town_texture_release(DndTexture *tx){
 if(!tx||tx->cache> -2||!tx->data)return;
 unsigned id=(unsigned)(-tx->cache-2);if(id>=texture_count)return;TownTexture *t=textures+id;
 if(t->refs)t->refs--;
 tx->data=NULL;
 if(!t->refs&&!texture_warm){tex_free(t->base);tex_free(t->detail);t->base=t->detail=NULL;t->base_size=t->detail_size=0;t->detail_level=-1;if(texture_job.texture==(int)id)texture_job.cancel=1;tex_publish();}
}
int town_texture_view(const DndTexture *original,DndTexture *view,int *format){
 *view=*original;*format=6;if(original->cache> -2)return 1;unsigned id=(unsigned)(-original->cache-2);if(id>=texture_count)return 0;
 TownTexture *t=textures+id;int level=t->detail?t->detail_level:(int)tex_field(t,3);view->data=t->detail?t->detail:t->base;if(!view->data)return 0;
 view->width=tex_field(t,0)>>(unsigned)level;view->height=tex_field(t,1)>>(unsigned)level;view->levels=tex_field(t,4)-(unsigned)level;
 if(!(texture_features&2)&&view->levels>2)view->levels=2;
 view->size=t->detail?t->detail_size:t->base_size;*format=(int)tex_field(t,2);return 1;
}
int animaltown_textures_flags(int flags){if(flags<0)return texture_features;if(dnd_device_active()||flags>127)return -1;texture_features=flags;if(!(flags&8))town_texture_reclaim_aram();if(!(flags&32)&&town_geometry_clear)town_geometry_clear();tex_publish();return flags;}
static void tex_start(int id,int level){
 TownTexture *t=textures+id;TownTextureJob *j=&texture_job;int minimum=level==(int)tex_field(t,3);unsigned size=tex_size(t,level,(int)tex_field(t,4));
 unsigned char *data=tex_allocate(size,minimum);
 if(!data&&minimum){
  for(unsigned i=0;i<texture_count;i++)if(textures[i].detail){tex_free(textures[i].detail);textures[i].detail=NULL;textures[i].detail_size=0;textures[i].detail_level=-1;demotions++;}
  tex_publish();data=tex_allocate(size,1);
 }
 if(!data){t->retry=texture_frame+30;if(minimum)t->fatal=1;return;}
 *j=(TownTextureJob){.texture=id,.level=level,.first=level,.end=(int)tex_field(t,4),.minimum=minimum,.cache_hit=-1,.data=data,.size=size,.block_crc=~0u,.level_crc=~0u};
}
int animaltown_textures_step(int allow_read){
 if(!texture_initialized||dnd_device_active())return -1;
 TownTextureJob *j=&texture_job;
 if(tex_cache_pump())return 2;
 if(texture_io_pending)return 0;
 if(j->texture>=0&&!j->minimum)for(unsigned i=0;i<texture_count;i++)if(!textures[i].base&&textures[i].priority==2&&textures[i].retry<=texture_frame&&!textures[i].fatal){j->cancel=1;break;}
 if(j->texture>=0&&j->cancel){tex_abort(0);return 2;}
 if(j->texture<0){
  for(unsigned i=0;i<texture_count;i++){
   TownTexture *t=textures+i;
   if(t->detail&&(texture_features&1)&&texture_frame-t->last_high>=120){tex_free(t->detail);t->detail=NULL;t->detail_size=0;t->detail_level=-1;demotions++;tex_publish();return 2;}
   if(t->fatal)return -1;
  }
  for(int phase=0;phase<2;phase++)for(int priority=2;priority>=0;priority--)for(unsigned i=0;i<texture_count;i++){
   TownTexture *t=textures+i;if(t->retry>texture_frame||t->priority!=priority)continue;
   if(phase==0&&!t->base&&(texture_warm||t->priority||t->refs)){tex_start((int)i,(int)tex_field(t,3));return texture_job.texture>=0?2:0;}
   unsigned desired=(texture_features&1)?t->want:0;
   if(phase==1&&t->base&&t->refs&&desired<tex_field(t,3)&&(!t->detail||desired<(unsigned)t->detail_level)){tex_start((int)i,(int)desired);return texture_job.texture>=0?2:0;}
  }
  return town_geometry_pump&&town_geometry_pump()?2:0;
 }
 TownTexture *t=textures+j->texture;
 if(j->level==j->end){
  if(j->minimum){t->base=j->data;t->base_size=j->size;}else{tex_free(t->detail);t->detail=j->data;t->detail_size=j->size;t->detail_level=j->first;promotions++;}
  t->generation++;t->errors=0;j->data=NULL;j->texture=-1;tex_publish();return 2;
 }
 const unsigned char *r=tex_range(t,j->level);unsigned length=tex_u32(r+8);
 // Promotions reuse verified minimum levels instead of rereading DVD tails.
 if(!j->minimum&&j->level>=(int)tex_field(t,3)){
  unsigned base_offset=tex_size(t,(int)tex_field(t,3),j->level);unsigned count=TEX_MIN(4096u,length-j->level_position);
  memcpy(j->data+j->destination,t->base+base_offset+j->level_position,count);tex_flush(j->data+j->destination,count);j->destination+=count;j->level_position+=count;
  if(j->level_position==length){j->level++;j->level_position=0;}return 2;
 }
 if(j->read_ready){
  unsigned count=TEX_MIN(4096u,j->read_count-j->block_position);unsigned char *source=(unsigned char*)(uintptr_t)dnd_buffer_address(texture_stage[0],(int)j->block_position);if(!source)return -1;
  if(j->block_position==0){
   j->previous_crc=j->level_crc;unsigned block=tex_u32(r+16)+j->level_position/TEX_QUANTUM;
   j->cache_candidate=j->cache_hit<0&&tex_cache_begin_store(j->bank,j->read_offset,j->read_count,tex_u32(texture_catalog+block_offset+block*4));
  }
  if(j->cache_candidate){void *cache=(void*)(uintptr_t)dnd_buffer_address(texture_stage[1],(int)j->block_position);if(!cache)return -1;memcpy(cache,source,count);}
  j->block_crc=tex_crc(j->block_crc,source,count);j->level_crc=tex_crc(j->level_crc,source,count);memcpy(j->data+j->destination,source,count);tex_flush(j->data+j->destination,count);j->destination+=count;j->block_position+=count;
  if(j->block_position==j->read_count){
   unsigned block=tex_u32(r+16)+j->level_position/TEX_QUANTUM;
   if(block>=texture_blocks||~j->block_crc!=tex_u32(texture_catalog+block_offset+block*4)){checksum_failures++;
    if(j->cache_hit>=0){texture_cache_blocks[j->cache_hit].valid=0;texture_cache_errors++;j->destination-=j->read_count;j->level_crc=j->previous_crc;j->block_position=0;j->block_crc=~0u;j->read_ready=0;j->cache_hit=-1;j->cache_candidate=0;return 2;}
    tex_abort(1);return 2;}
   if(j->cache_candidate)tex_cache_finish_store();
   j->cache_candidate=0;j->cache_hit=-1;
   j->level_position+=j->read_count;j->read_ready=0;j->block_position=0;j->block_crc=~0u;
   if(j->level_position==length){if(~j->level_crc!=tex_u32(r+12)){checksum_failures++;tex_abort(1);return 2;}j->level++;j->level_position=0;j->level_crc=~0u;}
  }
  return 2;
 }
 if(!allow_read)return 0;
 j->bank=(int)tex_u32(r);j->read_offset=tex_u32(r+4)+j->level_position;j->read_count=TEX_MIN(TEX_QUANTUM,length-j->level_position);
 unsigned block=tex_u32(r+16)+j->level_position/TEX_QUANTUM;int hit=tex_cache_read(j->bank,j->read_offset,j->read_count,tex_u32(texture_catalog+block_offset+block*4));
 if(hit>=0){j->cache_hit=hit;j->read_ready=1;return 2;}if(hit== -2)return 0;
 // The storage reader takes an exclusive native-buffer lease. ARAM uses
 // held leases only between reads; a held destination cannot enter I/O.
 if(dnd_buffer_unhold(texture_stage[0]))return -1;
 j->cache_hit=-1;texture_io_pending=1;return 1;
}
int animaltown_textures_read(void){
 if(!texture_io_pending||texture_job.texture<0)return -1;
 return dnd_asset_read(texture_banks[texture_job.bank],texture_job.read_offset,texture_stage[0],0,(int)texture_job.read_count);
}
int animaltown_textures_complete(int count){
 if(!texture_io_pending)return 0;
 texture_io_pending=0;io_reads++;
 if(dnd_buffer_hold(texture_stage[0]))return -1;
 if(count!=(int)texture_job.read_count){io_failures++;tex_abort(1);return 0;}
 io_bytes+=(unsigned)count;texture_job.read_ready=1;return 1;
}
int animaltown_textures_stats(int kind){
 unsigned base=0,detail=0,ready=0,fatal=0;for(unsigned i=0;i<texture_count;i++){base+=textures[i].base_size;detail+=textures[i].detail_size;ready+=textures[i].base!=NULL;fatal+=textures[i].fatal!=0;}
 return kind==0?(int)(ready==texture_count):kind==1?(int)texture_bytes:kind==2?(int)texture_peak:kind==3?(int)base:kind==4?(int)detail:kind==5?(int)promotions:kind==6?(int)demotions:kind==7?(int)io_reads:kind==8?(int)io_bytes:kind==9?(int)io_failures:kind==10?(int)checksum_failures:kind==11?(int)fatal:kind==12?(int)publication_count:kind==13?texture_io_pending:kind==14?texture_job.texture:kind==15?(int)texture_cache_hits:kind==16?(int)texture_cache_misses:kind==17?(int)texture_cache_bytes:kind==18?(texture_cache_store?1024*1024:0):kind==19?(int)texture_cache_stores:kind==20?(int)texture_cache_errors:kind>=32&&kind<38&&town_geometry_stats?town_geometry_stats(kind-32):kind>=40&&kind<45&&town_pose_stats?town_pose_stats(kind-40):-1;
}

int town_texture_minimum(unsigned id){if(id>=texture_count)return -1;TownTexture *t=textures+id;if(t->base)return 1;t->priority=2;return t->fatal?-1:0;}

int town_texture_identity(unsigned id,unsigned width,unsigned height,unsigned hash){return id<texture_count&&tex_field(textures+id,0)==width&&tex_field(textures+id,1)==height&&tex_field(textures+id,6)==hash;}

int town_texture_feature(int bit){return (texture_features&bit)!=0;}
int town_texture_memory_available(unsigned bytes){return tex_memory_available(bytes);}
int animaltown_textures_geometryscope(int enabled){return town_geometry_scope?town_geometry_scope(enabled):enabled;}

int animaltown_textures_poseregister(int actor,int model,int bytes){return model>=0&&model<TEX_MODEL_CAP&&town_pose_register_actor?town_pose_register_actor(actor,texture_models[model],(unsigned)bytes):0;}
int animaltown_textures_poseactor(int actor){return town_pose_actor?town_pose_actor(actor):0;}
int animaltown_textures_posebatch(int begin){return town_pose_batch?town_pose_batch(begin):0;}
