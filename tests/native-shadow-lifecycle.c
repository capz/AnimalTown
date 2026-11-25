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
    else{assert(bytes==32||bytes==SU_SNOW_BYTES||bytes==SU_SNOW_STRIP_BYTES);snow_flushes++;}
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
    assert(z/4>=snow_first_z&&z/4<snow_strip_past(snow_first_z,snow_texel_height));
    return snow_row(z/4,snow_first_z)[(x/8)*32+z%4*8+x%8];
}
static void set_snow_wear(DndArray *visits,int x,int z,int wear){
    int cell=z*SU_WORLD_WIDTH+x,shift=4*(cell%2);
    uint8_t *packed=(uint8_t*)visits->data;
    packed[cell/2]=(uint8_t)((packed[cell/2]&~(15<<shift))|(wear<<shift));
}
static int plain_snow(DndArray *visits,DndArray *tiles,DndArray *levels,int revision,int terrain_revision,int winter){
    return animaltown_shadows_snow(visits,tiles,levels,NULL,revision,terrain_revision,winter,-1,0,0,0,SU_SNOW_HEIGHT);
}
static int strip_snow(DndArray *visits,DndArray *tiles,DndArray *levels,int revision,int min_z,int max_z){
    return animaltown_shadows_snow(visits,tiles,levels,NULL,revision,0,1,-1,0,0,min_z,max_z);
}
static int visual_snow(DndArray *visits,DndArray *tiles,DndArray *levels,DndArray *stamps,int revision,int focus,int ms,int fall_ticks){
    return animaltown_shadows_snow(visits,tiles,levels,stamps,revision,0,1,focus,ms,fall_ticks,0,SU_SNOW_HEIGHT);
}
static int visual_depth(int cell){
    return snow_visual->depths[snow_visual->buffer][snow_local(cell,snow_visual->origin)];
}
static void smooth_snow_checks(DndArray *visits,DndArray *tiles,DndArray *levels){
    memset(visits->data,0,visits->length);
    const int x=7,z=41,focus=z*SU_WORLD_WIDTH+x,cell=focus+1,edge=(x+1)*4,row=z*4+1;
    set_snow_wear(visits,x,z,9);set_snow_wear(visits,x+1,z,6);
    assert(plain_snow(visits,tiles,levels,100,0,1)==1&&live==2&&!snow_visual);
    int covered=snow_sample(edge,row);
    assert(covered<191&&visual_snow(visits,tiles,levels,NULL,100,focus,0,0)==1);
    assert(visual_depth(cell)==6*SU_SNOW_UNIT);
    set_snow_wear(visits,x+1,z,9);
    assert(visual_snow(visits,tiles,levels,NULL,101,focus,10,0)==1);
    assert(visual_depth(cell)==6*SU_SNOW_UNIT);
    int before=snow_flushes;
    assert(visual_snow(visits,tiles,levels,NULL,101,focus,110,0)==1);
    int started=visual_depth(cell);
    assert(started>6*SU_SNOW_UNIT&&started<9*SU_SNOW_UNIT&&snow_flushes-before<=10);
    set_snow_wear(visits,x+1,z,12);
    assert(visual_snow(visits,tiles,levels,NULL,102,focus,120,0)==1);
    assert(visual_depth(cell)==started);
    assert(visual_snow(visits,tiles,levels,NULL,102,focus,220,0)==1);
    assert(visual_depth(cell)>started&&visual_depth(cell)<12*SU_SNOW_UNIT);
    assert(visual_snow(visits,tiles,levels,NULL,102,focus,420,0)==1);
    assert(visual_depth(cell)==12*SU_SNOW_UNIT);
    int open=snow_sample(edge,row);
    assert(open>=191);

    DndArray *stamps=pixels(4*SU_SNOW_PACKED);
    memset(stamps->data,0,stamps->length);stamps->length/=2;stamps->element_size=2;
    ((uint16_t*)stamps->data)[cell]=12;
    assert(snow_fine_wear(12,60*30)>3*SU_SNOW_UNIT);
    assert(snow_fine_wear(12,6*60*30)>3*SU_SNOW_UNIT);
    assert(snow_fine_wear(12,420*30)==3*SU_SNOW_UNIT);

    assert(visual_snow(visits,tiles,levels,stamps,102,focus,520,105)==1);
    assert(visual_depth(cell)<12*SU_SNOW_UNIT&&visual_depth(cell)>11*SU_SNOW_UNIT);
    before=snow_flushes;
    assert(visual_snow(visits,tiles,levels,stamps,102,focus,620,105)==1&&snow_flushes==before);
    set_snow_wear(visits,x+1,z,8);
    assert(visual_snow(visits,tiles,levels,stamps,103,focus,720,5950)==1);
    int mid=snow_sample(edge,row);
    assert(mid>covered&&mid<open);
    assert(visual_snow(visits,tiles,levels,stamps,103,focus+90,820,5950)==1);
    assert(snow_sample(edge,row)>=191&&live==2);
    assert(visual_snow(visits,tiles,levels,stamps,103,focus,920,5950)==1&&snow_sample(edge,row)==mid);
    assert(plain_snow(visits,tiles,levels,103,0,1)==1&&!snow_visual&&snow_sample(edge,row)>=191);
    assert(visual_snow(visits,tiles,levels,stamps,103,focus,930,5950)==1&&snow_sample(edge,row)==mid);
    assert(plain_snow(visits,tiles,levels,103,0,0)==1&&!snow_visual&&!animaltown_snow_mask());
    memset(visits->data,0,visits->length);
    free(stamps);
    puts("Smooth snow: timed steps, revisits, fractional recovery, scrolling and toggle teardown passed");
}
static void snow_joins_checks(DndArray *visits,DndArray *tiles,DndArray *levels){
    const int x=7,z=41,edge_x=(x+1)*4,edge_z=(z+1)*4,row=z*4+1,column=x*4+1;
    const int cell=z*SU_WORLD_WIDTH+x;
    uint8_t *terrain=(uint8_t*)tiles->data,*elevation=(uint8_t*)levels->data;
    set_snow_wear(visits,x,z,9);
    set_snow_wear(visits,x+1,z,9);
    assert(plain_snow(visits,tiles,levels,100,0,1)==1&&live==2);
    assert(snow_sample(edge_x-1,row)>=191&&snow_sample(edge_x,row)>=191);
    assert(snow_sample(x*4,row)<191&&snow_sample(edge_x+3,row)<191); /* Rounded outside caps. */
    assert(snow_sample(edge_x-1,z*4)<191&&snow_sample(edge_x,z*4)<191);

    int before=snow_flushes;
    set_snow_wear(visits,x+1,z,8);
    assert(plain_snow(visits,tiles,levels,101,0,1)==1);
    assert(snow_sample(edge_x-1,row)>=191&&snow_sample(edge_x,row)>=191);
    assert(snow_flushes-before<=10);
    set_snow_wear(visits,x+1,z,7);
    assert(plain_snow(visits,tiles,levels,102,0,1)==1);
    assert(snow_sample(edge_x-1,row)<191&&snow_sample(edge_x,row)<191);
    assert(snow_sample(column,row)>=191); /* Recovery closes the join before its neighbor. */

    set_snow_wear(visits,x+1,z,9);
    elevation[cell+1]=1;
    assert(plain_snow(visits,tiles,levels,103,1,1)==1);
    assert(snow_sample(edge_x-1,row)<191&&snow_sample(edge_x,row)<191);
    elevation[cell]=1;
    assert(plain_snow(visits,tiles,levels,103,2,1)==1);
    assert(snow_sample(edge_x-1,row)>=191&&snow_sample(edge_x,row)>=191);
    terrain[cell+1]=1;
    assert(plain_snow(visits,tiles,levels,103,3,1)==1);
    assert(snow_sample(edge_x-1,row)<191&&snow_sample(edge_x,row)<191);
    terrain[cell+1]=elevation[cell]=elevation[cell+1]=0;

    set_snow_wear(visits,x,z+1,9);
    assert(plain_snow(visits,tiles,levels,104,4,1)==1);
    assert(snow_sample(column,edge_z-1)>=191&&snow_sample(column,edge_z)>=191);
    assert(snow_sample(edge_x-1,row)>=191&&snow_sample(edge_x,row)>=191); /* Bend. */
    assert(snow_sample(edge_x-1,edge_z-1)<191);
    set_snow_wear(visits,x+1,z+1,9);
    assert(plain_snow(visits,tiles,levels,105,4,1)==1);
    for(int j=edge_z-1;j<=edge_z;j++)for(int i=edge_x-1;i<=edge_x;i++)assert(snow_sample(i,j)>=191);
    set_snow_wear(visits,x+1,z,0);
    set_snow_wear(visits,x,z+1,0);
    assert(plain_snow(visits,tiles,levels,106,4,1)==1);
    for(int j=edge_z-1;j<=edge_z;j++)for(int i=edge_x-1;i<=edge_x;i++)assert(snow_sample(i,j)<191);
    assert(plain_snow(visits,tiles,levels,107,4,0)==1);
    puts("Snow joins: both axes, rounded caps, bends, inner corners, diagonal separation, terrace and recovery passed");
}
static void snow_edge_checks(DndArray *visits,DndArray *tiles,DndArray *levels){
    assert(plain_snow(visits,tiles,levels,400,0,0)==1&&!snow_snapshot&&!snow_visual&&!snow_pixels);
    memset(visits->data,0,visits->length);memset(tiles->data,0,tiles->length);memset(levels->data,64,levels->length);
    int cell=40*SU_WORLD_WIDTH+20;
    ((uint8_t*)levels->data)[cell]=255; /* Clear material patch, same terrain ID. */
    assert(plain_snow(visits,tiles,levels,401,401,1)==1&&snow_snapshot&&snow_pixels);
    assert(snow_sample(19*4+3,40*4+1)==255&&snow_sample(19*4+2,40*4+1)==127);
    assert(snow_sample(20*4,40*4+1)==255); /* No filtered cap lip over the ramp toe. */
    assert(snow_sample(20*4+1,39*4+3)==255&&snow_sample(20*4+1,40*4)==255);
    assert(snow_sample(20*4+3,40*4+1)==255&&snow_sample(21*4,40*4+1)==255);
    assert(snow_sample(20*4+1,40*4+3)==255&&snow_sample(20*4+1,41*4)==255);
    assert((snow_sample(19*4+3,40*4+1)+snow_sample(19*4+2,40*4+1))/2==191);
    assert(animaltown_snow_mask_at(19.5f,40.5f,0)!=NULL);
    assert(animaltown_shadows_stats(17)==sizeof(SuSnowSnapshot));
    int uploads=snow_flushes;
    assert(plain_snow(visits,tiles,levels,401,401,1)==1&&snow_flushes==uploads);
    ((uint8_t*)levels->data)[cell]=64;
    assert(plain_snow(visits,tiles,levels,401,402,1)==1&&snow_sample(19*4+3,40*4+1)==0);
    for(int season=0;season<3;season++){
        assert(plain_snow(visits,tiles,levels,401,402,0)==1);
        assert(!snow_snapshot&&!snow_visual&&!snow_pixels&&animaltown_shadows_stats(14)==0&&animaltown_shadows_stats(15)==0&&animaltown_shadows_stats(17)==0);
        uploads=snow_flushes;
        for(int frame=0;frame<100;frame++)assert(plain_snow(visits,tiles,levels,401,402,0)==1);
        assert(snow_flushes==uploads&&!snow_snapshot);
    }
    memset(levels->data,0,levels->length);
    puts("Snow borders: clear grass-ID patches, quarter-metre cap cut, stable uploads and zero native buffers outside winter passed");
}
static void snow_strip_checks(DndArray *visits,DndArray *tiles,DndArray *levels){
    memset(visits->data,0,visits->length);
    set_snow_wear(visits,64,112,15);
    assert(strip_snow(visits,tiles,levels,200,0,56)==1&&!snow_pixels&&live==1);
    assert(strip_snow(visits,tiles,levels,200,64,120)==1&&snow_pixels&&live==2);
    assert(animaltown_shadows_stats(14)==SU_SNOW_STRIP_BYTES&&snow_sample(64*4+1,112*4+1)==255);
    assert(strip_snow(visits,tiles,levels,200,0,0)==1&&!snow_pixels&&live==1);
    assert(strip_snow(visits,tiles,levels,200,64,120)==1&&snow_sample(64*4+1,112*4+1)==255);
    assert(plain_snow(visits,tiles,levels,200,0,0)==1&&live==1);

    for(int z=0;z<SU_SNOW_HEIGHT;z++){
        int x=(z*3)%126;
        set_snow_wear(visits,x,z,8+z%8);
        set_snow_wear(visits,x+1,z,8+(z+1)%8);
        if(z+1<SU_SNOW_HEIGHT)set_snow_wear(visits,x,z+1,9);
    }
    assert(plain_snow(visits,tiles,levels,201,0,1)==1&&snow_texel_height==SU_SNOW_SIDE&&snow_first_z==0);
    uint8_t *full=malloc(SU_SNOW_BYTES);assert(full);memcpy(full,snow_pixels,SU_SNOW_BYTES);
    for(int route=0;route<2*(SU_SNOW_HEIGHT-56)+1;route++){
        int min_z=route<=SU_SNOW_HEIGHT-56?route:2*(SU_SNOW_HEIGHT-56)-route;
        int old_first=snow_first_z,before=texture_publications,flushes=snow_flushes;
        int was_strip=snow_texel_height==SU_SNOW_STRIP_TEXELS;
        assert(strip_snow(visits,tiles,levels,201,min_z,min_z+56)==1&&snow_pixels&&live==2);
        int published_first=-1;
        assert(animaltown_snow_layout(&published_first)==SU_SNOW_STRIP_TEXELS&&published_first==snow_first_z);
        assert(animaltown_shadows_stats(14)==SU_SNOW_STRIP_BYTES);
        if(was_strip&&old_first==snow_first_z)assert(texture_publications==before&&snow_flushes==flushes);
        else assert(texture_publications==before+1&&snow_flushes==flushes+1);
        int bytes=(snow_strip_past(snow_first_z,snow_texel_height)-snow_first_z)*SU_SNOW_ROW_BYTES;
        assert(!memcmp(snow_row(snow_first_z,snow_first_z),full+(snow_first_z+1)*SU_SNOW_ROW_BYTES,(size_t)bytes));
    }
    int before=texture_publications;
    set_snow_wear(visits,125,118,15);
    assert(strip_snow(visits,tiles,levels,202,0,56)==1&&texture_publications==before);
    assert(strip_snow(visits,tiles,levels,202,64,120)==1&&snow_sample(125*4+1,118*4+1)==255);
    release_error=1;
    assert(plain_snow(visits,tiles,levels,202,0,1)==0&&animaltown_shadows_stats(14)==SU_SNOW_STRIP_BYTES);
    release_error=0;create_error=1;
    assert(plain_snow(visits,tiles,levels,202,0,1)==0&&!snow_pixels&&live==1);
    create_error=0;
    assert(plain_snow(visits,tiles,levels,202,0,1)==1&&animaltown_shadows_stats(14)==SU_SNOW_BYTES);
    int focus=20+96*SU_WORLD_WIDTH;
    assert(visual_snow(visits,tiles,levels,NULL,202,focus,10,0)==1&&snow_visual);
    set_snow_wear(visits,20,96,12);
    assert(visual_snow(visits,tiles,levels,NULL,203,focus,110,0)==1);
    assert(visual_snow(visits,tiles,levels,NULL,203,focus,210,0)==1);
    memcpy(full,snow_pixels,SU_SNOW_BYTES);
    assert(animaltown_shadows_snow(visits,tiles,levels,NULL,203,0,1,focus,210,0,64,120)==1&&snow_visual);
    assert(animaltown_shadows_stats(15)==(int)sizeof(*snow_visual));
    assert(!memcmp(snow_row(snow_first_z,snow_first_z),full+(snow_first_z+1)*SU_SNOW_ROW_BYTES,
        (size_t)(snow_strip_past(snow_first_z,snow_texel_height)-snow_first_z)*SU_SNOW_ROW_BYTES));
    assert(strip_snow(visits,tiles,levels,203,0,0)==1&&!snow_pixels&&!snow_visual&&live==1);
    free(full);
    puts("Snow strip: 128 KiB, pixel parity on a round trip, sparse updates, fallback and same-revision resume passed");
}
static void town_snow_checks(void){
    DndArray *visits=pixels(SU_SNOW_PACKED);
    DndArray *tiles=pixels(SU_SNOW_PACKED*2),*levels=pixels(SU_SNOW_PACKED*2);
    memset(tiles->data,0,tiles->length);memset(levels->data,0,levels->length);
    uint8_t *field=(uint8_t*)visits->data;
    memset(field,0,SU_SNOW_PACKED);
    assert(plain_snow(visits,tiles,levels,0,0,1)==1&&!animaltown_snow_mask()&&live==1&&animaltown_shadows_stats(14)==0);
    const int exposed_texels[]={0,0,4,12,16};
    uint8_t previous[16]={0};
    int x=7,z=41,cell=z*SU_WORLD_WIDTH+x,shift=(cell%2)*4;
    field[cell/2]=(uint8_t)(1<<shift);
    create_error=1;assert(plain_snow(visits,tiles,levels,1,0,1)==0&&!snow_pixels);create_error=0;
    const int maximum_wear=SU_SNOW_MAX_VISITS*SU_SNOW_STEPS_PER_VISIT;
    for(int wear=1;wear<=maximum_wear;wear++){
        field[cell/2]=(uint8_t)(wear<<shift);
        int before=snow_flushes,exposed=0;
        assert(plain_snow(visits,tiles,levels,wear,0,1)==1&&live==2);
        assert(animaltown_shadows_stats(14)==SU_SNOW_BYTES);
        assert(animaltown_snow_mask_at(4,44,0)==snow_pixels);
        assert(animaltown_snow_mask_at(12,44,0)==snow_pixels);
        assert(animaltown_snow_mask_at(28,44,0)==NULL);
        assert(animaltown_snow_mask_at(28,44,1)==snow_pixels);

        assert(snow_flushes-before<=10+(wear==1)); // The initial allocation also flushes the full texture.
        before=snow_flushes;
        assert(plain_snow(visits,tiles,levels,wear,0,1)==1&&snow_flushes==before);
        for(int j=0;j<4;j++)for(int i=0;i<4;i++){
            uint8_t sample=snow_sample(x*4+i,z*4+j);
            assert(sample>=previous[j*4+i]);
            previous[j*4+i]=sample;
            exposed+=sample>=191;
            if(wear==maximum_wear)assert(sample==255);
        }
        assert(previous[5]>0&&(previous[5]>=191)==(wear>=8));
        if(wear%SU_SNOW_STEPS_PER_VISIT==0){
            int stage=wear/SU_SNOW_STEPS_PER_VISIT;
            assert(exposed==exposed_texels[stage-1]);
            if(stage==1)assert(previous[0]==0);
            if(stage==2)assert(previous[0]>0);
        }
    }
    assert(snow_sample((x-2)*4,z*4)==0&&snow_sample((x+2)*4,z*4)==0);
    assert(snow_sample((x+1)*4,z*4+1)>0&&snow_sample((x+1)*4,z*4+1)<191);
    assert(snow_sample((x+1)*4+1,z*4+1)==0);

    // An entirely travelled town has one bounded, continuous coverage field.
    memset(field,2*SU_SNOW_STEPS_PER_VISIT*0x11,SU_SNOW_PACKED);
    assert(plain_snow(visits,tiles,levels,maximum_wear+1,0,1)==1);
    for(int row=0;row<SU_SNOW_HEIGHT*4;row++)for(int column=0;column<SU_SNOW_SIDE;column++)
        assert(snow_sample(column,row)>0&&snow_sample(column,row)<191);
    memset(field,maximum_wear*0x11,SU_SNOW_PACKED);
    assert(plain_snow(visits,tiles,levels,maximum_wear+2,0,1)==1);
    for(int row=0;row<SU_SNOW_HEIGHT*4;row++)for(int column=0;column<SU_SNOW_SIDE;column++)
        assert(snow_sample(column,row)==255);
    int reserved=animaltown_shadows_stats(10);
    assert(reserved==(int)su_storage_bytes()+SU_SNOW_BYTES);
    memset(field,0,SU_SNOW_PACKED);
    release_error=1;
    assert(plain_snow(visits,tiles,levels,maximum_wear+3,0,1)==0&&live==2&&snow_sample(x*4+1,z*4+1)==255);
    release_error=0;
    assert(plain_snow(visits,tiles,levels,maximum_wear+3,0,1)==1&&!animaltown_snow_mask()&&live==1);
    assert(animaltown_shadows_stats(14)==0);
    assert(animaltown_shadows_stats(10)==(int)su_storage_bytes());
    // A new dent after complete recovery allocates and publishes clean storage.
    field[cell/2]=(uint8_t)(maximum_wear<<shift);
    assert(plain_snow(visits,tiles,levels,maximum_wear+4,0,1)==1&&live==2&&snow_sample(x*4+1,z*4+1)==255);
    assert(animaltown_snow_mask_at(28,44,0)==NULL);
    int adjacent=(z+1)*SU_WORLD_WIDTH+x+1;
    field[adjacent/2]|=(uint8_t)(maximum_wear<<((adjacent%2)*4));
    assert(plain_snow(visits,tiles,levels,maximum_wear+5,0,1)==1);
    field[cell/2]=0;
    assert(plain_snow(visits,tiles,levels,maximum_wear+6,0,1)==1&&animaltown_snow_mask_at(4,44,0)==snow_pixels);
    memset(field,0,SU_SNOW_PACKED);
    const int far_cell=90*SU_WORLD_WIDTH+100;
    field[far_cell/2]=(uint8_t)(maximum_wear<<((far_cell%2)*4));
    assert(plain_snow(visits,tiles,levels,maximum_wear+7,0,1)==1&&animaltown_snow_mask_at(4,44,0)==NULL);
    // Include stamps on every side of page/world borders, then verify actual
    // sampled texels (including filter/relief guard) for every cheap page.
    const int border_x[]={0,7,8,15,16,126,127},border_z[]={0,7,8,15,16,118,119};
    for(unsigned i=0;i<sizeof(border_x)/sizeof(*border_x);i++){
        int at=border_z[i]*SU_WORLD_WIDTH+border_x[i];
        field[at/2]|=(uint8_t)(maximum_wear<<((at%2)*4));
    }
    assert(plain_snow(visits,tiles,levels,maximum_wear+8,0,1)==1);
    int cheap_pages=0;
    for(int pz=0;pz<SU_SNOW_PAGE_ROWS;pz++)for(int px=0;px<SU_WORLD_WIDTH/SU_SNOW_PAGE;px++){
        if(animaltown_snow_mask_at(px*8+4,pz*8+4,0))continue;
        cheap_pages++;
        for(int tz=pz*32-1;tz<=(pz+1)*32;tz++)for(int tx=px*32-1;tx<=(px+1)*32;tx++)
            if(tx>=0&&tx<SU_SNOW_SIDE&&tz>=0&&tz<SU_SNOW_HEIGHT*4)assert(snow_sample(tx,tz)==0);
    }
    assert(cheap_pages>0);
    release_error=1;assert(plain_snow(visits,tiles,levels,maximum_wear+8,0,0)==0&&live==2);release_error=0;
    assert(plain_snow(visits,tiles,levels,maximum_wear+8,0,0)==1&&!animaltown_snow_mask()&&live==1);
    snow_joins_checks(visits,tiles,levels);
    smooth_snow_checks(visits,tiles,levels);
    snow_edge_checks(visits,tiles,levels);
    snow_strip_checks(visits,tiles,levels);
    free(visits);free(tiles);free(levels);
    printf("Town snow: %d cells, five wear levels, %d charged bytes in winter. Ordinary shadows use %zu bytes\n",
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
