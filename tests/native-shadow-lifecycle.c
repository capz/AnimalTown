/* Exercise the actual native bridge with a charged, single-owner SDK buffer.
 * Draft cancellation must release it and leave the next town's casters empty. */
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include "dnd_draw.h"
#include "dnd_timing.h"
#include "../tools/native-shadow-union.h"

static void *allocation;
static int allocated,live,active,release_error,create_error,reset_count,invalidations;
bool dnd_device_active(void){return active!=0;}
int dnd_buffer_create(int bytes,int alignment){
    assert(!live&&alignment==32);
    if(create_error)return 0;
    allocation=aligned_alloc((size_t)alignment,(size_t)bytes);assert(allocation);
    allocated=bytes;live=1;return 101;
}
uint64_t dnd_buffer_address(int token,int offset){assert(token==101&&offset==0&&live);return (uintptr_t)allocation;}
int dnd_draw_release(int token){
    assert(token==101&&live&&!active);
    if(release_error)return -1;
    free(allocation);allocation=NULL;allocated=live=0;return 0;
}
int dnd_draw_live(void){return live;}
int dnd_draw_total_bytes(void){return allocated;}
void dnd_draw_backend_flush(void *data,int bytes,int invalidate){(void)data;(void)bytes;(void)invalidate;}
void dnd_draw_backend_publish_texture(void){}
void dnd_draw_backend_invalidate_cache(void){invalidations++;}
void animaltown_shadows_model_reset(void){reset_count++;}
int64_t dnd_timing_now(void){return 0;}
#include "../tools/native-shadow-union.c"

static DndArray *pixels(int count){
    DndArray *a=calloc(1,sizeof(*a)+(size_t)count);assert(a);
    a->length=(uint32_t)count;a->element_size=1;memset(a->data,255,(size_t)count);return a;
}
int main(void){
    DndArray *atlas=pixels(32768),*circle=pixels(2048),*cloud=pixels(SU_CLOUD_BYTES);memset(cloud->data,0,SU_CLOUD_BYTES);
    assert(animaltown_shadows_shutdown()==1&&!live);
    assert(animaltown_shadows_init(NULL,circle,cloud)==0&&!live);
    create_error=1;assert(animaltown_shadows_init(atlas,circle,cloud)==0&&!live);create_error=0;
    assert(animaltown_shadows_init(atlas,circle,cloud)==1&&live==1);
    DndArray *offset=calloc(1,sizeof(*offset)+8);assert(offset);offset->length=2;offset->element_size=4;
    assert(animaltown_shadows_cloud(offset)==1);
    ((float*)offset->data)[0]=NAN;assert(animaltown_shadows_cloud(offset)==0);
    ((float*)offset->data)[0]=1;assert(animaltown_shadows_cloud(offset)==0);
    ((float*)offset->data)[0]=.5f;active=1;assert(animaltown_shadows_cloud(offset)==0);active=0;
    assert(animaltown_shadows_cloud(offset)==1&&shadows->cloud_x==128);
    offset->length=1;assert(animaltown_shadows_cloud(offset)==0);free(offset);
    assert(animaltown_shadows_stats(10)==(int)su_storage_bytes());
    /* Repeated init does not allocate a second coverage cache. */
    assert(animaltown_shadows_init(atlas,circle,cloud)==1&&live==1);
    float origin[6];assert(animaltown_shadows_begin(0,1,0)==1);
    assert(animaltown_shadows_page(2,2,origin)==NULL&&origin[2]==8&&origin[3]==32&&origin[4]==8&&origin[5]==32);
    DndArray *empty=pixels(0),*tile=pixels(32);memset(tile->data,33,32);
    assert(animaltown_shadows_terrain(empty,0,0)==1);
    assert(animaltown_shadows_terrain(tile,0,1)==1);
    assert(animaltown_shadows_terrain(tile,SU_TERRAIN_BYTES-32,1)==1);
    assert(animaltown_shadows_terrain(tile,SU_TERRAIN_BYTES,1)==0);
    assert(animaltown_shadows_terrain(empty,0,2)==1);
    assert(animaltown_shadows_prepare(1000)==1);
    assert(animaltown_shadows_page(2,2,origin)==shadows->coverage&&origin[2]==254&&origin[3]==256&&origin[4]==126&&origin[5]==128);
    assert(su_terrain_sample(shadows,2,2)==33); /* One texel/m, including the gutter. */
    free(empty);free(tile);
    shadows->caster[0].active=1;shadows->terrain_ready=1;
    active=1;assert(animaltown_shadows_shutdown()==0&&live==1&&shadows);active=0;
    release_error=1;assert(animaltown_shadows_shutdown()==0&&live==1&&shadows);release_error=0;
    assert(animaltown_shadows_shutdown()==1&&!live&&!allocated&&!shadows&&!shadow_storage);
    assert(reset_count==1&&invalidations==1);
    assert(animaltown_shadows_stats(10)==0&&animaltown_shadows_mode()==0);
    assert(animaltown_shadows_shutdown()==1&&reset_count==1);
    /* A fresh session cannot inherit a discarded world's static coverage. */
    assert(animaltown_shadows_init(atlas,circle,cloud)==1&&live==1);
    for(int i=0;i<SU_CASTERS;i++)assert(!shadows->caster[i].active);
    assert(!shadows->terrain_ready&&shadows->shapes[0]==255);
    assert(animaltown_shadows_shutdown()==1&&!live&&reset_count==2);
    free(atlas);free(circle);free(cloud);
    puts("Shadow bridge lifecycle: charged buffers released, safe retries and clean town reconstruction passed");
    return 0;
}
