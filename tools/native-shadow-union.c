#include "dnd_draw.h"
#include "dnd_model.h"
#ifndef ANIMALTOWN_SHADOW_UNION_H
#include "animaltown_shadow_union.h"
#endif
#include "dnd_timing.h"
static SuState *shadows;
static int shadow_storage;
extern void animaltown_shadows_model_reset(void);
static int environment_rgb=0xffffff,light_strength;
int animaltown_environment_color(void){return environment_rgb;}
int animaltown_light_strength(void){return light_strength;}
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
int animaltown_shadows_environment(int rgb){
    if(rgb<0||rgb>0xffffff)return 0;
    if(environment_rgb!=rgb){environment_rgb=rgb;dnd_draw_backend_invalidate_cache();animaltown_shadows_model_reset();}
    return 1;
}

int animaltown_shadows_shutdown(void){
    if(dnd_device_active())return 0;
    if(!shadows)return 1;
    /* No frame may still sample the coverage pages. The resource release
     * waits for GX retirement before freeing the charged SDK buffer. */
    if(dnd_draw_release(shadow_storage)<0)return 0;
    shadows=NULL;shadow_storage=0;environment_rgb=0xffffff;light_strength=0;
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
    if(!shadows||dnd_device_active()||!offset||offset->elements_are_references||offset->element_size!=4||offset->length!=5)return 0;
    const float *p=(const float*)offset->data;
    if(!isfinite(p[0])||!isfinite(p[1])||p[0]<0||p[0]>=1||p[1]<0||p[1]>=1)return 0;
    if(!isfinite(p[2])||!isfinite(p[3])||!isfinite(p[4])||p[2]<8||p[2]>128||p[3]<1||p[3]>255||p[4]<0||p[4]>SU_OPACITY)return 0;
    su_cloud_style(shadows,p[2],p[3],p[4]);
    su_cloud_motion(shadows,p[0],p[1]);return 1;
}
int animaltown_shadows_lights(DndArray *sources,int count,int strength){
    if(!shadows||dnd_device_active()||!sources||sources->element_size!=4||count<0||count>SU_LIGHTS||sources->length<(unsigned)count*4||strength<0||strength>255)return 0;
    const float *p=(const float*)sources->data;
    for(int i=0;i<count*4;i++)if(!isfinite(p[i]))return 0;
    for(int i=0;i<count;i++)if(p[i*4+2]<=0||p[i*4+2]>16||p[i*4+3]<0||p[i*4+3]>255)return 0;
    su_lights(shadows,p,count);light_strength=strength;return 1;
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
        for(int i=0;i<SU_PAGES;i++)if(shadows->page[i].publish){dnd_draw_backend_flush(shadows->pixels+i*SU_BYTES,SU_BYTES,0);shadows->page[i].publish=0;}
        dnd_draw_backend_publish_texture();
    }
    return shadows->overflow?0:1;
}
int animaltown_shadows_scope(int mode){
    if(!shadows)return 0;
    shadows->scope=mode;dnd_draw_backend_invalidate_cache();animaltown_shadows_model_reset();return mode;
}
int animaltown_shadows_mode(void){return shadows&&shadows->enabled?shadows->scope:0;}
const uint8_t *animaltown_shadows_page(float x,float z,float *origin){
    if(!shadows)return NULL;
    int px=(int)floorf(x/shadows->span),pz=(int)floorf(z/shadows->span),at=su_find(shadows,px,pz);
    origin[0]=px*shadows->span;origin[1]=pz*shadows->span;origin[2]=shadows->span;
    origin[3]=SU_SIDE;
    origin[4]=shadows->span;origin[5]=SU_SIDE;
    if(shadows->scope!=3&&at>=0&&shadows->page[at].needed&&!shadows->page[at].static_dirty&&!shadows->page[at].static_pending)
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
    return kind==1?shadows->overflow:kind==2?shadows->changed:kind==3?(int)su_storage_bytes():kind==4?shadows->static_rasters:kind==5?shadows->dynamic_rasters:kind==6?shadows->support_builds:kind==7?shadows->support_hits:shadows->deferred;
}
