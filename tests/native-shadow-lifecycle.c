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
static int field_flushes,page_flushes,snow_flushes,texture_publications;
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
void dnd_draw_backend_flush(void *data,int bytes,int invalidate){
    assert(data&&!invalidate);
    if(bytes==SU_TERRAIN_BYTES)field_flushes++;
    else if(bytes==SU_BYTES)page_flushes++;
    else{assert(bytes==2*SU_BYTES||bytes==2*SU_TERRAIN_BYTES);snow_flushes++;}
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

/* Town.Height in the managed domain. Treat every tile as a snow receiver
 * here to exercise the full field, including where a real town has water. */
enum { TOWN_HEIGHT=120, PAGE_SPAN=8, PAGE_COLUMNS=SU_WORLD_WIDTH/PAGE_SPAN,
       PAGE_ROWS=TOWN_HEIGHT/PAGE_SPAN, TOWN_PAGES=PAGE_COLUMNS*PAGE_ROWS };

static unsigned rgb565_depth(const uint8_t *rgb,const uint8_t *ia4,int x,int z,int width){
    int offset=su_rgb565_offset(x,z,width);
    unsigned texel=((unsigned)rgb[offset]<<8)|rgb[offset+1];
    unsigned source=width==SU_SIDE?ia4[su_offset(x,z)]:ia4[su_terrain_offset(x,z)];
    assert((texel>>11)==((source&15)*31+7)/15);
    assert((texel&31)==((source>>4)*31+7)/15);
    return (texel>>5)&63;
}

static void scan_town(bool *seen,int expected_detailed){
    int detailed=0;
    assert(animaltown_shadows_scope(4)==4);
    for(int pz=0;pz<PAGE_ROWS;pz++)for(int px=0;px<PAGE_COLUMNS;px++){
        float origin[6];
        const uint8_t *rgb=animaltown_shadows_page(px*PAGE_SPAN+4,pz*PAGE_SPAN+4,origin);
        assert(rgb);
        if(origin[3]==SU_SIDE){
            int slot=su_find(shadows,px,pz);
            assert(slot>=0&&rgb==snow_pixels+2*slot*SU_BYTES);
            const uint8_t *ia4=shadows->pixels+slot*SU_BYTES;
            assert(origin[0]==px*PAGE_SPAN&&origin[1]==pz*PAGE_SPAN);
            for(int z=0;z<SU_SIDE;z++)for(int x=0;x<SU_SIDE;x++){
                float local_x=(x-.5f)*PAGE_SPAN/SU_INNER;
                float local_z=(z-.5f)*PAGE_SPAN/SU_INNER;
                /* Pages share an exact 8 m repeat. Use local coordinates
                 * to avoid world-coordinate rounding at quantization ties. */
                assert(rgb565_depth(rgb,ia4,x,z,SU_SIDE)==su_snow_depth(local_x,local_z));
            }
            detailed++;
            seen[pz*PAGE_COLUMNS+px]=true;
        }else{
            assert(rgb==snow_coverage()&&origin[3]==SU_TERRAIN_WIDTH&&origin[5]==SU_TERRAIN_HEIGHT);
            for(int z=pz*PAGE_SPAN;z<(pz+1)*PAGE_SPAN;z++)for(int x=px*PAGE_SPAN;x<(px+1)*PAGE_SPAN;x++)
                assert(rgb565_depth(rgb,shadows->coverage,x+1,z+1,SU_TERRAIN_WIDTH)==su_snow_depth(x+.5f,z+.5f));
        }
    }
    assert(detailed==expected_detailed);
}

static void town_snow_checks(void){
    enum { JUNCTION_COLUMNS=PAGE_COLUMNS/2, JUNCTIONS=JUNCTION_COLUMNS*((PAGE_ROWS+1)/2) };
    bool seen[TOWN_PAGES]={false};
    DndArray *uv=pixels(12*sizeof(float)),*bounds=pixels(4*sizeof(float)),*center=pixels(2*sizeof(float));
    DndArray *lamps=pixels(8*sizeof(float));
    uv->element_size=bounds->element_size=center->element_size=sizeof(float);
    uv->length=12;bounds->length=4;center->length=2;
    /* The preceding gain fixture deliberately keeps an actor shadow alive. */
    ((float*)center->data)[0]=((float*)center->data)[1]=0;
    for(int slot=0;slot<SU_CASTERS;slot++)assert(animaltown_shadows_active(slot,0,center)==1);
    lamps->element_size=sizeof(float);lamps->length=8;
    const float sources[]={6.5f,4.5f,4,255,118.5f,116.5f,4,255};
    memcpy(lamps->data,sources,sizeof(sources));
    assert(animaltown_shadows_lights(lamps,2,255)==1);
    free(lamps);
    assert(animaltown_shadows_prepare(1000)==1);
    assert(su_field_channel(shadows->coverage,6.5f,4.5f,1)==255);
    assert(su_field_channel(shadows->coverage,118.5f,116.5f,1)==255);
    const float square[]={-1,-1,1,-1,1,1,-1,-1,1,1,-1,1};
    const int reserved=animaltown_shadows_stats(10);
    scan_town(seen,0);
    for(int first=0;first<JUNCTIONS;first+=SU_CASTERS){
        assert(animaltown_shadows_begin(0,1,0)==1);
        int count=JUNCTIONS-first;
        if(count>SU_CASTERS)count=SU_CASTERS;
        for(int slot=0;slot<SU_CASTERS;slot++){
            int junction=first+slot;
            float x=PAGE_SPAN*(2*(junction%JUNCTION_COLUMNS)+1);
            float z=PAGE_SPAN*(2*(junction/JUNCTION_COLUMNS)+1);
            float b[]={x-1,z-1,x+1,z+1},c[]={x,z},verts[12];
            for(int i=0;i<12;i++)verts[i]=square[i]+c[i%2];
            memcpy(uv->data,verts,sizeof(verts));
            memcpy(bounds->data,b,sizeof(b));
            memcpy(center->data,c,sizeof(c));
            assert(animaltown_shadows_mesh(slot,slot<count?6:0,slot<SU_STATIC?0:4,uv,bounds)==1);
            assert(animaltown_shadows_active(slot,slot<count,center)==1);
        }
        /* Let the normal one-static-page-per-frame bridge publish every page.
         * The fixture clock gives deterministic time to the raster work. */
        for(int frame=0;frame<=count*4;frame++){
            assert(animaltown_shadows_prepare(1000)==1);
            assert(animaltown_shadows_stats(9)==1&&animaltown_shadows_stats(10)==reserved);
        }
        assert(animaltown_shadows_stats(0)==count*4);
        scan_town(seen,first==0?count*4:TOWN_PAGES-first*4);
        if(first==0){
            float origin[6];
            assert(animaltown_shadows_scope(5)==5);
            assert(animaltown_shadows_page(4,4,origin)==snow_coverage());
            assert(origin[3]==SU_TERRAIN_WIDTH&&origin[5]==SU_TERRAIN_HEIGHT);
        }
    }
    for(int page=0;page<TOWN_PAGES;page++)assert(seen[page]);
    free(uv);free(bounds);free(center);
    printf("Town snow: all %d cells verified on fallback, all %d regions visited with detail, %d detailed pages at peak\n",
           SU_WORLD_WIDTH*TOWN_HEIGHT,TOWN_PAGES,SU_CASTERS*4);
    printf("Town snow memory: %zu bytes IA4 baseline, %d bytes charged in one buffer (+%d), %zu bytes of static depth templates\n",
           su_storage_bytes(),reserved,SU_SNOW_BYTES,sizeof(snow_page_depth)+sizeof(snow_field_depth));
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
    assert(animaltown_shadows_stats(10)==(int)su_storage_bytes()+SU_SNOW_BYTES);
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
    const uint8_t *rgb=animaltown_shadows_page(2,2,origin);
    assert(rgb==snow_coverage());
    int x=6,z=5,at=su_rgb565_offset(x,z,SU_TERRAIN_WIDTH);
    unsigned texel=(unsigned)rgb[at]*256+rgb[at+1];
    unsigned ia4=shadows->coverage[su_terrain_offset(x,z)];
    assert((texel>>11)==((ia4&15)*31+7)/15);
    assert((texel&31)==((ia4>>4)*31+7)/15);
    assert(((texel>>5)&63)==su_snow_depth(x-.5f,z-.5f)&&((texel>>5)&63)>0);
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
