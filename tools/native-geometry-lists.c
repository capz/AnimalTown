#include "native-geometry-lists.h"
#include "dnd_model_internal.h"
#include "dnd_draw.h"
#include <stdlib.h>
#include <string.h>
#ifdef DND_GAMECUBE_STORAGE
#include <gccore.h>
#include <malloc.h>
#endif
#define LIST_CAP 64
#define LIST_BUDGET (128u*1024u)
typedef struct {int kind,token;unsigned mesh,generation,mask,count,stamp;} ListKey;
typedef struct {ListKey key;unsigned char *data;unsigned bytes,primitives,position,out,batch_end;int pending;} ListEntry;
static ListEntry lists[LIST_CAP];
static ListKey requests[LIST_CAP];static unsigned requests_count,list_frame,list_bytes,list_hits,list_misses,list_builds,list_fallbacks;
static int list_scope,mesh_owner,model_owner,model_eligible;static unsigned model_mesh,model_generation;
extern int town_texture_feature(int);
extern int town_texture_memory_available(unsigned);
extern int town_geometry_mesh_source(int,const void**,unsigned*,unsigned*,int*) __attribute__((weak));
static int list_key_equal(const ListKey *a,const ListKey *b){return a->kind==b->kind&&a->token==b->token&&a->mesh==b->mesh&&a->generation==b->generation&&a->mask==b->mask&&a->count==b->count;}
static unsigned list_attributes(unsigned mask){unsigned n=1;for(int i=0;i<4;i++)n+=(mask>>i)&1u;return n;}
static int list_source(const ListKey *key,const void **indices,unsigned *count,unsigned *vertices,int *width){
 if(!key->kind){if(!town_geometry_mesh_source)return 0;return town_geometry_mesh_source(key->token,indices,count,vertices,width);}
 DndModel *m=dnd_model_find(key->token,false);if(!m||!m->ready||m->hash!=key->generation||key->mesh>=m->meshes||m->mesh[key->mesh].skin>=0||m->clips)return 0;
 DndMesh *mesh=m->mesh+key->mesh;*indices=mesh->index;*count=mesh->indices;*vertices=mesh->vertices;*width=32;return 1;
}
static unsigned list_index(const void *p,unsigned i,int width){
 if(!p)return i;
 if(width==16)return ((const uint16_t*)p)[i];
 const unsigned char *b=(const unsigned char*)p+i*4;return (unsigned)b[0]<<24|(unsigned)b[1]<<16|(unsigned)b[2]<<8|b[3];
}
static void list_drop(ListEntry *entry){if(entry->data){list_bytes-=entry->bytes;free(entry->data);}memset(entry,0,sizeof(*entry));}
void town_geometry_begin(unsigned frame){list_frame=frame;mesh_owner=0;model_owner=0;list_scope=0;}
int town_geometry_scope(int enabled){if(enabled<0||enabled>1)return -1;list_scope=enabled;if(!enabled)mesh_owner=0;return enabled;}
void town_geometry_mesh_owner(int token){mesh_owner=token;}
void town_geometry_model_owner(int token,unsigned mesh,unsigned generation,int eligible){model_owner=token;model_mesh=mesh;model_generation=generation;model_eligible=eligible;}
static int list_get(ListKey key,TownGeometryList *view){
 if(!town_texture_feature(32)||!key.token||!key.count||key.count%3||key.mask>15)return 0;
 for(int i=0;i<LIST_CAP;i++)if(lists[i].data&&list_key_equal(&lists[i].key,&key)){
  lists[i].key.stamp=list_frame;if(lists[i].pending){list_fallbacks++;return 0;}*view=(TownGeometryList){lists[i].data,lists[i].bytes,lists[i].primitives};list_hits++;return 1;
 }
 list_misses++;for(unsigned i=0;i<requests_count;i++)if(list_key_equal(requests+i,&key))return 0;
 if(requests_count<LIST_CAP){key.stamp=list_frame;requests[requests_count++]=key;}else list_fallbacks++;return 0;
}
int town_geometry_mesh_list(unsigned mask,unsigned count,TownGeometryList *view){if(!list_scope)return 0;return list_get((ListKey){.kind=0,.token=mesh_owner,.mask=mask,.count=count},view);}
int town_geometry_model_list(unsigned mask,unsigned count,TownGeometryList *view){if(!model_eligible)return 0;return list_get((ListKey){.kind=1,.token=model_owner,.mesh=model_mesh,.generation=model_generation,.mask=mask,.count=count},view);}
void town_geometry_forget(int kind,int token){
 for(int i=0;i<LIST_CAP;i++)if(lists[i].data&&lists[i].key.kind==kind&&lists[i].key.token==token)list_drop(lists+i);
 for(unsigned i=0;i<requests_count;)if(requests[i].kind==kind&&requests[i].token==token)requests[i]=requests[--requests_count];else i++;
}
void town_geometry_clear(void){for(int i=0;i<LIST_CAP;i++)list_drop(lists+i);requests_count=0;}
int town_geometry_pump(void){
 if(dnd_device_active()||!town_texture_feature(32))return 0;
 ListEntry *entry=NULL;for(int i=0;i<LIST_CAP;i++)if(lists[i].pending){entry=lists+i;break;}
 if(!entry){
  if(!requests_count)return 0;
  ListKey key=requests[0];memmove(requests,requests+1,(--requests_count)*sizeof(*requests));
  const void *indices;unsigned count,vertices;int width;
  if(!list_source(&key,&indices,&count,&vertices,&width)||count!=key.count||vertices>65536){list_fallbacks++;return 1;}
  unsigned primitive_count=(count+65534u)/65535u,attributes=list_attributes(key.mask);
  if(count>LIST_BUDGET/(attributes*2u)){list_fallbacks++;return 1;}
  unsigned bytes=(primitive_count*3+count*attributes*2+31u)&~31u;if(bytes>LIST_BUDGET||!town_texture_memory_available(bytes)){list_fallbacks++;return 1;}
  while(list_bytes+bytes>LIST_BUDGET){int oldest=-1;for(int i=0;i<LIST_CAP;i++)if(lists[i].data&&!lists[i].pending&&(oldest<0||lists[i].key.stamp<lists[oldest].key.stamp))oldest=i;if(oldest<0){list_fallbacks++;return 1;}list_drop(lists+oldest);}
  for(int i=0;i<LIST_CAP;i++)if(!lists[i].data){entry=lists+i;break;}
  if(!entry){int oldest=0;for(int i=1;i<LIST_CAP;i++)if(lists[i].key.stamp<lists[oldest].key.stamp)oldest=i;list_drop(lists+oldest);entry=lists+oldest;}
#ifdef DND_GAMECUBE_STORAGE
  unsigned char *data=memalign(32,bytes);
#else
  unsigned char *data=aligned_alloc(32,bytes);
#endif
  if(!data){list_fallbacks++;return 1;}*entry=(ListEntry){.key=key,.data=data,.bytes=bytes,.primitives=primitive_count,.pending=1};list_bytes+=bytes;
 }
 const void *indices;unsigned count,vertices;int width;
 if(!list_source(&entry->key,&indices,&count,&vertices,&width)||count!=entry->key.count){list_drop(entry);return 1;}
 unsigned attributes=list_attributes(entry->key.mask),limit=entry->position+512;if(limit>count)limit=count;
 while(entry->position<limit){
  if(entry->position==entry->batch_end){unsigned n=count-entry->position;if(n>65535)n=65535;entry->data[entry->out++]=0x90;entry->data[entry->out++]=(unsigned char)(n>>8);entry->data[entry->out++]=(unsigned char)n;entry->batch_end+=n;}
  unsigned index=list_index(indices,entry->position,width);if(index>=vertices||index>65535){list_drop(entry);list_fallbacks++;return 1;}
  for(unsigned a=0;a<attributes;a++){entry->data[entry->out++]=(unsigned char)(index>>8);entry->data[entry->out++]=(unsigned char)index;}entry->position++;
 }
 if(entry->position==count){memset(entry->data+entry->out,0,entry->bytes-entry->out);entry->pending=0;list_builds++;
#ifdef DND_GAMECUBE_STORAGE
 DCFlushRange(entry->data,entry->bytes);
#endif
 }
 return 1;
}
int town_geometry_stats(int kind){return kind==0?(int)list_bytes:kind==1?(int)list_hits:kind==2?(int)list_misses:kind==3?(int)list_builds:kind==4?(int)list_fallbacks:kind==5?(int)requests_count:-1;}
