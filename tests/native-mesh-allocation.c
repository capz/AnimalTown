#include <assert.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>

/* Exercise the production allocator with a bounded largest-free-block fault.
 * Arrays use the ordinary host allocator; only native resource malloc is denied. */
static size_t reject_at;
static void *resource_malloc(size_t bytes){return reject_at&&bytes>=reject_at?NULL:malloc(bytes);}
#define malloc resource_malloc
#include "draw_resources.c"
#undef malloc

TownFrameCounters town_frame_counters;
static bool active;
static int raw;
int dnd_raw_depth(void){return raw;}
bool dnd_device_active(void){return active;}
void dnd_draw_backend_wait(void){}
void dnd_draw_backend_flush(void *p,int bytes,int operation){(void)p;(void)bytes;(void)operation;}
static DndArray *array(int count,int size){
    DndArray *a=calloc(1,sizeof(*a)+(size_t)count*size);assert(a);
    a->length=count;a->element_size=size;return a;
}
static int mesh(DndArray *vertices,int count){return dnd_mesh_create_range(vertices,count,NULL,0,1,0);}
static void unchanged(int live,int bytes){assert(dnd_draw_live()==live&&dnd_draw_total_bytes()==bytes);}

int main(void){
    DndArray *vertices=array(516,24),*indices=array(3,2);
    assert(animaltown_take_mesh_allocation_failure()==0);

    /* A large panel cannot fit, but the smaller shared label still can. */
    reject_at=5000;
    assert(mesh(vertices,516)==0);
    unchanged(0,0);
    assert(animaltown_take_mesh_allocation_failure()==1);
    assert(animaltown_take_mesh_allocation_failure()==0);
    int small=mesh(vertices,126);assert(small>0);
    assert(animaltown_take_mesh_allocation_failure()==0);
    int bytes=dnd_draw_bytes(small);assert(bytes==4536);

    /* Both a later success and every rejected mesh envelope clear old results. */
    assert(mesh(vertices,516)==0);
    int next=mesh(vertices,3);assert(next>0);
    assert(animaltown_take_mesh_allocation_failure()==0);
    assert(dnd_draw_release(next)==0);
    assert(mesh(vertices,516)==0);
    active=true;assert(mesh(vertices,3)==0);active=false;
    assert(animaltown_take_mesh_allocation_failure()==0);
    unchanged(1,bytes);
    assert(mesh(vertices,516)==0);
    assert(dnd_mesh_create_range(NULL,3,NULL,0,1,0)==0);
    assert(animaltown_take_mesh_allocation_failure()==0);
    assert(mesh(vertices,4)==0);
    assert(animaltown_take_mesh_allocation_failure()==0);

    /* An unrelated resource failure cannot be recovered as a UI mesh miss. */
    assert(mesh(vertices,516)==0);
    reject_at=1;assert(dnd_buffer_create(32,32)==0);
    assert(animaltown_take_mesh_allocation_failure()==0);
    reject_at=0;

    /* Invalid data and frame/raw/budget/slot policy failures stay hard failures. */
    raw=1;assert(mesh(vertices,3)==0);raw=0;
    assert(animaltown_take_mesh_allocation_failure()==0);
    int accounted=total_bytes;total_bytes=BUDGET;
    assert(mesh(vertices,3)==0);total_bytes=accounted;
    assert(animaltown_take_mesh_allocation_failure()==0);
    ((float*)vertices->data)[0]=NAN;assert(mesh(vertices,3)==0);
    ((float*)vertices->data)[0]=0;
    assert(animaltown_take_mesh_allocation_failure()==0);
    vertices->element_size=28;assert(mesh(vertices,3)==0);vertices->element_size=24;
    assert(animaltown_take_mesh_allocation_failure()==0);
    assert(dnd_mesh_create_range(vertices,3,NULL,0,99,0)==0);
    assert(animaltown_take_mesh_allocation_failure()==0);
    ((uint16_t*)indices->data)[0]=3;
    assert(dnd_mesh_create_range(vertices,3,indices,3,1,0)==0);
    assert(animaltown_take_mesh_allocation_failure()==0);
    unchanged(1,bytes);

    int handles[SLOTS-1];
    for(int i=0;i<SLOTS-1;i++){handles[i]=dnd_buffer_create(32,32);assert(handles[i]>0);}
    assert(mesh(vertices,3)==0);
    assert(animaltown_take_mesh_allocation_failure()==0);
    for(int i=0;i<SLOTS-1;i++)assert(dnd_draw_release(handles[i])==0);
    assert(dnd_draw_release(small)==0);
    unchanged(0,0);

    reject_at=1;assert(mesh(vertices,126)==0);
    assert(animaltown_take_mesh_allocation_failure()==1);
    assert(animaltown_take_mesh_allocation_failure()==0);
    reject_at=0;small=mesh(vertices,126);assert(small>0);
    assert(animaltown_take_mesh_allocation_failure()==0);
    assert(dnd_draw_release(small)==0);unchanged(0,0);
    free(indices);free(vertices);
    puts("Native mesh allocation: consumed malloc-only result, small-mesh survival, stale isolation, hard failures and rollback passed");
    return 0;
}
