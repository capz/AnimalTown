#include "dnd_draw.h"
#include "dnd_model.h"
#ifndef ANIMALTOWN_SHADOW_UNION_H
#include "animaltown_shadow_union.h"
#endif
#include "dnd_timing.h"
#include <stdlib.h>
#ifdef GEKKO
#include <ogc/system.h>
#include <malloc.h>
#endif
static SuState *shadows;
static int shadow_storage;
static int snow_storage,snow_revision=-1,snow_terrain_revision=-1;
static uint32_t snow_publication;
static uint8_t snow_visits[SU_SNOW_PACKED];
static uint8_t *snow_pixels;
#define SNOW_STEP_MS 300u
#define SNOW_SAMPLE_MS 100u
/* Match the shared SnowWear clock and recovery checkpoint. */
#define SNOW_TICKS_PER_SECOND 30
#define SNOW_REFILL_TICKS (60*SNOW_TICKS_PER_SECOND)
#define SNOW_STAMP_CLOCK_MASK 4095
#define SNOW_RECENT_STEPS 64

typedef struct {
    uint32_t started;
    uint16_t cell;
    uint8_t from;
} SuSnowStep;
typedef struct {
    uint8_t depths[2][SU_SNOW_VISUAL_BYTES];
    SuSnowStep steps[SNOW_RECENT_STEPS];
    int origin,buffer,count,revision,fall_ticks;
    uint32_t sampled;
} SuSnowVisual;
static SuSnowVisual *snow_visual;

const uint8_t *animaltown_snow_mask(void){return snow_pixels;}
static void snow_release_visual(void){free(snow_visual);snow_visual=NULL;}
static int snow_release(void){
    if(snow_storage&&dnd_draw_release(snow_storage)<0)return 0;
    snow_storage=0;snow_pixels=NULL;snow_revision=snow_terrain_revision=-1;
    memset(snow_visits,0,sizeof(snow_visits));
    snow_release_visual();
    return 1;
}

