/* Immutable geometry in ARAM; fixed MEM1 working set for GX. No movement-time
 * vertex construction, mesh allocation or index generation. DMA preserves the
 * original world UVs, receiver alpha and independent shadow-page draws. */
#include "dnd_aram.h"
#include <errno.h>
#define TA_SECTIONS 4096
#define TA_PARTS 16384
#define TA_STAGE 65536
#define TA_KINDS 109
#define TA_PAGE_PARTS (TA_KINDS*2)
#define TA_BYTES (8*1024*1024)
#define TA_CACHE (1024*1024)
#define TA_BLOCK 1024
typedef struct {int offset,vertices,indices,kind,format;uint32_t color;int uniform,cache,users;} TaPart;
static int ta_meta[TA_PARTS/256],ta_first[TA_SECTIONS+1],ta_triangles[TA_SECTIONS];
static unsigned char ta_used[TA_CACHE/TA_BLOCK];
static int ta_store,ta_cache,ta_stage,ta_meta_count,ta_bytes,ta_tail,ta_parts,ta_sections,ta_completed,ta_live,ta_peak;
static unsigned ta_bindings,ta_reads;static bool ta_sealed;
/* Only cold-resume binds remain pending across calls. The broker owns the
 * cache/ARAM transfer holds; a separate view hold prevents handle reuse. */
static int ta_request,ta_pending_view,ta_pending_entry=-1,ta_pending_block,ta_pending_blocks;
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
    int request=dnd_aram_request_native(ta_store,offset,buffer,destination,count,write,0);
    if(request<1)return -1;
    int rc;do{rc=dnd_aram_request_poll(request);}while(rc==0);
    return rc==1?1:-1;
}
static void ta_clear_pending(bool keep_blocks){
    if(!keep_blocks&&ta_pending_blocks)memset(ta_used+ta_pending_block,0,(size_t)ta_pending_blocks);
    int token=ta_pending_view,index=(token&1023)-1;
    ta_request=ta_pending_view=ta_pending_block=ta_pending_blocks=0;ta_pending_entry=-1;
    if(token>0&&index>=0&&index<SLOTS){
        Resource *r=&slots[index];
        if(r->kind==MESH&&r->generation==(token>>10)&&r->holds>0){
            if(--r->holds==0&&r->pending&&!dnd_device_active())release(r);
        }
    }
}
int animaltown_terrain_cancel_bind(void){
    if(dnd_device_active()||dnd_raw_depth())return -1;
    if(!ta_request)return 1;
    (void)dnd_aram_request_cancel(ta_request);
    int rc=dnd_aram_request_poll(ta_request);if(rc==0)return 0;
    ta_clear_pending(false);return 1;
}
/* Unlike reset, suspension preserves the immutable archive and its metadata.
 * Callers first drain pending binds and remove every published mesh alias. */
