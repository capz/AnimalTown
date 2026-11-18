/* Exercise charged shadow and snow storage, including session teardown. */
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include "dnd_draw.h"
#include "dnd_timing.h"
#include "../tools/native-shadow-union.h"

static void *allocations[2];
static int allocation_bytes[2];
static int allocated,live,active,release_error,create_error,reset_count,invalidations;
static int field_flushes,page_flushes,snow_flushes,texture_publications;
bool dnd_device_active(void){return active!=0;}
int dnd_buffer_create(int bytes,int alignment){
    assert(live<2&&alignment==32);
    if(create_error)return 0;
    int slot=allocations[0]?1:0;
    allocations[slot]=aligned_alloc((size_t)alignment,(size_t)bytes);assert(allocations[slot]);
    memset(allocations[slot],0,(size_t)bytes);
    allocation_bytes[slot]=bytes;allocated+=bytes;live++;
    return 101+slot;
}
uint64_t dnd_buffer_address(int token,int offset){
    assert(token>=101&&token<=102&&offset==0&&allocations[token-101]);
    return (uintptr_t)allocations[token-101];
}
int dnd_draw_release(int token){
    assert(token>=101&&token<=102&&allocations[token-101]&&!active);
    if(release_error)return -1;
    int slot=token-101;
    free(allocations[slot]);allocations[slot]=NULL;allocated-=allocation_bytes[slot];live--;
    return 0;
}
int dnd_draw_live(void){return live;}
int dnd_draw_total_bytes(void){return allocated;}
void dnd_draw_backend_flush(void *data,int bytes,int invalidate){
    assert(data&&!invalidate);
    if(bytes==SU_TERRAIN_BYTES)field_flushes++;
    else if(bytes==SU_BYTES)page_flushes++;
    else{assert(bytes==32||bytes==SU_SNOW_BYTES);snow_flushes++;}
}
void dnd_draw_backend_publish_texture(void){texture_publications++;}
void dnd_draw_backend_invalidate_cache(void){invalidations++;}
void animaltown_shadows_model_reset(void){reset_count++;}
int64_t dnd_timing_now(void){return 0;}
#include "../tools/native-shadow-union.c"

static DndArray *pixels(int count){
    DndArray *a=calloc(1,sizeof(*a)+(size_t)count);assert(a);
    a->length=(uint32_t)count;a->element_size=1;memset(a->data,255,(size_t)count);return a;
}