/* Size matters for the shared MEM1 arena, including while the preview is off. */
#if defined(__GNUC__) && !defined(__clang__)
#pragma GCC push_options
#pragma GCC optimize ("Os")
#endif
static int snow_array(const DndArray *array,unsigned length,unsigned size){
    return array&&!array->elements_are_references&&array->element_size==size&&array->length==length;
}
static int snow_origin(int focus){
    int x=focus%SU_WORLD_WIDTH-SU_SNOW_VISUAL_SIDE/2;
    int z=focus/SU_WORLD_WIDTH-SU_SNOW_VISUAL_SIDE/2;
    if(x<0)x=0;
    if(x>SU_WORLD_WIDTH-SU_SNOW_VISUAL_SIDE)x=SU_WORLD_WIDTH-SU_SNOW_VISUAL_SIDE;
    if(z<0)z=0;
    if(z>SU_SNOW_HEIGHT-SU_SNOW_VISUAL_SIDE)z=SU_SNOW_HEIGHT-SU_SNOW_VISUAL_SIDE;
    return z*SU_WORLD_WIDTH+x;
}
static int snow_local(int cell,int origin){
    int x=cell%SU_WORLD_WIDTH-origin%SU_WORLD_WIDTH,z=cell/SU_WORLD_WIDTH-origin/SU_WORLD_WIDTH;
    return (unsigned)x<SU_SNOW_VISUAL_SIDE&&(unsigned)z<SU_SNOW_VISUAL_SIDE?z*SU_SNOW_VISUAL_SIDE+x:-1;
}
static int snow_dirty(uint64_t *dirty,int x,int z){
    uint64_t block=UINT64_C(1)<<x,blocks=block|(block<<1)|(block>>1);
    dirty[z]|=blocks;dirty[z+1]|=blocks;
    if(z>0)dirty[z-1]|=blocks;
    return 1;
}
static int snow_dirty_visual(uint64_t *dirty,const SuSnowField *previous,const SuSnowField *next,int origin){
    int changed=0,left=origin%SU_WORLD_WIDTH,top=origin/SU_WORLD_WIDTH;
    for(int z=top;z<top+SU_SNOW_VISUAL_SIDE;z++)for(int x=left;x<left+SU_SNOW_VISUAL_SIDE;x++)
        if(su_snow_wear(previous,x,z)!=su_snow_wear(next,x,z))changed=snow_dirty(dirty,x/2,z);
    return changed;
}
static void snow_footstep(SuSnowVisual *visual,const SuSnowField *previous,int x,int z,int origin,uint32_t now){
    int cell=z*SU_WORLD_WIDTH+x;
    if(snow_local(cell,origin)<0)return;
    int i=0;
    while(i<visual->count&&visual->steps[i].cell!=cell)i++;
    if(i==SNOW_RECENT_STEPS)return; /* Bulk edits still show the correct final depth. */
    if(i==visual->count)visual->count++;
    visual->steps[i]=(SuSnowStep){now,(uint16_t)cell,(uint8_t)su_snow_wear(previous,x,z)};
}
static int snow_fine_wear(int stamp,int fall_ticks){
    const int floor=SU_SNOW_STEPS_PER_VISIT*SU_SNOW_UNIT;
    int seconds=fall_ticks/SNOW_TICKS_PER_SECOND;
    int age=(seconds-(stamp>>4))&SNOW_STAMP_CLOCK_MASK;
    int left=SNOW_REFILL_TICKS-age*SNOW_TICKS_PER_SECOND-fall_ticks%SNOW_TICKS_PER_SECOND;
    if(left<=0)return floor;
    int excess=(stamp&15)*SU_SNOW_UNIT-floor;
    return floor+(excess*left+SNOW_REFILL_TICKS/2)/SNOW_REFILL_TICKS;
}
static uint8_t *snow_sample_visual(SuSnowVisual *visual,const SuSnowField *next,const uint16_t *stamps,int origin,uint32_t now,int fall_ticks){
    uint8_t *depths=visual->depths[visual->buffer^1];
    for(int z=0;z<SU_SNOW_VISUAL_SIDE;z++)for(int x=0;x<SU_SNOW_VISUAL_SIDE;x++){
        int cell=origin+z*SU_WORLD_WIDTH+x,stamp=stamps?stamps[cell]:0;
        int wear=((next->packed[cell/2]>>((cell%2)*4))&15)*SU_SNOW_UNIT;
        depths[z*SU_SNOW_VISUAL_SIDE+x]=(uint8_t)(stamp?snow_fine_wear(stamp,fall_ticks):wear);
    }
    for(int i=visual->count-1;i>=0;i--){
        SuSnowStep step=visual->steps[i];
        uint32_t age=now-step.started;
        if(age>=SNOW_STEP_MS){visual->steps[i]=visual->steps[--visual->count];continue;}
        int at=snow_local(step.cell,origin);
        if(at<0)continue;
        int eased=(int)(age*age*(3*SNOW_STEP_MS-2*age)/SNOW_STEP_MS);
        depths[at]=(uint8_t)(step.from+(depths[at]-step.from)*eased/(int)(SNOW_STEP_MS*SNOW_STEP_MS));
    }
    return depths;
}
#if defined(__GNUC__) && !defined(__clang__)
__attribute__((noinline))
#endif
int animaltown_shadows_snow(DndArray *visits,DndArray *tiles,DndArray *levels,DndArray *recovery,int revision,int terrain_revision,int winter,int focus,int elapsed_ms,int fall_ticks){
    if(!shadows||dnd_device_active())return 0;
    if(!winter)return (snow_revision<0&&!snow_visual)||snow_release();
    if(!snow_array(visits,SU_SNOW_PACKED,1)||!snow_array(tiles,SU_SNOW_PACKED*2,1)||!snow_array(levels,SU_SNOW_PACKED*2,1))return 0;
    int enabled=focus>=0;
    if(enabled){
        if(focus>=SU_SNOW_PACKED*2||(recovery&&!snow_array(recovery,SU_SNOW_PACKED*2,2)))return 0;
        if(!snow_visual){
            snow_visual=calloc(1,sizeof(*snow_visual));
            if(snow_visual)snow_visual->origin=-1;
        }
        enabled=snow_visual!=NULL;
    }
    SuSnowVisual *visual=snow_visual;
    int old_origin=visual?visual->origin:-1,origin=enabled?snow_origin(focus):-1;
    uint32_t now=(uint32_t)elapsed_ms;
    int terrain_changed=snow_terrain_revision!=terrain_revision;
    int packed_changed=snow_revision!=revision;
    int sample=enabled&&(old_origin<0||((now-visual->sampled>=SNOW_SAMPLE_MS)&&
        (origin!=old_origin||visual->revision!=revision||visual->fall_ticks!=fall_ticks||visual->count)));
    if(!packed_changed&&!terrain_changed&&!sample&&(enabled||old_origin<0))return 1;
    SuSnowField previous={snow_visits,old_origin>=0?visual->depths[visual->buffer]:NULL,old_origin};
    SuSnowField next={(const uint8_t*)visits->data,enabled?previous.visual:NULL,enabled?old_origin:-1};
    uint64_t dirty[SU_SNOW_HEIGHT+1]={0};
    int changed=0;
    if(packed_changed||terrain_changed)for(int z=0;z<SU_SNOW_HEIGHT;z++)for(int x=0;x<SU_SNOW_BLOCKS;x++){
        int at=z*SU_SNOW_BLOCKS+x,before=snow_visits[at],after=next.packed[at];
        if(before==after&&(!terrain_changed||!after))continue;
        if(enabled&&old_origin>=0&&before!=after){
            if((after&15)>(before&15))snow_footstep(visual,&previous,x*2,z,origin,now);
            if((after>>4)>(before>>4))snow_footstep(visual,&previous,x*2+1,z,origin,now);
        }
        changed=snow_dirty(dirty,x,z);
    }
    if(sample){
        next.origin=origin;
        next.visual=snow_sample_visual(visual,&next,recovery?(const uint16_t*)recovery->data:NULL,origin,now,fall_ticks);
    }
    if(sample||!enabled){
        if(old_origin>=0&&old_origin!=next.origin)changed|=snow_dirty_visual(dirty,&previous,&next,old_origin);
        if(next.origin>=0)changed|=snow_dirty_visual(dirty,&previous,&next,next.origin);
    }
    if(changed){
        if(!snow_storage){
            snow_storage=dnd_buffer_create(SU_SNOW_BYTES,32);
            if(!snow_storage){
                if(enabled){
                    snow_release_visual();
                    return animaltown_shadows_snow(visits,tiles,levels,NULL,revision,terrain_revision,winter,-1,0,0);
                }
#ifdef GEKKO
                struct mallinfo memory=mallinfo();
                SYS_Report("ANIMALTOWN SNOW ALLOCATION FAILED bytes %u arena_free %u malloc_free %u native_bytes %u\n",(unsigned)SU_SNOW_BYTES,(unsigned)SYS_GetArena1Size(),(unsigned)memory.fordblks,(unsigned)dnd_draw_total_bytes());
#endif
                return 0;
            }
            snow_pixels=(uint8_t*)(uintptr_t)dnd_buffer_address(snow_storage,0);
            if(!snow_pixels){snow_release();return 0;}
            dnd_draw_backend_flush(snow_pixels,SU_SNOW_BYTES,0);
        }
        for(int z=0;z<=SU_SNOW_HEIGHT;z++)for(int x=0;x<SU_SNOW_BLOCKS;x++)if(dirty[z]&(UINT64_C(1)<<x)){
            uint8_t *dest=snow_pixels+((z+1)*SU_SNOW_BLOCKS+x)*32;
            su_snow_block(dest,&next,(const uint8_t*)tiles->data,(const uint8_t*)levels->data,x,z);
            dnd_draw_backend_flush(dest,32,0);
        }
        /* Dolphin samples sparse texture hashes. Mark an unused padded row. */
        uint32_t publication=++snow_publication;
        memcpy(snow_pixels,&publication,sizeof(publication));
        dnd_draw_backend_flush(snow_pixels,32,0);
        memcpy(snow_visits,next.packed,sizeof(snow_visits));
        dnd_draw_backend_publish_texture();
    }
    if(sample){
        visual->buffer^=1;visual->origin=origin;visual->sampled=now;
        visual->revision=revision;visual->fall_ticks=fall_ticks;
    }else if(!enabled)snow_release_visual();
    snow_revision=revision;snow_terrain_revision=terrain_revision;
    return 1;
}
#if defined(__GNUC__) && !defined(__clang__)
#pragma GCC pop_options
#endif
extern void animaltown_shadows_model_reset(void);
static int environment_rgb=0xffffff,light_strength,shadow_strength=255;
int animaltown_environment_color(void){return environment_rgb;}
int animaltown_light_strength(void){return light_strength;}
int animaltown_shadow_strength(void){return shadow_strength;}
int animaltown_object_scope(void){return shadows?shadows->object_scope:0;}
const float *animaltown_point_sources(int *count){*count=shadows?shadows->point_count:0;return shadows?shadows->points:NULL;}
int animaltown_shadows_objects(int scope){
    if(!shadows||scope<0||scope>2)return 0;
    if(shadows->object_scope!=scope){shadows->object_scope=scope;dnd_draw_backend_invalidate_cache();animaltown_shadows_model_reset();}
    return 1;
}
int animaltown_shadows_points(DndArray *sources,int count){
    if(!shadows||dnd_device_active()||!sources||sources->element_size!=4||sources->elements_are_references||count<0||count>SU_LIGHTS||sources->length<(unsigned)count*5)return 0;
    const float *p=(const float*)sources->data;
    for(int i=0;i<count*5;i++)if(!isfinite(p[i]))return 0;
    for(int i=0;i<count;i++)if(p[i*5+3]<=0||p[i*5+3]>32||p[i*5+4]<0||p[i*5+4]>255)return 0;
    /* Camera curvature changes these positions, never shadow/ground texels. */
    memcpy(shadows->points,p,(size_t)count*5*sizeof(float));shadows->point_count=count;return 1;
}
int animaltown_shadows_environment(int rgb,int strength){
    if(rgb<0||rgb>0xffffff||strength<0||strength>255)return 0;
    if(environment_rgb!=rgb||shadow_strength!=strength){
        environment_rgb=rgb;shadow_strength=strength;
        dnd_draw_backend_invalidate_cache();animaltown_shadows_model_reset();
    }
    return 1;
}

