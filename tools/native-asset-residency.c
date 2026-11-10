/* One completed, <=32 KiB DMA quantum between frames. Terrain may immediately
 * use the same ARAM channel afterward. Immutable models are backed and verified
 * before eviction; verified immutable bytes reuse validated native metadata. */
#include "dnd_model_internal.h"
#include "dnd_aram.h"
#include "dnd_draw.h"
#include "dnd_storage.h"
#include "dnd_timing.h"
#include <limits.h>
#ifdef DND_GAMECUBE_STORAGE
#include <gccore.h>
#endif
#include <string.h>
#define ASSETS 34
#define QUANTUM 32768
#define FNV 2166136261u
extern void animaltown_shadows_model_reset(void);
extern int animaltown_assets_stage_begin(DndModel*);
extern int animaltown_assets_texture_hash(DndModel*,unsigned,unsigned*);
extern int animaltown_assets_texture_begin(DndModel*,unsigned,unsigned,unsigned);
extern int animaltown_assets_texture_step(DndModel*,unsigned,unsigned,unsigned,unsigned,int);
extern unsigned animaltown_assets_texture_drop(DndModel*,unsigned);
extern void animaltown_assets_abort(DndModel*);
static void invalidate_model_cache(void){dnd_draw_backend_invalidate_cache();animaltown_shadows_model_reset();}
typedef struct {
    int token,store,hold,age,phase,restoring,file,disabled,fatal;
    DndModel *model;
    unsigned length,offset,hash;
    int metadata,work,texture_kind;
    unsigned *links,texture_base,block_base,texture,cursor,search,instance;
    char path[192];
} Asset;
static Asset assets[ASSETS];
static int stage,last,active_restore=-1;
static unsigned evictions,restores,transferred,recoveries,failures,max_restore_us;
static unsigned crc_table[256];static int crc_ready;
static unsigned block_crc(const unsigned char *p,unsigned n){
    if(!crc_ready){for(unsigned i=0;i<256;i++){unsigned c=i;for(int bit=0;bit<8;bit++)c=(c>>1)^((c&1)?0xedb88320u:0);crc_table[i]=c;}crc_ready=1;}
    unsigned c=~0u;while(n--)c=(c>>8)^crc_table[(c^*p++)&255u];return ~c;
}
static unsigned hash_bytes(unsigned h,const unsigned char *p,unsigned n){while(n--)h=(h^*p++)*16777619u;return h;}
static void drop_store(Asset *a){if(a->store){(void)dnd_aram_release(a->store);(void)dnd_aram_dispose(a->store);a->store=0;}}
static int saved_offset(DndModel *m,const void *pointer,unsigned *out){
    uintptr_t p=(uintptr_t)pointer,base=(uintptr_t)m->package;
    if(p<base||p-base>=m->length)return 0;
    *out=(unsigned)(p-base);return 1;
}
static int capture_metadata(Asset *a,DndModel *m){
    a->texture_base=2*m->meshes+m->skins+m->clips;a->block_base=a->texture_base+2*m->textures;
    unsigned words=a->block_base+(m->length+QUANTUM-1)/QUANTUM;
    a->metadata=dnd_buffer_create((int)(words*4),32);if(!a->metadata)return 0;
    if(dnd_buffer_hold(a->metadata)){(void)dnd_draw_release(a->metadata);a->metadata=0;return 0;}
    a->links=(unsigned*)(uintptr_t)dnd_buffer_address(a->metadata,0);if(!a->links)return 0;
    unsigned n=0;
    for(unsigned i=0;i<m->meshes;i++)if(!saved_offset(m,m->mesh[i].data,a->links+n++)||!saved_offset(m,m->mesh[i].index,a->links+n++))return 0;
    for(unsigned i=0;i<m->skins;i++)if(!saved_offset(m,m->skin[i].data,a->links+n++))return 0;
    for(unsigned i=0;i<m->clips;i++)if(!saved_offset(m,m->clip[i].tracks,a->links+n++))return 0;
    unsigned start=dnd_model_u32(m->package+16+3*16+4),bytes=dnd_model_u32(m->package+16+3*16+8);
    if(start>m->length||bytes>m->length-start||bytes<4||dnd_model_u32(m->package+start)!=m->textures)return 0;
    unsigned at=start+4;
    for(unsigned i=0;i<m->textures;i++){
        if(at>start+bytes||start+bytes-at<28||dnd_model_u32(m->package+at+24)!=m->texture[i].size)return 0;
        at+=28;if(m->texture[i].size>start+bytes-at)return 0;
        a->links[n++]=at;if(!animaltown_assets_texture_hash(m,i,a->links+n++))return 0;at+=m->texture[i].size;
    }
    return at==start+bytes;
}
static void drop_metadata(Asset *a){if(a->metadata){(void)dnd_buffer_unhold(a->metadata);(void)dnd_draw_release(a->metadata);a->metadata=0;a->links=NULL;}}
static void rebase_metadata(Asset *a){
    DndModel *m=a->model;unsigned n=0;
    for(unsigned i=0;i<m->meshes;i++){m->mesh[i].data=m->staged+a->links[n++];m->mesh[i].index=m->staged+a->links[n++];}
    for(unsigned i=0;i<m->skins;i++)m->skin[i].data=m->staged+a->links[n++];
    for(unsigned i=0;i<m->clips;i++)m->clip[i].tracks=m->staged+a->links[n++];
}
int animaltown_assets_capture(int slot,int expected,int hold,DndString *path){
    if(dnd_device_active()||slot<0||slot>=ASSETS||assets[slot].token||hold< -1)return 0;
    /* Startup has exactly one unclaimed completed load at a time. Reject an
     * ambiguous registry rather than infer private managed object layouts. */
    DndModel *model=NULL;
    for(int i=0;i<DND_MODEL_SLOTS;i++)if(dnd_models[i]){
        int claimed=0;for(int j=0;j<ASSETS;j++)claimed|=assets[j].token==dnd_models[i]->token;
        if(!claimed){if(model||!dnd_models[i]->ready||dnd_models[i]->closed)return 0;model=dnd_models[i];}
    }
    Asset next={0};
    if(!model||model->bytes!=(unsigned)expected||model->refs==INT_MAX||!dnd_fs_path_utf8_native(path,next.path,sizeof(next.path))||strncmp(next.path,"dvd:/",5))return 0;
    if(!stage){int next_stage=dnd_buffer_create(QUANTUM,32);if(!next_stage)return 0;if(dnd_buffer_hold(next_stage)){(void)dnd_draw_release(next_stage);return 0;}stage=next_stage;}
    next.model=model;next.token=model->token;next.length=model->length;next.hold=hold;next.hash=FNV;
    if(hold>=0&&!capture_metadata(&next,model)){drop_metadata(&next);return 0;}
    model->refs++;assets[slot]=next;return 1;
}
int animaltown_assets_forget(int slot){
    if(dnd_device_active()||slot<0||slot>=ASSETS)return 0;
    Asset *a=&assets[slot];if(!a->token)return 1;
    DndModel *m=a->model;if(!m)return 0;
    if(a->file)dnd_fs_close(a->file);
    if(a->restoring)animaltown_assets_abort(m);
    if(active_restore==slot)active_restore=-1;
    drop_store(a);drop_metadata(a);memset(a,0,sizeof(*a));dnd_model_release(m);return 1;
}
int animaltown_assets_state(int slot){
    if(slot<0||slot>=ASSETS||!assets[slot].token)return 0;
    Asset *a=&assets[slot];DndModel *m=a->model;
    return (m&&!m->closed&&m->ready&&!m->restoring?1:0)|(a->phase==3?2:0)|(a->restoring?4:0)|(a->disabled?8:0)|(a->fatal?16:0);
}
static int dma(Asset *a,unsigned count,int write){
    unsigned bytes=(count+31u)&~31u;
    if(dnd_aram_begin_native(a->store,(int)a->offset,stage,0,(int)bytes,write))return 0;
    int rc;do{rc=dnd_aram_poll_native();}while(!rc);
    if(rc!=1)return 0;
    transferred+=bytes;return 1;
}
static void fail_backup(Asset *a){drop_store(a);a->disabled=1;a->phase=0;a->offset=0;failures++;}
static int disc_recovery(Asset *a){
    if(a->restoring){animaltown_assets_abort(a->model);a->restoring=0;}
    drop_store(a);a->disabled=1;a->phase=0;a->offset=0;a->work=0;
    a->file=dnd_fs_open_utf8_native(a->path,3,1,1);
    if(!a->file||dnd_fs_length_native(a->file)!=a->length||dnd_model_restore_begin(a->token)<0){
        if(a->file)dnd_fs_close(a->file);
        a->file=0;a->fatal=1;return 0;
    }
    a->restoring=1;recoveries++;return 1;
}
static int restore_step(Asset *a,unsigned char *buffer){
    DndModel *m=a->model;
    if(!a->restoring){
        if(!animaltown_assets_stage_begin(m))return 0;
        a->restoring=1;a->offset=0;a->hash=FNV;a->work=0;
    }
    if(a->work==0){
        unsigned count=a->length-a->offset;if(count>QUANTUM)count=QUANTUM;
        int ok;
        if(a->file)ok=dnd_fs_read_native(a->file,m->staged+a->offset,(int)count)==(int)count;
        else{
            ok=a->phase==3&&dma(a,count,0);
            if(ok){
                unsigned block=~0u;for(unsigned i=0;i<count;i++){block=(block>>8)^crc_table[(block^buffer[i])&255u];a->hash=(a->hash^buffer[i])*16777619u;}
                ok=(~block)==a->links[a->block_base+a->offset/QUANTUM];
                if(ok)memcpy(m->staged+a->offset,buffer,count);
            }
        }
        if(!ok)return -1;
#ifdef DND_GAMECUBE_STORAGE
        DCFlushRange(m->staged+a->offset,count);
#endif
        a->offset+=count;
        if(a->offset==a->length){
            if(a->file){ // Error recovery deliberately uses the full SDK oracle.
                int result=dnd_model_restore_end(a->token,1);dnd_fs_close(a->file);a->file=0;a->restoring=0;a->offset=0;
                return result<0?-2:2;
            }
            if(a->hash!=m->hash)return -1;
            rebase_metadata(a);a->work=1;a->texture=a->cursor=a->search=a->instance=0;a->texture_kind=0;
        }
        return 1;
    }
    if(a->work==1){
        if(a->texture==m->textures){a->work=2;return 1;}
        unsigned index=a->texture,source=a->links[a->texture_base+index*2],hash=a->links[a->texture_base+index*2+1];
        if(!a->texture_kind){a->texture_kind=animaltown_assets_texture_begin(m,index,hash,a->search);if(!a->texture_kind)return -2;return 1;}
        unsigned count=m->texture[index].size-a->cursor;if(count>QUANTUM)count=QUANTUM;
        int result=animaltown_assets_texture_step(m,index,source,a->cursor,count,a->texture_kind==2);
        if(result<0)return -2;
        if(!result){a->search=animaltown_assets_texture_drop(m,index);a->texture_kind=0;a->cursor=0;return 1;}
        a->cursor+=count;
        if(a->cursor==m->texture[index].size){a->texture++;a->cursor=a->search=0;a->texture_kind=0;}
        return 1;
    }
    // Rebuild at most one retained pose per call. No elapsed animation advances.
    while(a->instance<DND_INSTANCE_SLOTS){
        DndInstance *in=dnd_instances[a->instance++];if(!in||in->model!=m)continue;
        m->ready=true;int result=dnd_model_evaluate(in);m->ready=false;
        return result<0?-2:1;
    }
    m->package=m->staged;m->staged=NULL;m->restoring=false;m->ready=true;
    a->restoring=0;a->offset=0;return 2;
}
int animaltown_assets_advance(DndArray *priorities,int startup){
    if(dnd_device_active()||!priorities||priorities->element_size!=4||priorities->length!=ASSETS)return -1;
    const int32_t *want=(const int32_t*)priorities->data;
    for(int i=0;i<ASSETS;i++)if(want[i]<0||want[i]>2)return -1;
    for(int i=0;i<ASSETS;i++){
        Asset *a=&assets[i];if(!a->token)continue;
        if(want[i])a->age=0;else if(a->age<INT_MAX)a->age++;
    }
    if(active_restore>=0&&(!assets[active_restore].token||assets[active_restore].fatal))active_restore=-1;
    if(active_restore>=0&&want[active_restore]<2){
        Asset *a=&assets[active_restore];
        // Urgent actors/rooms may preempt transport, comparisons or completed
        // textures. An uninitialized new texture is never exposed to another
        // registry lookup while its bounded copy is still in progress.
        if(a->work!=1||a->texture_kind!=2)for(int i=0;i<ASSETS;i++)if(assets[i].token&&!assets[i].fatal&&!assets[i].model->ready&&want[i]==2){active_restore=-1;break;}
    }
    int chosen=active_restore;
    // Demand restoration wins over initial backing, without leaving a DMA or
    // managed pointer pending across this call. Keep in-progress jobs bounded.
    for(int pass=0;pass<3&&chosen<0;pass++)for(int n=0;n<ASSETS;n++){
        int i=(last+n)%ASSETS;Asset *a=&assets[i];if(!a->token||a->fatal)continue;
        DndModel *m=a->model;if(!m||m->closed)return -1;
        if(pass==0&&!m->ready&&want[i]==2)chosen=i;
        if(pass==1&&!m->ready&&(want[i]||a->restoring))chosen=i;
        if(pass==2&&m->ready&&a->hold>=0&&!a->disabled&&a->phase!=3)chosen=i;
        if(chosen>=0)break;
    }
    if(chosen>=0){
        Asset *a=&assets[chosen];DndModel *m=a->model;
        unsigned char *buffer=(unsigned char*)(uintptr_t)dnd_buffer_address(stage,0);
        if(!m||m->closed||!buffer)return -1;
        unsigned count;
        if(!m->ready){
            active_restore=chosen;int64_t begin=dnd_timing_now();int result=restore_step(a,buffer);
            unsigned elapsed=(unsigned)(dnd_timing_now()-begin);if(elapsed>max_restore_us)max_restore_us=elapsed;
            if(result<0){
                failures++;
                if(result==-1&&!a->file)(void)disc_recovery(a);
                else{if(a->file){dnd_fs_close(a->file);a->file=0;}animaltown_assets_abort(m);a->restoring=0;a->fatal=1;active_restore=-1;}
            }else if(result==2){invalidate_model_cache();dnd_draw_backend_publish_texture();restores++;active_restore=-1;}
        }else{
            if(!a->store){
                a->store=dnd_aram_allocate((int)((a->length+31u)&~31u));
                if(!a->store||dnd_aram_retain(a->store)){fail_backup(a);return 1;}
                a->phase=1;a->offset=0;a->hash=FNV;
            }
            count=a->length-a->offset;if(count>QUANTUM)count=QUANTUM;
            if(a->phase==1){a->links[a->block_base+a->offset/QUANTUM]=block_crc(m->package+a->offset,count);memcpy(buffer,m->package+a->offset,count);memset(buffer+count,0,((count+31u)&~31u)-count);if(!dma(a,count,1)){fail_backup(a);return 1;}}
            else if(a->phase==2){if(!dma(a,count,0)||block_crc(buffer,count)!=a->links[a->block_base+a->offset/QUANTUM]){fail_backup(a);return 1;}a->hash=hash_bytes(a->hash,buffer,count);}
            else return -1;
            a->offset+=count;
            if(a->offset==a->length){
                a->offset=0;
                if(a->phase==1){a->phase=2;a->hash=FNV;}
                else if(a->hash==m->hash)a->phase=3;
                else fail_backup(a);
            }
        }
        last=(chosen+1)%ASSETS;
    }
    if(!startup)for(int n=0;n<ASSETS;n++){
        int i=(last+n)%ASSETS;Asset *a=&assets[i];
        if(!a->token||want[i]||a->hold<0||a->age<=a->hold||a->phase!=3||a->restoring||a->fatal)continue;
        DndModel *m=a->model;
        if(m&&m->ready){if(dnd_model_unload(a->token)<0)return -1;invalidate_model_cache();a->offset=0;evictions++;break;}
    }
    return chosen>=0?1:0;
}
int animaltown_assets_stats(int kind){
    unsigned pending=0,cold=0,main=0,aram=0;
    for(int i=0;i<ASSETS;i++)if(assets[i].token){Asset *a=&assets[i];DndModel *m=a->model;if(m){main+=m->bytes;if(!m->ready)cold++;}if(a->store)aram+=(a->length+31u)&~31u;if(a->hold>=0&&!a->disabled&&a->phase!=3)pending++;}
    return kind==0?(int)pending:kind==1?(int)cold:kind==2?(int)main:kind==3?(int)aram:kind==4?(int)evictions:kind==5?(int)restores:kind==6?(int)transferred:kind==7?(int)failures:kind==8?(int)recoveries:kind==9?(int)max_restore_us:-1;
}
