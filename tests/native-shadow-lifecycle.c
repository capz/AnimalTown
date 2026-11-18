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
    DndArray *offset=calloc(1,sizeof(*offset)+24);assert(offset);offset->length=6;offset->element_size=4;
    float *frame=(float*)offset->data;frame[2]=32;frame[3]=255;frame[4]=SU_OPACITY;
    assert(animaltown_shadows_cloud(offset)==1);
    ((float*)offset->data)[0]=NAN;assert(animaltown_shadows_cloud(offset)==0);
    ((float*)offset->data)[0]=1;assert(animaltown_shadows_cloud(offset)==0);
    ((float*)offset->data)[0]=.5f;active=1;assert(animaltown_shadows_cloud(offset)==0);active=0;
    assert(animaltown_shadows_cloud(offset)==1&&shadows->cloud_x==128);
    frame[2]=NAN;assert(animaltown_shadows_cloud(offset)==0);frame[2]=64;
    frame[3]=0;assert(animaltown_shadows_cloud(offset)==0);frame[3]=255;
    frame[4]=SU_OPACITY+1;assert(animaltown_shadows_cloud(offset)==0);frame[4]=33;
    frame[5]=NAN;assert(animaltown_shadows_cloud(offset)==0);
    frame[5]=-1;assert(animaltown_shadows_cloud(offset)==0);
    frame[5]=255;assert(animaltown_shadows_cloud(offset)==0);frame[5]=96;
    assert(animaltown_shadows_cloud(offset)==1&&shadows->cloud_x==256&&shadows->cloud_scale==1);
    offset->elements_are_references=1;assert(animaltown_shadows_cloud(offset)==0);offset->elements_are_references=0;
    offset->length=5;assert(animaltown_shadows_cloud(offset)==0);free(offset);
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
    assert(su_terrain_sample(shadows,2,2)==su_shadow(su_pack(33,0))); /* One texel/m, including the gutter. */
    /* Moving model lights must never dirty or rewrite the baked ground field. */
    DndArray *points=calloc(1,sizeof(*points)+20);assert(points);points->length=5;points->element_size=4;
    float *p=(float*)points->data;p[0]=2;p[1]=2.3f;p[2]=3;p[3]=4.5f;p[4]=255;
    uint8_t saved[SU_TERRAIN_BYTES];memcpy(saved,shadows->coverage,sizeof(saved));
    assert(animaltown_shadows_points(points,1)==1&&shadows->point_count==1);
    p[0]=5;assert(animaltown_shadows_points(points,1)==1&&!shadows->cloud_dirty&&!shadows->light_dirty);
    assert(!memcmp(saved,shadows->coverage,sizeof(saved)));
    int count;assert(animaltown_point_sources(&count)==shadows->points&&count==1);
    active=1;assert(animaltown_shadows_points(points,1)==0);active=0;
    p[3]=0;assert(animaltown_shadows_points(points,1)==0);p[3]=4.5f;
    p[0]=NAN;assert(animaltown_shadows_points(points,1)==0);p[0]=5;
    points->length=4;assert(animaltown_shadows_points(points,1)==0);points->length=5;
    points->elements_are_references=1;assert(animaltown_shadows_points(points,1)==0);points->elements_are_references=0;
    assert(animaltown_shadows_objects(3)==0&&animaltown_object_scope()==0);
    assert(animaltown_shadows_objects(2)==1&&animaltown_object_scope()==2);
    assert(animaltown_shadows_objects(0)==1&&animaltown_object_scope()==0);
    assert(animaltown_shadows_environment(0x596c91,-1)==0&&animaltown_shadow_strength()==255);
    assert(animaltown_shadows_environment(0x596c91,256)==0&&animaltown_environment_color()==0xffffff);
    assert(animaltown_shadows_environment(-1,0)==0);
    assert(animaltown_shadows_lights(points,0,255)==1&&animaltown_light_strength()==255);
    assert(animaltown_shadows_environment(0x596c91,0)==1&&animaltown_shadow_strength()==0);
    assert(!memcmp(saved,shadows->coverage,sizeof saved)&&!shadows->cloud_dirty&&!shadows->light_dirty);
    assert(animaltown_light_strength()==255); /* Disabling shadows must not disable lamps. */
    int before=invalidations;
    assert(animaltown_shadows_environment(0x596c91,0)==1&&invalidations==before);
    assert(animaltown_shadows_environment(0x596c91,128)==1&&invalidations==before+1);
    reset_count=invalidations=0;free(points);
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
    assert(!shadows->terrain_ready&&shadows->shapes[0]==255&&!shadows->point_count&&!shadows->object_scope);
    assert(animaltown_shadow_strength()==255&&animaltown_light_strength()==0&&animaltown_environment_color()==0xffffff);
    assert(animaltown_shadows_shutdown()==1&&!live&&reset_count==2);
    free(atlas);free(circle);free(cloud);
    puts("Shadow bridge lifecycle: charged buffers released, safe retries and clean town reconstruction passed");
    return 0;
}
