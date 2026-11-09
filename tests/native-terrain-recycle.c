#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include "draw_resources.c"
TownFrameCounters town_frame_counters;
static bool active;
int dnd_raw_depth(void){return 0;}
bool dnd_device_active(void){return active;}
void dnd_draw_backend_wait(void){}
void dnd_draw_backend_flush(void *p,int bytes,int operation){(void)p;(void)bytes;(void)operation;}
static DndArray *array(int count,int size){DndArray *a=calloc(1,sizeof(*a)+(size_t)count*size);assert(a);a->length=count;a->element_size=size;return a;}
int main(void){
    DndArray *v=array(12,36),*ix=array(12,2);float *p=(float*)v->data;uint16_t *ids=(uint16_t*)ix->data;
    for(int i=0;i<12;i++){p[i*9]=(float)i;ids[i]=(uint16_t)(i%6);}
    int handle=dnd_mesh_create_range(v,12,ix,12,2,0);assert(handle>0);int bytes=dnd_draw_bytes(handle);
    town_frame_counters.enabled=1;
    for(int n=0;n<1000;n++){
        p[0]=(float)n;
        assert(animaltown_mesh_recycle(handle,v,6,ix,6)==1);
        Resource *r=find(handle,MESH);assert(r&&r->vertices==6&&r->indices==6&&r->bytes==bytes);
        assert(((DndDrawVertex*)r->data)[0].x==(float)n);
        assert(!memcmp(r->data+6*sizeof(DndDrawVertex),ix->data,12));
    }
    assert(dnd_draw_live()==1&&town_frame_counters.count[TOWN_MESH_UPDATES]==1000&&town_frame_counters.count[TOWN_MESH_CREATES]==0);
    Resource *r=find(handle,MESH);unsigned char *saved=malloc(bytes);memcpy(saved,r->data,bytes);
    ids[0]=99;assert(animaltown_mesh_recycle(handle,v,6,ix,6)==-1);ids[0]=0;
    p[0]=NAN;assert(animaltown_mesh_recycle(handle,v,6,ix,6)==-1);p[0]=0;
    active=true;assert(animaltown_mesh_recycle(handle,v,6,ix,6)==-1);active=false;
    assert(!memcmp(saved,r->data,bytes)&&r->vertices==6&&r->indices==6);
    DndArray *large=array(100,36);assert(animaltown_mesh_recycle(handle,large,100,ix,6)==0);assert(!memcmp(saved,r->data,bytes));
    assert(dnd_draw_release(handle)==0&&dnd_draw_live()==0);free(saved);free(large);free(v);free(ix);
    puts("Terrain recycle: 1000 indexed updates retain handle/bytes; topology, shrink, growth fallback and atomic rejection passed");
}