int animaltown_terrain_suspend(void){
    if(dnd_device_active()||dnd_raw_depth()||!ta_sealed)return -1;
    int drained=animaltown_terrain_cancel_bind();if(drained!=1)return drained;
    if(!ta_cache)return 1;
    for(int i=0;i<SLOTS;i++)if(slots[i].terrain_parent==ta_cache)return -1;
    Resource *cache=find(ta_cache,BUFFER);if(!cache||cache->holds)return -1;
    if(dnd_draw_release(ta_cache))return -1;
    ta_cache=0;ta_bytes-=TA_CACHE;ta_live=0;memset(ta_used,0,sizeof(ta_used));return 1;
}
int animaltown_terrain_resume(void){
    if(dnd_device_active()||dnd_raw_depth()||!ta_sealed)return -1;
    if(ta_cache)return 1;
    if(TA_CACHE>BUDGET-total_bytes)return -2;
    /* The cache has no readable bytes until a completed bind publishes them.
     * Reserve uninitialized native storage rather than zeroing 1 MiB in one
     * frame. A tiny normal resource establishes the token/ownership fields. */
    int token=create(BUFFER,1,32);if(!token)return -2;
    Resource *cache=find(token,BUFFER);void *memory=malloc(TA_CACHE+31u);
    if(!memory){release(cache);return -2;}
    free(cache->allocation);cache->allocation=memory;
    cache->data=(unsigned char*)(((uintptr_t)memory+31u)&~(uintptr_t)31u);
    cache->bytes=TA_CACHE;total_bytes+=TA_CACHE-1;ta_bytes+=TA_CACHE;ta_cache=token;
    return 1;
}
int animaltown_terrain_reset(int count){
    if(dnd_device_active()||dnd_raw_depth()||count<0||count>TA_SECTIONS||ta_request)return -1;
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
    if(dnd_device_active()||dnd_raw_depth()||ta_sealed||section<0||section!=ta_completed||section>=ta_sections||kind<0||kind>=TA_KINDS||!source||source->geometry||source->terrain_parent||source->commands||source->format<1||source->format>2||used<0||ta_parts>=TA_PARTS)return -1;
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
/* Cold-load only. Coalesce within a shadow page and an identical native
 * material/format/color class. Payloads retain every vertex attribute; indices
 * are rebased, never expanded. Oversized pages keep their original parts. */
static int ta_same(const TaPart *a,const TaPart *b){
    return a->kind==b->kind&&a->format==b->format&&(a->indices!=0)==(b->indices!=0)&&a->uniform==b->uniform&&(!a->uniform||a->color==b->color);
}
static int ta_coalesce(int section){
    int first=ta_first[section],count=ta_parts-first;if(count<2||count>TA_PAGE_PARTS)return 1;
    int groups[TA_PAGE_PARTS],group_count=0,start=ta_part(first)->offset,bytes=ta_tail-start;
    if(bytes>TA_STAGE)return 1;
    Resource *stage=find(ta_stage,BUFFER),*cache=find(ta_cache,BUFFER);if(!stage||!cache)return -1;
    /* No views exist until sealing: borrow the idle MEM1 cache for output and
     * descriptor scratch, retaining the original 64 KiB staging/MEM1 budget. */
    _Static_assert(TA_STAGE+TA_PAGE_PARTS*sizeof(TaPart)<=TA_CACHE,"Cold coalescer cache scratch");
    TaPart *merged=(TaPart*)(cache->data+TA_STAGE);
    for(int i=0;i<count;i++){
        const TaPart *original=ta_part(first+i);int group=0;
        while(group<group_count&&!ta_same(original,&merged[group]))group++;
        if(group==group_count){merged[group]=*original;merged[group].vertices=merged[group].indices=0;group_count++;}
        groups[i]=group;merged[group].vertices+=original->vertices;merged[group].indices+=original->indices;
        if(merged[group].vertices>65535||merged[group].indices>65535)return 1;
    }
    if(group_count==count)return 1;
    if(ta_dma(start,ta_stage,0,bytes,0)!=1)return -3;
    unsigned char *src=(unsigned char*)stage->data,*dst=(unsigned char*)cache->data;int end=0;
    for(int group=0;group<group_count;group++){
        TaPart *p=&merged[group];int size=ta_size(p);if(size>TA_STAGE-end)return -2;
        memset(dst+end,0,(size_t)size);int vertex=0,index=0;
        for(int i=0;i<count;i++)if(groups[i]==group){
            const TaPart *q=ta_part(first+i);const unsigned char *data=src+q->offset-start;
            memcpy(dst+end+(size_t)vertex*sizeof(DndDrawVertex),data,(size_t)q->vertices*sizeof(DndDrawVertex));
            const uint16_t *in=(const uint16_t*)(data+(size_t)q->vertices*sizeof(DndDrawVertex));
            uint16_t *out=(uint16_t*)(dst+end+(size_t)p->vertices*sizeof(DndDrawVertex));
            for(int j=0;j<q->indices;j++)out[index++]=(uint16_t)(in[j]+vertex);
            vertex+=q->vertices;
        }
        p->offset=start+end;end+=size;
    }
    if(end>bytes)return -2; /* Padding can shrink, never grow. */
    if(dnd_buffer_hold(ta_cache))return -1;
    int rc=ta_dma(start,ta_cache,0,end,1);(void)dnd_buffer_unhold(ta_cache);if(rc!=1)return -3;
    for(int i=0;i<group_count;i++)*ta_part(first+i)=merged[i];
    ta_tail=start+end;ta_parts=first+group_count;return 1;
}
int animaltown_terrain_complete(int section){
    if(dnd_device_active()||dnd_raw_depth()||ta_sealed||section<0||section!=ta_completed||section>=ta_sections)return -1;
    int rc=ta_coalesce(section);if(rc!=1)return rc;
    ta_first[++ta_completed]=ta_parts;
    if(ta_completed==ta_sections){ta_sealed=true;(void)dnd_buffer_unhold(ta_stage);(void)dnd_draw_release(ta_stage);ta_stage=0;ta_bytes-=TA_STAGE;}
    return 1;
}
int animaltown_terrain_info(int section,int kind){
    if(!ta_sealed||section<0||section>=ta_sections)return -1;
    if(kind==3){
        int count=0;for(int i=ta_first[section];i<ta_first[section+1];i++){
            const TaPart *part=ta_part(i);if(part->kind==108)count+=(part->indices?part->indices:part->vertices)/3;
        }
        return count;
    }
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
static int ta_bind_view(Resource *r,TaPart *p,int index){
    Resource *cache=find(ta_cache,BUFFER);if(!cache||p->cache<0)return -1;
    if(r->terrain_entry==index+1)return 1;
    int previous=r->terrain_parent;
    if(previous!=ta_cache&&dnd_buffer_hold(ta_cache))return -1;
    terrain_detach(r);p->users++;
    r->terrain_entry=index+1;r->terrain_parent=ta_cache;r->data=cache->data+p->cache*TA_BLOCK;r->vertices=p->vertices;r->indices=p->indices;r->format=p->format;
    r->uniform_color=p->uniform!=0;r->mesh_color=p->color;r->dynamic=0;
    if(previous&&previous!=ta_cache)(void)dnd_buffer_unhold(previous);
    ta_bindings++;dnd_draw_backend_mesh_changed();return 1;
}
/* Existing save-time cache API shares the same private owner/lifetime guards. */
int animaltown_terrain_cache(int enabled){
    if(enabled!=0&&enabled!=1)return -1;
    return enabled?animaltown_terrain_resume():animaltown_terrain_suspend();
}
static int ta_free_block(int blocks){
    int run=0;
    for(int i=0;i<TA_CACHE/TA_BLOCK;i++){if(ta_used[i])run=0;else if(++run==blocks)return i+1-blocks;}
    return -1;
}
int animaltown_terrain_bind(int token,int index){
    Resource *r=find(token,MESH);TaPart *p=ta_part(index);Resource *cache=find(ta_cache,BUFFER);
    if(dnd_device_active()||dnd_raw_depth()||!ta_sealed||ta_request||!r||r->holds||r->geometry||r->commands||r->allocation||r->bytes!=1||!p||!cache)return -1;
    if(r->terrain_entry==index+1)return 1;
    if(p->cache<0){
        int blocks=(ta_size(p)+TA_BLOCK-1)/TA_BLOCK,first=ta_free_block(blocks);if(first<0)return -2;
        int rc=ta_dma(p->offset,ta_cache,first*TA_BLOCK,ta_size(p),0);if(rc!=1)return -3;
        memset(ta_used+first,1,(size_t)blocks);p->cache=first;ta_live+=blocks*TA_BLOCK;if(ta_live>ta_peak)ta_peak=ta_live;ta_reads++;
    }
    return ta_bind_view(r,p,index);
}
/* Polling publication for resume/prefetch: no wait loop and no partial alias.
 * An incomplete request leaves its view's previous geometry unchanged. */
int animaltown_terrain_bind_step(int token,int index){
    Resource *r=find(token,MESH);TaPart *p=ta_part(index);Resource *cache=find(ta_cache,BUFFER);
    if(dnd_device_active()||dnd_raw_depth()||!ta_sealed)return -1;
    if(ta_request){
        if(token!=ta_pending_view||index!=ta_pending_entry)return 0;
        int rc=dnd_aram_request_poll(ta_request);if(rc==0)return 0;
        /* A terminal poll releases the DMA owner holds. Revalidate tokens
         * before publishing; an externally retired buffer may now be gone. */
        r=find(token,MESH);p=ta_part(index);cache=find(ta_cache,BUFFER);
        if(rc<0||!r||!p||!cache){ta_clear_pending(false);return -3;}
        int block=ta_pending_block,blocks=ta_pending_blocks;
        ta_clear_pending(true);
        r=find(token,MESH);if(!r){memset(ta_used+block,0,(size_t)blocks);return -1;}
        p->cache=block;ta_live+=blocks*TA_BLOCK;if(ta_live>ta_peak)ta_peak=ta_live;ta_reads++;
        return ta_bind_view(r,p,index);
    }
    if(!r||r->holds||r->geometry||r->commands||r->allocation||r->bytes!=1||!p||!cache)return -1;
    if(r->terrain_entry==index+1)return 1;
    if(p->cache>=0)return ta_bind_view(r,p,index);
    int blocks=(ta_size(p)+TA_BLOCK-1)/TA_BLOCK,first=ta_free_block(blocks);if(first<0)return -2;
    int request=dnd_aram_request_native(ta_store,p->offset,ta_cache,first*TA_BLOCK,ta_size(p),0,0);
    if(request==-EAGAIN)return 0;
    if(request<1)return -3;
    memset(ta_used+first,1,(size_t)blocks);r->holds++;
    ta_request=request;ta_pending_view=token;ta_pending_entry=index;ta_pending_block=first;ta_pending_blocks=blocks;
    /* Start the first broker chunk now rather than waiting a whole frame.
     * This reentry can only take the pending branch; it never waits on DMA. */
    return animaltown_terrain_bind_step(token,index);
}
int animaltown_terrain_stat(int kind){return kind==0?ta_bytes:kind==1?ta_parts:kind==2?ta_meta_count:kind==3?ta_completed:kind==4?(int)ta_bindings:kind==5?ta_tail:kind==6?(int)ta_reads:kind==7?ta_peak:kind==8?(ta_cache?TA_CACHE:0):kind==9?(ta_request?1:0):-1;}
