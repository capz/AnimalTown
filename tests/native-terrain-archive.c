#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>

/* Deny only the old standalone staging request, through the real allocator. */
static size_t denied_resource_size;
static int resource_allocations,denied_resource_allocations;
static void *resource_malloc(size_t bytes){
    resource_allocations++;
    if(bytes==denied_resource_size){denied_resource_allocations++;return NULL;}
    return malloc(bytes);
}
#define malloc resource_malloc
#include "draw_resources.c"
#undef malloc
TownFrameCounters town_frame_counters;
static bool active;
static int flushes;
uint32_t animaltown_profile_clock(void){static uint32_t ticks;return ++ticks;}
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
static void metadata_fragmentation_checks(void){
    denied_resource_size=256u*sizeof(TaPart)+31u;
    assert(!dnd_buffer_create(256*(int)sizeof(TaPart),32));
    assert(animaltown_terrain_reset(1600)==1);
    DndArray *v=array(3,36);int mesh=dnd_mesh_create_range(v,3,NULL,0,2,0);assert(mesh>0);
    for(int i=0;i<1600;i++){
        assert(animaltown_terrain_append(i,0,mesh,3)>0);
        assert(animaltown_terrain_complete(i)==1);
    }
    assert(ta_parts==1600&&ta_meta_count==(1600+TA_META_PARTS-1)/TA_META_PARTS);
    for(int i=0;i<1600;i++)assert(ta_part(i)->offset==i*128&&ta_part(i)->vertices==3);
    assert(dnd_draw_release(mesh)==0&&animaltown_terrain_reset(0)==1&&dnd_draw_live()==0);
    free(v);denied_resource_size=0;denied_resource_allocations=0;
    puts("Terrain metadata: 1600 parts survive rejected 9 KiB allocations with bounded small pages and exact reset cleanup");
}
static void shared_bake_checks(void){
    assert(dnd_draw_live()==0);
    denied_resource_size=TA_STAGE+31u;
    assert(!dnd_buffer_create(TA_STAGE,32)&&denied_resource_allocations==1);
    int allocations=resource_allocations;
    assert(animaltown_terrain_reset(2)==1&&resource_allocations==allocations+1);
    int cache=ta_cache;
    assert(ta_stage==cache&&find(cache,BUFFER)->holds==1&&dnd_draw_live()==1&&ta_bytes==TA_CACHE);
    assert(animaltown_terrain_reset(0)==1&&!find(cache,BUFFER)&&!ta_stage&&!ta_cache&&dnd_draw_live()==0&&dnd_aram_stat(4)==0);
    assert(animaltown_terrain_reset(2)==1&&ta_stage==ta_cache&&find(ta_cache,BUFFER)->holds==1);

    /* Two padded 32-KiB indexed parts exactly fill the input region. Distinct
     * vertices expose an overlapping in-place merge or a wrong DMA offset. */
    enum {VERTICES=900,INDICES=174};
    DndArray *v=array(VERTICES,36),*ix=array(INDICES,2);
    float *input=(float*)v->data;uint16_t *ids=(uint16_t*)ix->data;
    for(int i=0;i<VERTICES;i++){input[i*9]=(float)i;input[i*9+7]=i*.25f;input[i*9+8]=i*.5f;((uint32_t*)v->data)[i*9+6]=i%2?0xffffff00:0xffffffff;}
    for(int i=0;i<INDICES;i++)ids[i]=(uint16_t)(i*5);
    int source=dnd_mesh_create_range(v,VERTICES,ix,INDICES,2,0);assert(source);
    int vertex_bytes=VERTICES*(int)sizeof(DndDrawVertex),payload=vertex_bytes+INDICES*2;
    assert(((payload+31)&~31)==TA_STAGE/2);
    unsigned char *expected=calloc(1,TA_STAGE);assert(expected);
    Resource *mesh=find(source,MESH);memcpy(expected,mesh->data,(size_t)payload);
    assert(animaltown_terrain_append(0,3,source,0)==1);
    DndDrawVertex *points=(DndDrawVertex*)mesh->data;
    for(int i=0;i<VERTICES;i++){points[i].x+=1024;points[i].u=-points[i].u-1;}
    memcpy(expected+TA_STAGE/2,mesh->data,(size_t)payload);
    assert(animaltown_terrain_append(0,3,source,0)==2&&ta_tail==TA_STAGE);
    Resource *workspace=find(ta_cache,BUFFER);memset(workspace->data,0xa5,TA_CACHE);
    int live=dnd_draw_live(),bytes=dnd_draw_total_bytes();allocations=resource_allocations;
    assert(animaltown_terrain_complete(0)==1&&ta_stage==ta_cache&&workspace->holds==1);
    assert(dnd_draw_live()==live&&dnd_draw_total_bytes()==bytes&&resource_allocations==allocations);
    assert(!memcmp(workspace->data,expected,TA_STAGE)); /* Input survives output and descriptor writes. */
    assert(!memcmp(workspace->data+2*TA_STAGE,ta_part(0),sizeof(TaPart)));
    assert(!memcmp(workspace->data+TA_STAGE,expected,(size_t)vertex_bytes));
    assert(!memcmp(workspace->data+TA_STAGE+vertex_bytes,expected+TA_STAGE/2,(size_t)vertex_bytes));
    for(int i=TA_STAGE+ta_size(ta_part(0));i<2*TA_STAGE;i++)assert(workspace->data[i]==0xa5);
    for(int i=2*TA_STAGE+TA_PAGE_PARTS*(int)sizeof(TaPart);i<TA_CACHE;i++)assert(workspace->data[i]==0xa5);
    int view=animaltown_terrain_view();assert(view&&animaltown_terrain_bind(view,0)==-1);
    int plain=dnd_mesh_create_range(v,3,NULL,0,2,0);assert(plain);
    assert(animaltown_terrain_append(1,104,plain,3)==2);
    live=dnd_draw_live();bytes=dnd_draw_total_bytes();allocations=resource_allocations;
    assert(animaltown_terrain_complete(1)==1&&!ta_stage&&workspace->holds==0);
    assert(dnd_draw_live()==live&&dnd_draw_total_bytes()==bytes&&resource_allocations==allocations);
    assert(animaltown_terrain_info(0,1)==1&&animaltown_terrain_info(1,1)==1);
    assert(animaltown_terrain_bind(view,0)==1);mesh=find(view,MESH);
    assert(mesh->vertices==2*VERTICES&&mesh->indices==2*INDICES);
    assert(!memcmp(mesh->data,expected,(size_t)vertex_bytes)&&!memcmp(mesh->data+vertex_bytes,expected+TA_STAGE/2,(size_t)vertex_bytes));
    uint16_t *merged=(uint16_t*)(mesh->data+2*vertex_bytes);
    for(int i=0;i<INDICES;i++)assert(merged[i]==ids[i]&&merged[INDICES+i]==ids[i]+VERTICES);
    assert(animaltown_terrain_bind(view,1)==1&&!memcmp(find(view,MESH)->data,find(plain,MESH)->data,3*sizeof(DndDrawVertex)));
    assert(dnd_draw_release(view)==0&&dnd_draw_release(source)==0&&dnd_draw_release(plain)==0&&animaltown_terrain_reset(0)==1);
    assert(!ta_stage&&!ta_cache&&dnd_draw_live()==0&&dnd_aram_stat(4)==0&&denied_resource_allocations==1);
    denied_resource_size=0;free(expected);free(v);free(ix);
    puts("Shared terrain bake: one cache allocation, exact 64-KiB coalescer input, disjoint scratch, byte-exact DMA output and lease cleanup passed");
}
static void residency_checks(DndArray *vertices,DndArray *indices){
    assert(dnd_draw_live()==0&&animaltown_terrain_reset(1)==1);
    int source=dnd_mesh_create_range(vertices,12,indices,12,2,0);assert(source);
    unsigned char expected[456];memcpy(expected,find(source,MESH)->data,sizeof(expected));
    assert(animaltown_terrain_append(0,3,source,0)==1);
    assert(animaltown_terrain_append(0,104,source,0)==2);
    assert(animaltown_terrain_complete(0)==1&&dnd_draw_release(source)==0);
    int view=animaltown_terrain_view();assert(view&&animaltown_terrain_bind(view,0)==1);
    assert(animaltown_terrain_stat(10)>0&&animaltown_terrain_stat(11)<TA_CACHE&&animaltown_terrain_stat(12)==1&&animaltown_terrain_stat(13)>0&&animaltown_terrain_stat(14)>0);
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
static void fragmented_archive_checks(void){
    const int spaniel_bytes=244544,hamster_bytes=323200;
    int old_archive=dnd_aram_allocate(TA_BYTES),spaniel=dnd_aram_allocate(spaniel_bytes);
    assert(old_archive&&spaniel&&dnd_aram_dispose(old_archive)==0);
    int hamster=dnd_aram_allocate(hamster_bytes);
    assert(hamster&&dnd_aram_stat(2)>=TA_BYTES&&dnd_aram_stat(3)<TA_BYTES);
    assert(animaltown_terrain_reset(9)==1&&animaltown_terrain_stat(15)==TA_BYTES);

    /* Seven 32-KiB parts and a short part put the final mesh across a
     * backing block boundary. The blocks need not be adjacent in ARAM. */
    enum {VERTICES=900,INDICES=174};
    DndArray *v=array(VERTICES,36),*ix=array(INDICES,2);
    for(int i=0;i<VERTICES;i++)((float*)v->data)[i*9]=(float)i;
    for(int i=0;i<INDICES;i++)((uint16_t*)ix->data)[i]=(uint16_t)(i*5);
    int big=dnd_mesh_create_range(v,VERTICES,ix,INDICES,2,0);
    int short_mesh=dnd_mesh_create_range(v,3,NULL,0,2,0);
    assert(big&&short_mesh);
    for(int i=0;i<9;i++){
        assert(animaltown_terrain_append(i,3,i==7?short_mesh:big,0)==i+1);
        assert(animaltown_terrain_complete(i)==1);
    }
    assert(ta_part(8)->offset==TA_ARAM_BLOCK-TA_STAGE/2+128);
    assert(animaltown_terrain_stat(5)==TA_ARAM_BLOCK+128&&animaltown_terrain_stat(15)==2*TA_ARAM_BLOCK);
    assert(dnd_aram_stat(1)==2*TA_ARAM_BLOCK+spaniel_bytes+hamster_bytes&&dnd_aram_stat(4)==4);
    int view=animaltown_terrain_view();
    Resource *expected=find(big,MESH);
    const size_t payload=VERTICES*sizeof(DndDrawVertex)+INDICES*sizeof(uint16_t);
    assert(view&&animaltown_terrain_bind(view,8)==1&&!memcmp(find(view,MESH)->data,expected->data,payload));
    assert(animaltown_terrain_bind(view,7)==1);
    const unsigned char *previous=find(view,MESH)->data;
    assert(animaltown_terrain_bind_step(view,8)==0);
    for(int i=0;i<1000&&ta_pending_bytes==0;i++)assert(animaltown_terrain_bind_step(view,8)==0);
    assert(ta_pending_bytes==TA_STAGE/2-128&&find(view,MESH)->data==previous);
    finish_cancel();
    assert(ta_part(8)->cache==-1&&find(view,MESH)->data==previous&&animaltown_terrain_stat(9)==0);
    assert(finish_bind(view,8)==1&&!memcmp(find(view,MESH)->data,expected->data,payload));
    assert(dnd_draw_release(view)==0&&dnd_draw_release(big)==0&&dnd_draw_release(short_mesh)==0);
    assert(animaltown_terrain_reset(0)==1&&animaltown_terrain_stat(15)==0&&dnd_draw_live()==0);
    assert(dnd_aram_dispose(spaniel)==0&&dnd_aram_dispose(hamster)==0&&dnd_aram_stat(1)==0);
    free(v);free(ix);
    puts("Terrain ARAM: fragmented bake, unused-block reclaim, split DMA and cancellation passed");
}
int main(void){
    assert(dnd_aram_initialize(2*1024*1024));
    metadata_fragmentation_checks();
    shared_bake_checks();
    fragmented_archive_checks();
    DndArray *v=array(12,36),*ix=array(12,2);float *p=(float*)v->data;uint16_t *ids=(uint16_t*)ix->data;
    for(int i=0;i<12;i++){p[i*9]=i;p[i*9+7]=4+i*.5f;((uint32_t*)v->data)[i*9+6]=i%2?0xffffff00:0xffffffff;ids[i]=(uint16_t)(i%6);}
    int indexed=dnd_mesh_create_range(v,12,ix,12,2,0),plain=dnd_mesh_create_range(v,12,NULL,0,2,0);
    assert(indexed>0&&plain>0&&animaltown_terrain_reset(2)==1);
    assert(animaltown_terrain_stat(0)==TA_CACHE&&ta_stage==ta_cache); /* Bake borrows the idle cache. */
    Resource *source=find(indexed,MESH);unsigned char expected[456];memcpy(expected,source->data,sizeof(expected));
    assert(animaltown_terrain_append(-1,0,indexed,0)==-1);
    assert(animaltown_terrain_append(1,0,indexed,0)==-1);
    assert(animaltown_terrain_append(0,3,indexed,0)==1);
    assert(animaltown_terrain_append(0,55,plain,6)==2);
    assert(animaltown_terrain_complete(0)==1&&animaltown_terrain_info(0,0)==-1);
    assert(animaltown_terrain_append(1,104,plain,6)==3);
    assert(animaltown_terrain_append(1,110,plain,6)==-1);
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
    assert(dnd_draw_live()==cold_resources&&!ta_stage&&find(ta_cache,BUFFER)->holds==0); /* Seal retires only the shared lease. */
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
    assert(animaltown_terrain_append(0,107,plain,6)==2);
    assert(animaltown_terrain_append(0,107,plain,6)==3);
    assert(animaltown_terrain_append(0,108,plain,6)==4); /* Opaque riverbed. */
    assert(animaltown_terrain_append(0,109,plain,6)==5); /* Faceted rocks. */
    assert(animaltown_terrain_append(0,110,plain,6)==-1);
    assert(animaltown_terrain_complete(0)==1);
    assert(animaltown_terrain_info(0,1)==4&&animaltown_terrain_kind(0)==104&&animaltown_terrain_kind(1)==107);
    assert(animaltown_terrain_kind(2)==108&&animaltown_terrain_kind(3)==109);
    assert(animaltown_terrain_info(0,3)==-1&&animaltown_terrain_info(0,2)==10);
    view=animaltown_terrain_view();assert(view>0);
    for(int part=2;part<4;part++){
        assert(animaltown_terrain_bind(view,part)==1);
        r=find(view,MESH);assert(r&&r->vertices==6&&!r->indices);
        assert(!memcmp(r->data,find(plain,MESH)->data,6*sizeof(DndDrawVertex)));
    }
    assert(dnd_draw_release(view)==0);
    assert(dnd_draw_release(plain)==0&&animaltown_terrain_reset(0)==1);
    residency_checks(v,ix);free(v);free(ix);puts("Terrain archive: exact indexed/trimmed payloads retain UV/alpha; 10000 byte-exact ARAM view binds; no uploads/allocations; atomic cache exhaustion, save-cache suspension/resume, reuse, guards and pinned parent lifetime passed");

}
