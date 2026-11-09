/* Immutable geometry in ARAM; fixed MEM1 working set for GX. No movement-time
 * vertex construction, mesh allocation or index generation. DMA preserves the
 * original world UVs, receiver alpha and independent shadow-page draws. */
#include "dnd_aram.h"
#define TA_SECTIONS 4096
#define TA_PARTS 16384
#define TA_STAGE 65536
#define TA_BYTES (8*1024*1024)
#define TA_CACHE (1024*1024)
#define TA_BLOCK 1024
typedef struct {int offset,vertices,indices,kind,format;uint32_t color;int uniform,cache,users;} TaPart;
static int ta_meta[TA_PARTS/256],ta_first[TA_SECTIONS+1],ta_triangles[TA_SECTIONS];
static unsigned char ta_used[TA_CACHE/TA_BLOCK];
static int ta_store,ta_cache,ta_stage,ta_meta_count,ta_bytes,ta_tail,ta_parts,ta_sections,ta_completed,ta_live,ta_peak;
static unsigned ta_bindings,ta_reads;static bool ta_sealed;
static TaPart *ta_part(int index){
    if(index<0||index>=ta_parts)return NULL;
    Resource *r=find(ta_meta[index/256],BUFFER);return r?(TaPart*)r->data+index%256:NULL;
}
static int ta_size(const TaPart *p){return (int)(((size_t)p->vertices*sizeof(DndDrawVertex)+(size_t)p->indices*2+31u)&~31u);}
static void terrain_detach(Resource *r){
    TaPart *p=ta_part(r->terrain_entry-1);r->terrain_entry=0;
    if(!p||--p->users>0)return;
    int blocks=(ta_size(p)+TA_BLOCK-1)/TA_BLOCK;
    memset(ta_used+p->cache,0,(size_t)blocks);ta_live-=blocks*TA_BLOCK;p->cache=-1;
}
static int ta_buffer(int bytes){int token=create(BUFFER,bytes,32);if(token)ta_bytes+=bytes;return token;}
static int ta_dma(int offset,int buffer,int destination,int count,int write){
    int rc=dnd_aram_begin_native(ta_store,offset,buffer,destination,count,write);if(rc)return -1;
    do{rc=dnd_aram_poll_native();}while(rc==0);
    return rc==1?1:-1;
}
int animaltown_terrain_reset(int count){
    if(dnd_device_active()||dnd_raw_depth()||count<0||count>TA_SECTIONS)return -1;
    for(int i=0;i<SLOTS;i++)if(slots[i].terrain_parent)return -1;
    if(ta_stage){(void)dnd_buffer_unhold(ta_stage);if(dnd_draw_release(ta_stage))return -1;}
    if(ta_cache&&dnd_draw_release(ta_cache))return -1;
    for(int i=0;i<ta_meta_count;i++)if(dnd_draw_release(ta_meta[i]))return -1;
    if(ta_store){if(dnd_aram_release(ta_store)||dnd_aram_dispose(ta_store))return -1;}
    memset(ta_first,0,sizeof(ta_first));memset(ta_triangles,0,sizeof(ta_triangles));memset(ta_used,0,sizeof(ta_used));
    ta_store=ta_stage=ta_cache=ta_meta_count=ta_bytes=ta_tail=ta_parts=ta_completed=ta_live=ta_peak=0;
    ta_bindings=ta_reads=0;ta_sections=count;ta_sealed=false;
    if(!count)return 1;
    ta_store=dnd_aram_allocate(TA_BYTES);if(!ta_store)return -2;
    if(dnd_aram_retain(ta_store)){(void)dnd_aram_dispose(ta_store);ta_store=0;return -2;}
    ta_cache=ta_buffer(TA_CACHE);ta_stage=ta_buffer(TA_STAGE);
    if(!ta_cache||!ta_stage||dnd_buffer_hold(ta_stage)){(void)animaltown_terrain_reset(0);return -2;}
    return 1;
}
int animaltown_terrain_append(int section,int kind,int token,int used){
    Resource *source=find(token,MESH);
    if(dnd_device_active()||dnd_raw_depth()||ta_sealed||section<0||section!=ta_completed||section>=ta_sections||kind<0||kind>=108||!source||source->geometry||source->terrain_parent||source->commands||source->format<1||source->format>2||used<0||ta_parts>=TA_PARTS)return -1;
    int n=source->indices?source->vertices:used?used:source->vertices,m=source->indices;
    if(n<1||n>source->vertices||(!m&&n%3))return -1;
    size_t bytes=(size_t)n*sizeof(DndDrawVertex)+(size_t)m*2;
    if(bytes>TA_STAGE-31)return -2;
    int padded=(int)((bytes+31u)&~31u);if(padded>TA_BYTES-ta_tail)return -2;
    if(ta_parts/256>=ta_meta_count){int meta=ta_buffer(256*(int)sizeof(TaPart));if(!meta)return -2;ta_meta[ta_meta_count++]=meta;}
    Resource *stage=find(ta_stage,BUFFER);if(!stage)return -1;
    memcpy(stage->data,source->data,(size_t)n*sizeof(DndDrawVertex));
    if(m)memcpy(stage->data+(size_t)n*sizeof(DndDrawVertex),source->data+(size_t)source->vertices*sizeof(DndDrawVertex),(size_t)m*2);
    memset(stage->data+bytes,0,(size_t)padded-bytes);
    const DndDrawVertex *v=(const DndDrawVertex*)stage->data;uint32_t color=v[0].color;int uniform=1;
    for(int i=1;i<n;i++)if(v[i].color!=color){uniform=0;break;}
    if(ta_dma(ta_tail,ta_stage,0,padded,1)!=1)return -3;
    TaPart part={ta_tail,n,m,kind,source->format,color,uniform,-1,0};
    TaPart *entry=(TaPart*)find(ta_meta[ta_parts/256],BUFFER)->data+ta_parts%256;*entry=part;
    ta_parts++;ta_tail+=padded;ta_triangles[section]+=(m?m:n)/3;return ta_parts;
}
int animaltown_terrain_complete(int section){
    if(dnd_device_active()||dnd_raw_depth()||ta_sealed||section<0||section!=ta_completed||section>=ta_sections)return -1;
    ta_first[++ta_completed]=ta_parts;
    if(ta_completed==ta_sections){ta_sealed=true;(void)dnd_buffer_unhold(ta_stage);(void)dnd_draw_release(ta_stage);ta_stage=0;ta_bytes-=TA_STAGE;}
    return 1;
}
int animaltown_terrain_info(int section,int kind){
    if(!ta_sealed||section<0||section>=ta_sections)return -1;
    return kind==0?ta_first[section]:kind==1?ta_first[section+1]-ta_first[section]:kind==2?ta_triangles[section]:-1;
}
int animaltown_terrain_kind(int entry){TaPart *p=ta_part(entry);return ta_sealed&&p?p->kind:-1;}
int animaltown_terrain_view(void){
    if(dnd_device_active()||dnd_raw_depth())return 0;
    int token=create(MESH,1,32);Resource *r=find(token,MESH);if(!r)return 0;
    free(r->allocation);r->allocation=NULL;r->data=NULL;return token;
}
int animaltown_terrain_unbind(int token){
    Resource *r=find(token,MESH);if(dnd_device_active()||dnd_raw_depth()||!r||r->allocation||r->bytes!=1||r->holds||r->geometry||r->commands)return -1;
    int previous=r->terrain_parent;terrain_detach(r);r->terrain_parent=0;r->data=NULL;r->vertices=r->indices=0;
    if(previous&&dnd_buffer_unhold(previous))return -1;
    dnd_draw_backend_mesh_changed();return 1;
}
int animaltown_terrain_bind(int token,int index){
    Resource *r=find(token,MESH);TaPart *p=ta_part(index);Resource *cache=find(ta_cache,BUFFER);
    if(dnd_device_active()||dnd_raw_depth()||!ta_sealed||!r||r->holds||r->geometry||r->commands||r->allocation||r->bytes!=1||!p||!cache)return -1;
    if(r->terrain_entry==index+1)return 1;
    if(p->cache<0){
        int blocks=(ta_size(p)+TA_BLOCK-1)/TA_BLOCK,run=0,first=-1;
        for(int i=0;i<TA_CACHE/TA_BLOCK;i++){if(ta_used[i])run=0;else if(++run==blocks){first=i+1-blocks;break;}}
        if(first<0)return -2;
        if(dnd_buffer_hold(ta_cache))return -1;
        int rc=ta_dma(p->offset,ta_cache,first*TA_BLOCK,ta_size(p),0);(void)dnd_buffer_unhold(ta_cache);if(rc!=1)return -3;
        memset(ta_used+first,1,(size_t)blocks);p->cache=first;ta_live+=blocks*TA_BLOCK;if(ta_live>ta_peak)ta_peak=ta_live;ta_reads++;
    }
    int previous=r->terrain_parent;
    if(!previous&&dnd_buffer_hold(ta_cache))return -1;
    terrain_detach(r);p->users++;
    r->terrain_entry=index+1;r->terrain_parent=ta_cache;r->data=cache->data+p->cache*TA_BLOCK;r->vertices=p->vertices;r->indices=p->indices;r->format=p->format;
    r->uniform_color=p->uniform!=0;r->mesh_color=p->color;r->dynamic=0;
    ta_bindings++;dnd_draw_backend_mesh_changed();return 1;
}
int animaltown_terrain_stat(int kind){return kind==0?ta_bytes:kind==1?ta_parts:kind==2?ta_meta_count:kind==3?ta_completed:kind==4?(int)ta_bindings:kind==5?ta_tail:kind==6?(int)ta_reads:kind==7?ta_peak:-1;}