static void light_gain_checks(void){
    const float quad[]={0,0,8,0,8,8,0,0,8,8,0,8},bounds[]={1,1,7,7};
    DndArray *sources=calloc(1,sizeof(*sources)+4*sizeof(float));assert(sources);
    sources->length=4;sources->element_size=sizeof(float);
    float *light=(float*)sources->data;
    light[0]=4.5f;light[1]=4.5f;light[2]=2.8f;light[3]=255;
    assert(animaltown_shadows_lights(sources,1,255)==1);
    su_mesh(shadows,SU_STATIC,6,4,quad,bounds);su_active(shadows,SU_STATIC,1,4,4);
    assert(animaltown_shadows_prepare(1000)==1&&animaltown_shadows_prepare(1000)==1);
    int page=su_find(shadows,0,0);assert(page>=0);
    uint8_t terrain[SU_TERRAIN_BYTES],coverage[SU_TERRAIN_BYTES],actor_page[SU_BYTES];
    memcpy(terrain,shadows->terrain,sizeof(terrain));
    memcpy(coverage,shadows->coverage,sizeof(coverage));
    memcpy(actor_page,shadows->pixels+page*SU_BYTES,sizeof(actor_page));
    assert(su_field_channel(coverage,4.5f,4.5f,1)==255);
    unsigned serial=shadows->serial;
    int fields=field_flushes,pages=page_flushes,snow_uploads=snow_flushes,publications=texture_publications;
    for(int gain=0;gain<=255;gain++){
        assert(animaltown_shadows_begin(0,1,0)==1);su_active(shadows,SU_STATIC,1,4,4);
        assert(animaltown_shadows_lights(sources,1,gain)==1);
        assert(animaltown_shadows_lights(NULL,0,gain)==1&&animaltown_light_strength()==gain);
        assert(!shadows->light_dirty&&!shadows->cloud_dirty&&!shadows->page[page].dirty);
        assert(animaltown_shadows_prepare(1000)==1&&shadows->serial==serial);
        assert(!shadows->changed&&!shadows->static_rasters&&!shadows->dynamic_rasters);
        assert(field_flushes==fields&&page_flushes==pages&&snow_flushes==snow_uploads&&texture_publications==publications);
        assert(!memcmp(terrain,shadows->terrain,sizeof(terrain))&&!memcmp(coverage,shadows->coverage,sizeof(coverage)));
        assert(!memcmp(actor_page,shadows->pixels+page*SU_BYTES,sizeof(actor_page)));
    }
    assert(animaltown_shadows_lights(NULL,1,0)==0&&animaltown_shadows_lights(NULL,0,256)==0);
    active=1;assert(animaltown_shadows_lights(NULL,0,0)==0);active=0;
    assert(animaltown_light_strength()==255);

    /* A room can rebuild its own actor page while keeping the outdoor layout. */
    assert(animaltown_shadows_begin(8,1,4)==1);
    su_mesh(shadows,SU_STATIC,6,4,quad,bounds);su_active(shadows,SU_STATIC,1,4,4);
    assert(animaltown_shadows_lights(NULL,0,0)==1);
    assert(animaltown_shadows_prepare(1000)==1&&animaltown_shadows_prepare(1000)==1);
    int indoor=su_find(shadows,0,0);assert(indoor>=0);
    for(int i=0;i<SU_BYTES;i++)assert(!(shadows->pixels[indoor*SU_BYTES+i]>>4));
    assert(animaltown_shadows_begin(0,1,0)==1);
    su_mesh(shadows,SU_STATIC,6,4,quad,bounds);su_active(shadows,SU_STATIC,1,4,4);
    assert(animaltown_shadows_lights(NULL,0,255)==1);
    assert(animaltown_shadows_prepare(1000)==1&&animaltown_shadows_prepare(1000)==1);
    assert(field_flushes==fields&&!memcmp(terrain,shadows->terrain,sizeof(terrain))&&!memcmp(coverage,shadows->coverage,sizeof(coverage)));

    /* A moved source refreshes coverage exactly once and preserves the actor shadow. */
    light[0]=14.5f;publications=texture_publications;
    assert(animaltown_shadows_lights(sources,1,255)==1&&shadows->light_dirty&&shadows->cloud_dirty);
    assert(animaltown_shadows_prepare(1000)==1&&!shadows->light_dirty&&!shadows->cloud_dirty);
    assert(field_flushes==fields+1&&texture_publications==publications+1);
    assert(su_field_channel(shadows->coverage,4.5f,4.5f,1)==0);
    assert(su_field_channel(shadows->coverage,14.5f,4.5f,1)==255);
    page=su_find(shadows,0,0);assert(page>=0);
    for(int i=0;i<SU_BYTES;i++)assert((shadows->pixels[page*SU_BYTES+i]&15)==(actor_page[i]&15));
    assert(animaltown_shadows_lights(sources,1,128)==1&&animaltown_shadows_prepare(1000)==1);
    assert(field_flushes==fields+1&&texture_publications==publications+1);
    free(sources);
    puts("Shadow bridge lighting: 256 gain values, room transitions and one layout change passed");
}