int animaltown_shadows_shutdown(void){
    if(dnd_device_active())return 0;
    if(!shadows)return 1;
    /* No frame may still sample the coverage pages. The resource release
     * waits for GX retirement before freeing the charged SDK buffer. */
    if(!snow_release()||dnd_draw_release(shadow_storage)<0)return 0;
    shadows=NULL;shadow_storage=0;environment_rgb=0xffffff;light_strength=0;shadow_strength=255;
    dnd_draw_backend_invalidate_cache();animaltown_shadows_model_reset();return 1;
}
int animaltown_shadows_init(DndArray *atlas,DndArray *circle,DndArray *clouds){
    if(shadows)return 1;
    if(!atlas||!circle||!clouds||atlas->element_size!=1||circle->element_size!=1||clouds->element_size!=1||atlas->length!=32768||circle->length!=2048||clouds->length!=SU_CLOUD_BYTES)return 0;
    shadow_storage=dnd_buffer_create((int)su_storage_bytes(),32);if(!shadow_storage)return 0;
    shadows=(SuState*)(uintptr_t)dnd_buffer_address(shadow_storage,0);if(!shadows){dnd_draw_release(shadow_storage);return 0;}su_init(shadows);
    const uint8_t *a=(const uint8_t*)atlas->data,*c=(const uint8_t*)circle->data;
    for(int p=0;p<4;p++)for(int y=0;y<64;y++)for(int x=0;x<64;x++){
        int px=p%2*64+x,py=p/2*64+y,at=(((py/4)*32+px/4)*16+py%4*4+px%4)*2;
        shadows->shapes[p*4096+y*64+x]=a[at];
    }
    for(int y=0;y<32;y++)for(int x=0;x<32;x++)shadows->shapes[4*4096+y*32+x]=c[(((y/4)*8+x/4)*16+y%4*4+x%4)*2];
    memcpy(shadows->clouds,clouds->data,SU_CLOUD_BYTES);shadows->cloud_ready=shadows->cloud_dirty=1;
    return 1;
}
int animaltown_shadows_cloud(DndArray *offset){
    if(!shadows||dnd_device_active()||!offset||offset->elements_are_references||offset->element_size!=4||offset->length!=6)return 0;
    const float *p=(const float*)offset->data;
    if(!isfinite(p[0])||!isfinite(p[1])||p[0]<0||p[0]>=1||p[1]<0||p[1]>=1)return 0;
    if(!isfinite(p[2])||!isfinite(p[3])||!isfinite(p[4])||p[2]<8||p[2]>128||p[3]<1||p[3]>255||p[4]<0||p[4]>SU_OPACITY)return 0;
    if(!isfinite(p[5])||p[5]<0||p[5]>=255)return 0;
    su_cloud_style(shadows,p[2],p[3],p[4],p[5]);
    su_cloud_motion(shadows,p[0],p[1]);return 1;
}
int animaltown_shadows_lights(DndArray *sources,int count,int strength){
    if(!shadows||dnd_device_active()||strength<0||strength>255)return 0;
    if(sources){
        if(sources->elements_are_references||sources->element_size!=4||count<0||count>SU_LIGHTS||sources->length<(unsigned)count*4)return 0;
        const float *p=(const float*)sources->data;
        for(int i=0;i<count*4;i++)if(!isfinite(p[i]))return 0;
        for(int i=0;i<count;i++)if(p[i*4+2]<=0||p[i*4+2]>16||p[i*4+3]<0||p[i*4+3]>255)return 0;
        su_lights(shadows,p,count);
    }else if(count)return 0;
    light_strength=strength;return 1;
}
/* Reset/32-byte tile write/publish all happen between frames. The old field is
 * hidden while baking; one final flush makes the complete mask visible. */
