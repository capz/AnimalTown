#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include "draw_resources.c"
TownFrameCounters town_frame_counters;
static bool active;
static int flushes;
int dnd_raw_depth(void){return 0;}
bool dnd_device_active(void){return active;}
void dnd_draw_backend_wait(void){}
void dnd_draw_backend_flush(void *p,int bytes,int operation){(void)p;(void)bytes;(void)operation;flushes++;}
static DndArray *array(int n,int size){DndArray *a=calloc(1,sizeof(*a)+(size_t)n*size);assert(a);a->length=n;a->element_size=size;return a;}
static int finish_bind(int view,int entry){
    int result=0;for(int i=0;i<10000&&result==0;i++)result=animaltown_terrain_bind_step(view,entry);
    return result;
}
static void finish_cancel(void){int result=0;for(int i=0;i<10000&&result==0;i++)result=animaltown_terrain_cancel_bind();assert(result==1);}
static void residency_checks(DndArray *vertices,DndArray *indices){
    assert(dnd_draw_live()==0&&animaltown_terrain_reset(1)==1);
    int source=dnd_mesh_create_range(vertices,12,indices,12,2,0);assert(source);
    unsigned char expected[456];memcpy(expected,find(source,MESH)->data,sizeof(expected));
    assert(animaltown_terrain_append(0,3,source,0)==1);
    assert(animaltown_terrain_append(0,104,source,0)==2);
    assert(animaltown_terrain_complete(0)==1&&dnd_draw_release(source)==0);
    int view=animaltown_terrain_view();assert(view&&animaltown_terrain_bind(view,0)==1);
    int aram=dnd_aram_stat(1),payload=animaltown_terrain_stat(5),resident=dnd_draw_total_bytes();
    int metadata=animaltown_terrain_stat(0)-TA_CACHE;
    assert(animaltown_terrain_suspend()==-1); /* A live mesh alias prevents release. */
    assert(dnd_draw_total_bytes()==resident&&dnd_aram_stat(1)==aram);
    active=true;assert(animaltown_terrain_suspend()==-1&&animaltown_terrain_resume()==-1);active=false;
    assert(animaltown_terrain_unbind(view)==1&&animaltown_terrain_suspend()==1);
    assert(animaltown_terrain_stat(8)==0&&animaltown_terrain_stat(0)==metadata);
    assert(dnd_draw_total_bytes()==resident-TA_CACHE&&dnd_aram_stat(1)==aram&&animaltown_terrain_stat(5)==payload);
    assert(animaltown_terrain_suspend()==1); /* Idempotent suspension. */
    int filler=dnd_buffer_create(BUDGET-total_bytes-TA_CACHE/2,32);assert(filler);
    int full=dnd_draw_total_bytes();assert(animaltown_terrain_resume()==-2);
    assert(dnd_draw_total_bytes()==full&&animaltown_terrain_stat(8)==0&&dnd_aram_stat(1)==aram);
    assert(dnd_draw_release(filler)==0&&animaltown_terrain_resume()==1&&animaltown_terrain_resume()==1);
    assert(dnd_draw_total_bytes()==resident&&animaltown_terrain_stat(8)==TA_CACHE);
    assert(animaltown_terrain_bind_step(view,0)==0&&animaltown_terrain_stat(9)==1);
    assert(!find(view,MESH)->data&&animaltown_terrain_reset(0)==-1);
    assert(finish_bind(view,0)==1&&!memcmp(find(view,MESH)->data,expected,sizeof(expected)));
    unsigned char *previous=find(view,MESH)->data;int live=ta_live;
    assert(animaltown_terrain_bind_step(view,1)==0);
    assert(find(view,MESH)->data==previous);
    finish_cancel();
    assert(find(view,MESH)->data==previous&&ta_live==live&&ta_part(1)->cache==-1&&!ta_request);
#ifdef DND_ARAM_TEST
    assert(animaltown_terrain_bind_step(view,1)==0);
    dnd_aram_test_fail_next();assert(finish_bind(view,1)==-3);
    assert(find(view,MESH)->data==previous&&ta_live==live&&ta_part(1)->cache==-1&&!ta_request);
#endif
    assert(animaltown_terrain_unbind(view)==1&&animaltown_terrain_suspend()==1&&animaltown_terrain_resume()==1);
    int cold=dnd_aram_allocate(65536),buffer=dnd_buffer_create(65536,32);assert(cold&&buffer);
    int background=dnd_aram_request_native(cold,0,buffer,0,65536,1,2);assert(background>0);
    assert(dnd_aram_request_poll(background)==0);
    assert(animaltown_terrain_bind_step(view,0)==0&&finish_bind(view,0)==1);
    assert(!memcmp(find(view,MESH)->data,expected,sizeof(expected)));
    int result=0;for(int i=0;i<10000&&result==0;i++)result=dnd_aram_request_poll(background);assert(result==1);
    assert(dnd_draw_release(buffer)==0&&dnd_aram_dispose(cold)==0);
    for(int i=0;i<100;i++){
        assert(animaltown_terrain_unbind(view)==1&&animaltown_terrain_suspend()==1&&animaltown_terrain_resume()==1);
        assert(finish_bind(view,0)==1&&!memcmp(find(view,MESH)->data,expected,sizeof(expected)));
        assert(dnd_draw_total_bytes()==resident&&dnd_aram_stat(1)==aram);
    }
    assert(animaltown_terrain_unbind(view)==1&&animaltown_terrain_suspend()==1&&animaltown_terrain_resume()==1);
    assert(animaltown_terrain_bind_step(view,0)==0);
    int retired_cache=ta_cache;
    assert(dnd_draw_release(retired_cache)==0); /* Held until the owned DMA drains. */
    assert(finish_bind(view,0)==-3&&!find(retired_cache,BUFFER));
    assert(!find(view,MESH)->data&&ta_live==0&&ta_part(0)->cache==-1&&!ta_request);
    /* External cache retirement is outside the private owner's normal API.
     * Repair that owner's bookkeeping only for this defensive lifetime probe. */
    ta_cache=0;ta_bytes-=TA_CACHE;
    assert(animaltown_terrain_resume()==1&&finish_bind(view,0)==1);
    assert(!memcmp(find(view,MESH)->data,expected,sizeof(expected)));
    assert(animaltown_terrain_unbind(view)==1&&animaltown_terrain_suspend()==1&&animaltown_terrain_resume()==1);
    assert(animaltown_terrain_bind_step(view,0)==0);
    assert(dnd_draw_release(view)==0); /* Ticket hold prevents stale-handle reuse. */
    finish_cancel();assert(!find(view,MESH));
    assert(animaltown_terrain_suspend()==1&&animaltown_terrain_reset(0)==1&&dnd_draw_live()==0&&dnd_aram_stat(4)==0);
    puts("Terrain residency: exact 1 MiB release, retained ARAM, alias/GPU guards, budget rollback, owned DMA contention, cancellation and repeated resume passed");
}
int main(void){
    assert(dnd_aram_initialize(2*1024*1024));
    DndArray *v=array(12,36),*ix=array(12,2);float *p=(float*)v->data;uint16_t *ids=(uint16_t*)ix->data;
    for(int i=0;i<12;i++){p[i*9]=i;p[i*9+7]=4+i*.5f;((uint32_t*)v->data)[i*9+6]=i%2?0xffffff00:0xffffffff;ids[i]=(uint16_t)(i%6);}
    int indexed=dnd_mesh_create_range(v,12,ix,12,2,0),plain=dnd_mesh_create_range(v,12,NULL,0,2,0);
    assert(indexed>0&&plain>0&&animaltown_terrain_reset(2)==1);
    assert(animaltown_terrain_stat(0)==1024*1024+64*1024); /* Original cold MEM1 budget. */
    Resource *source=find(indexed,MESH);unsigned char expected[456];memcpy(expected,source->data,sizeof(expected));
    assert(animaltown_terrain_append(-1,0,indexed,0)==-1);
    assert(animaltown_terrain_append(1,0,indexed,0)==-1);
    assert(animaltown_terrain_append(0,3,indexed,0)==1);
    assert(animaltown_terrain_append(0,55,plain,6)==2);
    assert(animaltown_terrain_complete(0)==1&&animaltown_terrain_info(0,0)==-1);
    assert(animaltown_terrain_append(1,104,plain,6)==3);
    assert(animaltown_terrain_append(1,109,plain,6)==-1);
    assert(animaltown_terrain_append(1,107,plain,6)==4);
    assert(animaltown_terrain_complete(1)==1);
    assert(animaltown_terrain_info(0,0)==0&&animaltown_terrain_info(0,1)==2&&animaltown_terrain_info(0,2)==6);
    assert(animaltown_terrain_info(1,0)==2&&animaltown_terrain_info(1,1)==2&&animaltown_terrain_kind(1)==55&&animaltown_terrain_kind(3)==107);
    assert(animaltown_terrain_append(1,3,indexed,0)==-1);
    assert(dnd_draw_release(indexed)==0&&dnd_draw_release(plain)==0);
    int view=animaltown_terrain_view();assert(view>0&&dnd_draw_bytes(view)==1);
    assert(animaltown_terrain_bind(view,0)==1);Resource *r=find(view,MESH);assert(r&&r->vertices==12&&r->indices==12&&!memcmp(r->data,expected,sizeof(expected)));
    assert(animaltown_terrain_reset(0)==-1);
    int flushed=flushes;town_frame_counters.enabled=1;
    for(int i=0;i<10000;i++){
        assert(animaltown_terrain_bind(view,i%4)==1);assert(animaltown_mesh_recycle(view,v,6,ix,6)==-1);
        assert(r->vertices==(i%4?6:12));assert(r->indices==(i%4?0:12));
    }
    assert(flushes==flushed&&!town_frame_counters.count[TOWN_MESH_UPDATES]&&!town_frame_counters.count[TOWN_MESH_CREATES]);
    active=true;assert(animaltown_terrain_bind(view,0)==-1&&animaltown_terrain_unbind(view)==-1&&animaltown_terrain_reset(0)==-1&&!animaltown_terrain_view());active=false;
    assert(animaltown_terrain_bind(view,9999)==-1);
    assert(animaltown_terrain_unbind(view)==1&&r->data==NULL&&!r->terrain_parent);
    assert(animaltown_terrain_reset(0)==1&&dnd_draw_live()==1&&dnd_draw_bytes(view)==1&&dnd_aram_stat(4)==0);
    assert(animaltown_terrain_bind(view,0)==-1&&dnd_draw_release(view)==0&&dnd_draw_live()==0);
    assert(animaltown_terrain_reset(1)==1);plain=dnd_mesh_create_range(v,12,NULL,0,2,0);
    assert(animaltown_terrain_append(0,0,plain,6)==1&&animaltown_terrain_complete(0)==1);
    view=animaltown_terrain_view();assert(animaltown_terrain_bind(view,0)==1);r=find(view,MESH);int page=r->terrain_parent;
    assert(dnd_draw_release(page)==0&&r->data!=NULL); // Parent remains pinned through pending disposal.
    assert(dnd_draw_release(view)==0);assert(!find(page,BUFFER));
    // Simulate external buffer retirement only for this lifetime probe.
    ta_cache=0;assert(animaltown_terrain_reset(0)==1);assert(dnd_draw_release(plain)==0&&dnd_draw_live()==0);
    // Adjacent halves: exact indexed attributes, triangle order and rebasing.
    assert(animaltown_terrain_reset(1)==1);indexed=dnd_mesh_create_range(v,12,ix,12,2,0);plain=dnd_mesh_create_range(v,12,NULL,0,2,0);
    assert(animaltown_terrain_append(0,3,indexed,0)==1);
    assert(animaltown_terrain_append(0,104,plain,6)==2);
    assert(animaltown_terrain_append(0,3,indexed,0)==3);
    assert(animaltown_terrain_append(0,107,plain,6)==4);
    int cold_resources=dnd_draw_live();
    assert(animaltown_terrain_complete(0)==1&&animaltown_terrain_info(0,1)==3&&animaltown_terrain_info(0,2)==12);
    assert(dnd_draw_live()==cold_resources-1&&find(ta_cache,BUFFER)->holds==0); /* Coalescing allocates nothing; stage retires. */
    assert(animaltown_terrain_kind(0)==3&&animaltown_terrain_kind(1)==104&&animaltown_terrain_kind(2)==107);
    view=animaltown_terrain_view();assert(animaltown_terrain_bind(view,0)==1);r=find(view,MESH);
    assert(r->vertices==24&&r->indices==24&&!r->uniform_color);
    assert(!memcmp(r->data,expected,432)&&!memcmp(r->data+432,expected,432));
    uint16_t *merged_ids=(uint16_t*)(r->data+864);
    for(int i=0;i<12;i++){assert(merged_ids[i]==ids[i]);assert(merged_ids[i+12]==ids[i]+12);}
    assert(dnd_draw_release(view)==0&&dnd_draw_release(indexed)==0&&dnd_draw_release(plain)==0&&animaltown_terrain_reset(0)==1);
    // Different uniform tints must keep separate indexed GX material classes.
    assert(animaltown_terrain_reset(1)==1);plain=dnd_mesh_create_range(v,12,NULL,0,2,0);
    Resource *colored=find(plain,MESH);for(int i=0;i<12;i++)((DndDrawVertex*)colored->data)[i].color=0xff123456;
    assert(animaltown_terrain_append(0,3,plain,6)==1);
    for(int i=0;i<12;i++)((DndDrawVertex*)colored->data)[i].color=0xff654321;
    assert(animaltown_terrain_append(0,3,plain,6)==2);
    assert(animaltown_terrain_complete(0)==1&&animaltown_terrain_info(0,1)==2);
    assert(dnd_draw_release(plain)==0&&animaltown_terrain_reset(0)==1);
    assert(animaltown_terrain_reset(1)==1);
    DndArray *big=array(1818,36);plain=dnd_mesh_create_range(big,1818,NULL,0,2,0);assert(plain>0);
    for(int i=0;i<17;i++)assert(animaltown_terrain_append(0,104,plain,1818)==i+1);
    assert(animaltown_terrain_complete(0)==1&&dnd_draw_release(plain)==0);
    int pool[17];for(int i=0;i<17;i++){pool[i]=animaltown_terrain_view();assert(pool[i]);}
    for(int i=0;i<16;i++)assert(animaltown_terrain_bind(pool[i],i)==1);
    unsigned char saved[36];memcpy(saved,find(pool[0],MESH)->data,sizeof(saved));int resident=animaltown_terrain_stat(0);
    assert(animaltown_terrain_bind(pool[16],16)==-2&&!memcmp(saved,find(pool[0],MESH)->data,sizeof(saved)));
    assert(animaltown_terrain_stat(0)==resident&&animaltown_terrain_stat(7)==TA_CACHE);
    assert(animaltown_terrain_unbind(pool[0])==1&&animaltown_terrain_bind(pool[16],16)==1);
    assert(animaltown_terrain_cache(0)==-1); /* Never drop live GX aliases. */
    for(int i=0;i<17;i++)assert(dnd_draw_release(pool[i])==0);
    resident=animaltown_terrain_stat(0);int aram=dnd_aram_stat(4);
    assert(animaltown_terrain_cache(0)==1&&animaltown_terrain_stat(0)==resident-TA_CACHE&&dnd_aram_stat(4)==aram);
    assert(animaltown_terrain_cache(0)==1&&animaltown_terrain_cache(1)==1&&animaltown_terrain_stat(0)==resident);
    int restored=animaltown_terrain_view();assert(restored&&animaltown_terrain_bind(restored,0)==1);
    assert(!memcmp(saved,find(restored,MESH)->data,sizeof(saved)));
    active=true;assert(animaltown_terrain_cache(0)==-1);active=false;
    assert(animaltown_terrain_cache(0)==-1&&animaltown_terrain_unbind(restored)==1);
    assert(dnd_draw_release(restored)==0);
    for(int cycle=0;cycle<32;cycle++)assert(animaltown_terrain_cache(0)==1&&animaltown_terrain_cache(1)==1);
    assert(animaltown_terrain_stat(0)==resident&&dnd_aram_stat(4)==aram);

    assert(animaltown_terrain_reset(0)==1&&dnd_draw_live()==0&&dnd_aram_stat(4)==0);free(big);
    assert(animaltown_terrain_reset(1)==1);
    plain=dnd_mesh_create_range(v,12,NULL,0,2,0);assert(plain>0);
    assert(animaltown_terrain_append(0,104,plain,6)==1);
    assert(animaltown_terrain_append(0,108,plain,6)==2);
    assert(animaltown_terrain_append(0,108,plain,6)==3);
    assert(animaltown_terrain_append(0,109,plain,6)==-1);
    assert(animaltown_terrain_complete(0)==1);
    assert(animaltown_terrain_info(0,1)==2&&animaltown_terrain_kind(0)==104&&animaltown_terrain_kind(1)==108);
    assert(animaltown_terrain_info(0,3)==4&&animaltown_terrain_info(0,2)==6);
    assert(dnd_draw_release(plain)==0&&animaltown_terrain_reset(0)==1);
    residency_checks(v,ix);free(v);free(ix);puts("Terrain archive: exact indexed/trimmed payloads retain UV/alpha; 10000 byte-exact ARAM view binds; no uploads/allocations; atomic cache exhaustion, save-cache suspension/resume, reuse, guards and pinned parent lifetime passed");

}