static uint8_t snow_sample(int x,int z){
    return snow_pixels[((z/4+1)*SU_SNOW_BLOCKS+x/8)*32+z%4*8+x%8];
}
static void town_snow_checks(void){
    DndArray *visits=pixels(SU_SNOW_PACKED);
    uint8_t *field=(uint8_t*)visits->data;
    memset(field,0,SU_SNOW_PACKED);
    assert(animaltown_shadows_snow(visits,0,1)==1&&!animaltown_snow_mask()&&live==1);
    int x=7,z=41,cell=z*SU_WORLD_WIDTH+x,shift=(cell%4)*2;
    field[cell/4]=(uint8_t)(1<<shift);
    create_error=1;assert(animaltown_shadows_snow(visits,1,1)==0&&!snow_pixels);create_error=0;
    assert(animaltown_shadows_snow(visits,1,1)==1&&live==2);
    assert(snow_sample(x*4+1,z*4+1)>0&&snow_sample(x*4+1,z*4+1)<191);
    assert(snow_sample((x-2)*4,z*4)==0&&snow_sample((x+2)*4,z*4)==0);
    int before=snow_flushes;
    assert(animaltown_shadows_snow(visits,1,1)==1&&snow_flushes==before);
    field[cell/4]=(uint8_t)(2<<shift);
    assert(animaltown_shadows_snow(visits,2,1)==1&&snow_flushes-before<=13);
    assert(snow_sample(x*4+1,z*4+1)>=191&&snow_sample(x*4,z*4)<191);
    field[cell/4]=(uint8_t)(3<<shift);
    assert(animaltown_shadows_snow(visits,3,1)==1);
    for(int j=0;j<4;j++)for(int i=0;i<4;i++)assert(snow_sample(x*4+i,z*4+j)==255);
    assert(snow_sample((x+1)*4,z*4+1)>0&&snow_sample((x+1)*4,z*4+1)<191);
    assert(snow_sample((x+1)*4+1,z*4+1)==0);

    // An entirely travelled town has one bounded, continuous coverage field.
    memset(field,255,SU_SNOW_PACKED);
    assert(animaltown_shadows_snow(visits,4,1)==1);
    for(int row=0;row<SU_SNOW_HEIGHT*4;row++)for(int column=0;column<SU_SNOW_SIDE;column++)
        assert(snow_sample(column,row)==255);
    int reserved=animaltown_shadows_stats(10);
    assert(reserved==(int)su_storage_bytes()+SU_SNOW_BYTES);
    memset(field,0,SU_SNOW_PACKED);
    assert(animaltown_shadows_snow(visits,5,1)==1&&snow_sample(x*4+1,z*4+1)==0);
    release_error=1;assert(animaltown_shadows_snow(visits,5,0)==0&&live==2);release_error=0;
    assert(animaltown_shadows_snow(visits,5,0)==1&&!animaltown_snow_mask()&&live==1);
    free(visits);
    printf("Town snow: %d cells, three wear levels, %d charged bytes in winter; ordinary shadows use %zu bytes\n",
           SU_WORLD_WIDTH*SU_SNOW_HEIGHT,reserved,su_storage_bytes());
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
    assert(animaltown_shadows_scope(4)==4);
    assert(animaltown_shadows_page(2,2,origin)==shadows->coverage);
    assert(animaltown_shadows_scope(1)==1);
    assert(su_terrain_sample(shadows,2,2)==su_shadow(su_pack(33,0))); /* One texel/m, including the gutter. */
    light_gain_checks();
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
    /* NULL updates gain only; a non-NULL empty list explicitly clears layout. */
    assert(animaltown_shadows_lights(NULL,0,255)==1&&animaltown_light_strength()==255);
    assert(shadows->light_count==1);
    assert(animaltown_shadows_environment(0x596c91,0)==1&&animaltown_shadow_strength()==0);
    assert(!memcmp(saved,shadows->coverage,sizeof saved)&&!shadows->cloud_dirty&&!shadows->light_dirty);
    assert(animaltown_light_strength()==255); /* Disabling shadows must not disable lamps. */
    int before=invalidations;
    assert(animaltown_shadows_environment(0x596c91,0)==1&&invalidations==before);
    assert(animaltown_shadows_environment(0x596c91,128)==1&&invalidations==before+1);
    town_snow_checks();
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