int animaltown_shadows_terrain(DndArray *tile,int offset,int mode){
    if(!shadows||dnd_device_active()||!tile||tile->element_size!=1)return 0;
    if(mode==0){if(tile->length)return 0;su_terrain_reset(shadows);return 1;}
    if(mode==1){
        if(shadows->terrain_ready||tile->length!=32||offset<0||offset%32||offset>SU_TERRAIN_BYTES-32)return 0;
        const uint8_t *p=(const uint8_t*)tile->data;
        for(int i=0;i<32;i++)if(p[i]>SU_OPACITY)return 0;
        for(int i=0;i<32;i++)shadows->terrain[offset+i]=su_pack(p[i],0);
        return 1;
    }
    if(mode==2){
        if(tile->length)return 0;
        su_terrain_publish(shadows);return 1;
    }
    return 0;
}
int animaltown_shadows_begin(int space,int enabled,int size){if(!shadows)return 0;su_begin(shadows,space,enabled,size);return 1;}
int animaltown_shadows_mesh(int slot,int count,int profile,DndArray *uv,DndArray *bounds){
    if(!shadows||slot<0||slot>=SU_CASTERS||count<0||count>SU_VERTICES||count%3||profile<0||profile>5||!uv||uv->element_size!=4||uv->length<(unsigned)count*2||!bounds||bounds->element_size!=4||bounds->length!=4)return 0;
    float *b=(float*)bounds->data;if(!isfinite(b[0])||!isfinite(b[1])||!isfinite(b[2])||!isfinite(b[3])||b[0]>=b[2]||b[1]>=b[3])return 0;
    for(int i=0;i<count*2;i++)if(!isfinite(((float*)uv->data)[i]))return 0;
    su_mesh(shadows,slot,count,profile,(float*)uv->data,b);return 1;
}
int animaltown_shadows_active(int slot,int active,DndArray *center){
    if(!shadows||slot<0||slot>=SU_CASTERS||!center||center->element_size!=4||center->length!=2)return 0;
    float *p=(float*)center->data;if(!isfinite(p[0])||!isfinite(p[1]))return 0;
    su_active(shadows,slot,active,p[0],p[1]);return 1;
}
static int shadow_can_work(void *deadline){return dnd_timing_now()<*(int64_t*)deadline;}
int animaltown_shadows_prepare(int microseconds){
    if(!shadows||dnd_device_active())return 0;
    int field_changed=shadows->cloud_dirty;
    // A bounded field update cannot consume the actor-page deadline.
    su_cloud_prepare(shadows);
    int64_t deadline=dnd_timing_now()+(microseconds>0?microseconds:0);
    su_prepare_budget(shadows,1,shadow_can_work,&deadline);
    /* Present already fenced the previous frame. Flush only modified pages;
     * publication invalidates GX's texture cache without an extra DrawDone. */
    if(shadows->changed||field_changed){
        if(field_changed)dnd_draw_backend_flush(shadows->coverage,SU_TERRAIN_BYTES,0);
        for(int i=0;i<SU_PAGES;i++)if(shadows->page[i].publish){
            dnd_draw_backend_flush(shadows->pixels+i*SU_BYTES,SU_BYTES,0);
            shadows->page[i].publish=0;
        }
        dnd_draw_backend_publish_texture();
    }
    return shadows->overflow?0:1;
}
int animaltown_shadows_scope(int mode){
    if(!shadows)return 0;
    shadows->scope=mode;dnd_draw_backend_invalidate_cache();animaltown_shadows_model_reset();return mode;
}
int animaltown_shadows_mode(void){return shadows&&shadows->enabled?((shadows->scope==4||shadows->scope==5)&&shadows->space?1:shadows->scope):0;}
const uint8_t *animaltown_shadows_page(float x,float z,float *origin){
    if(!shadows)return NULL;
    int px=(int)floorf(x/shadows->span),pz=(int)floorf(z/shadows->span),at=su_find(shadows,px,pz);
    origin[0]=px*shadows->span;origin[1]=pz*shadows->span;origin[2]=shadows->span;
    origin[3]=SU_SIDE;
    origin[4]=shadows->span;origin[5]=SU_SIDE;
    if(shadows->scope!=3&&shadows->scope!=5&&at>=0&&shadows->page[at].needed)
        return shadows->pixels+at*SU_BYTES;
    if(shadows->space==0&&!shadows->cloud_dirty&&(shadows->terrain_ready||shadows->cloud_ready)){
        origin[0]=origin[1]=0;origin[2]=(SU_TERRAIN_WIDTH-2)/SU_TERRAIN_TEXELS_PER_METRE;origin[3]=SU_TERRAIN_WIDTH;
        origin[4]=(SU_TERRAIN_HEIGHT-2)/SU_TERRAIN_TEXELS_PER_METRE;origin[5]=SU_TERRAIN_HEIGHT;
        return shadows->coverage;
    }
    return NULL;
}
int animaltown_shadows_stats(int kind){
    if(!shadows)return 0;
    if(kind==0){int n=0;for(int i=0;i<SU_PAGES;i++)n+=shadows->page[i].needed;return n;}
    if(kind==9)return dnd_draw_live();
    if(kind==10)return dnd_draw_total_bytes();
    if(kind==11)return shadow_strength;
    if(kind==12)return (int)(shadows->cloud_opacity*255+.5f);
    if(kind==13)return light_strength;
    return kind==1?shadows->overflow:kind==2?shadows->changed:kind==3?(int)su_storage_bytes():kind==4?shadows->static_rasters:kind==5?shadows->dynamic_rasters:kind==6?shadows->support_builds:kind==7?shadows->support_hits:shadows->deferred;
}
