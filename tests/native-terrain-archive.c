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
int main(void){
    DndArray *v=array(12,36),*ix=array(12,2);float *p=(float*)v->data;uint16_t *ids=(uint16_t*)ix->data;
    for(int i=0;i<12;i++){p[i*9]=i;p[i*9+7]=4+i*.5f;((uint32_t*)v->data)[i*9+6]=i%2?0xffffff00:0xffffffff;ids[i]=(uint16_t)(i%6);}
    int indexed=dnd_mesh_create_range(v,12,ix,12,2,0),plain=dnd_mesh_create_range(v,12,NULL,0,2,0);
    assert(indexed>0&&plain>0&&animaltown_terrain_reset(2)==1);
    Resource *source=find(indexed,MESH);unsigned char expected[456];memcpy(expected,source->data,sizeof(expected));
    assert(animaltown_terrain_append(-1,0,indexed,0)==-1);
    assert(animaltown_terrain_append(1,0,indexed,0)==-1);
    assert(animaltown_terrain_append(0,3,indexed,0)==1);
    assert(animaltown_terrain_append(0,55,plain,6)==2);
    assert(animaltown_terrain_complete(0)==1&&animaltown_terrain_info(0,0)==-1);
    assert(animaltown_terrain_append(1,104,plain,6)==3);
    assert(animaltown_terrain_complete(1)==1);
    assert(animaltown_terrain_info(0,0)==0&&animaltown_terrain_info(0,1)==2&&animaltown_terrain_info(0,2)==6);
    assert(animaltown_terrain_info(1,0)==2&&animaltown_terrain_info(1,1)==1&&animaltown_terrain_kind(1)==55);
    assert(animaltown_terrain_append(1,3,indexed,0)==-1);
    assert(dnd_draw_release(indexed)==0&&dnd_draw_release(plain)==0);
    int view=animaltown_terrain_view();assert(view>0&&dnd_draw_bytes(view)==1);
    assert(animaltown_terrain_bind(view,0)==1);Resource *r=find(view,MESH);assert(r&&r->vertices==12&&r->indices==12&&!memcmp(r->data,expected,sizeof(expected)));
    assert(animaltown_terrain_reset(0)==-1);
    int flushed=flushes;town_frame_counters.enabled=1;
    for(int i=0;i<10000;i++){
        assert(animaltown_terrain_bind(view,i%3)==1);assert(animaltown_mesh_recycle(view,v,6,ix,6)==-1);
        assert(r->vertices==(i%3?6:12));assert(r->indices==(i%3?0:12));
    }
    assert(flushes==flushed&&!town_frame_counters.count[TOWN_MESH_UPDATES]&&!town_frame_counters.count[TOWN_MESH_CREATES]);
    active=true;assert(animaltown_terrain_bind(view,0)==-1&&animaltown_terrain_unbind(view)==-1&&animaltown_terrain_reset(0)==-1&&!animaltown_terrain_view());active=false;
    assert(animaltown_terrain_bind(view,9999)==-1);
    assert(animaltown_terrain_unbind(view)==1&&r->data==NULL&&!r->terrain_parent);
    assert(animaltown_terrain_reset(0)==1&&dnd_draw_live()==1&&dnd_draw_bytes(view)==1);
    assert(animaltown_terrain_bind(view,0)==-1&&dnd_draw_release(view)==0&&dnd_draw_live()==0);
    assert(animaltown_terrain_reset(1)==1);plain=dnd_mesh_create_range(v,12,NULL,0,2,0);
    assert(animaltown_terrain_append(0,0,plain,6)==1&&animaltown_terrain_complete(0)==1);
    view=animaltown_terrain_view();assert(animaltown_terrain_bind(view,0)==1);r=find(view,MESH);int page=r->terrain_parent;
    assert(dnd_draw_release(page)==0&&r->data!=NULL); // Parent remains pinned through pending disposal.
    assert(dnd_draw_release(view)==0);assert(!find(page,BUFFER));
    // Simulate external buffer retirement only for this lifetime probe.
    ta_page_count=0;assert(animaltown_terrain_reset(0)==1);assert(dnd_draw_release(plain)==0&&dnd_draw_live()==0);
    free(v);free(ix);puts("Terrain archive: exact indexed/trimmed payloads retain UV/alpha; 10000 zero-copy view binds; no uploads/allocations; guards and pinned parent lifetime passed");
}
