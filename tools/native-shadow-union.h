/* Game-owned, bounded world-space shadow coverage cache. No GX dependency:
 * the exact rasterizer is exercised by the native host checks. */
#ifndef ANIMALTOWN_SHADOW_UNION_H
#define ANIMALTOWN_SHADOW_UNION_H
#include <stdint.h>
#include <stddef.h>
#include <string.h>
#include <math.h>
#define SU_CASTERS 39
#define SU_STATIC 32
#define SU_VERTICES 288
#define SU_PAGES 160 /* 39 footprints, each smaller than a page: <=156 pages. */
#define SU_LOOKUP 512 /* Power of two; at most 160 live entries. */
#define SU_SIDE 32
#define SU_INNER (SU_SIDE-2) /* One filtering gutter on each edge. */
#define SU_BYTES (SU_SIDE*SU_SIDE)
#define SU_OPACITY 66
#define SU_LIGHTS 24
#define SU_TERRAIN_WIDTH 256
#define SU_TERRAIN_HEIGHT 128
#define SU_TERRAIN_TEXELS_PER_METRE 1.f
#define SU_TERRAIN_BYTES (SU_TERRAIN_WIDTH*SU_TERRAIN_HEIGHT)
#define SU_CLOUD_SIDE 64
#define SU_CLOUD_BYTES (SU_CLOUD_SIDE*SU_CLOUD_SIDE)
#define SU_CLOUD_PERIOD 32.f
#define SU_WORLD_WIDTH 128 /* Must match Town.Width; the remaining columns are padding. */
#define SU_SUPPORT_SIDE (SU_SIDE+2) /* One erosion sample outside each gutter. */
#define SU_SUPPORT_BYTES ((SU_SUPPORT_SIDE*SU_SUPPORT_SIDE+7)/8)
#define SU_SUPPORT_PAGES 4
typedef struct {
    int count,profile,space,active,requested;
    unsigned revision;
    float x,z,bounds[4],uv[SU_VERTICES*2];
} SuCaster;
typedef struct { int used,x,z,space,needed,static_dirty,dirty,publish,base_blank,blank,static_pending; unsigned version; } SuPage;
typedef struct {int used,x,z,space;unsigned revision,use;uint8_t bits[SU_SUPPORT_BYTES];} SuSupport;
typedef int (*SuCanWork)(void*);
typedef struct {
    SuCaster caster[SU_CASTERS]; SuPage page[SU_PAGES];
    uint16_t lookup[SU_LOOKUP];
    uint8_t shapes[4*4096+1024];
    uint8_t *terrain,*coverage;
    uint8_t clouds[SU_CLOUD_BYTES];
    float lights[SU_LIGHTS*4];
    float points[SU_LIGHTS*5]; /* World-space position, radius, strength; never baked. */
    int point_count,object_scope;
    int light_count,light_dirty;
    int terrain_ready,cloud_ready,cloud_dirty,cloud_x,cloud_z;
    float cloud_scale,cloud_contrast,cloud_opacity,cloud_bias;
    SuSupport support[(SU_CASTERS-SU_STATIC)*SU_SUPPORT_PAGES];
    uint8_t static_work[SU_BYTES];
    int static_page,static_caster,cursor,deferred,support_builds,support_hits;
    unsigned serial,static_version,support_clock;
    uint8_t *base,*pixels;
    int space,enabled,scope,overflow,changed,static_rasters,dynamic_rasters,layout_dirty;
    float span;
} SuState;
static inline size_t su_storage_bytes(void){return ((sizeof(SuState)+31)&~(size_t)31)+SU_PAGES*SU_BYTES*2+SU_TERRAIN_BYTES*2;}
static inline void su_init(SuState *s){
    memset(s,0,su_storage_bytes());s->span=8;s->static_page=-1;
    s->cloud_scale=SU_CLOUD_SIDE/SU_CLOUD_PERIOD;s->cloud_contrast=1;s->cloud_opacity=SU_OPACITY/255.f;
    s->base=(uint8_t*)s+((sizeof(SuState)+31)&~(size_t)31);s->pixels=s->base+SU_PAGES*SU_BYTES;s->terrain=s->pixels+SU_PAGES*SU_BYTES;s->coverage=s->terrain+SU_TERRAIN_BYTES;
}
/* One terrain texture, shared by every section; no extra caster/page slots. */
static inline int su_terrain_offset(int x,int y){return ((y/4)*(SU_TERRAIN_WIDTH/8)+x/8)*32+y%4*8+x%8;}
static inline void su_terrain_changed(SuState *s){
    s->cloud_dirty=1;
    for(int i=0;i<SU_PAGES;i++)if(s->page[i].used&&s->page[i].space==0){
        s->page[i].static_dirty=1;s->page[i].dirty|=1;
        s->page[i].base_blank=0;s->page[i].version=++s->serial;
    }
    if(s->static_page>=0&&s->page[s->static_page].space==0)s->static_page=-1;
}
static inline uint8_t su_shadow(uint8_t value){return (uint8_t)(((value&15)*SU_OPACITY+7)/15);}
static inline uint8_t su_pack(int shadow,int light){return (uint8_t)(((light+8)/17)*16+(shadow*15+SU_OPACITY/2)/SU_OPACITY);}
/* 4 texels/metre. GX I8 blocks cover two town cells by one. */
#define SU_SNOW_SIDE (4*SU_WORLD_WIDTH)
#define SU_SNOW_HEIGHT 120
#define SU_SNOW_BYTES (SU_SNOW_SIDE*SU_SNOW_SIDE)
#define SU_SNOW_BLOCKS (SU_WORLD_WIDTH/2)
#define SU_SNOW_PACKED (SU_SNOW_BLOCKS*SU_SNOW_HEIGHT)
#define SU_SNOW_MAX_VISITS 5
#define SU_SNOW_STEPS_PER_VISIT 3
static inline int su_snow_wear(const uint8_t *packed,int x,int z){
    if((unsigned)x>=SU_WORLD_WIDTH||(unsigned)z>=SU_SNOW_HEIGHT)return 0;
    int cell=z*SU_WORLD_WIDTH+x;
    return (packed[cell/2]>>((cell%2)*4))&15;
}
static inline float su_snow_stamp(int visits,float x,float z){
    if(!visits)return 0;
    if(visits>=SU_SNOW_MAX_VISITS){
        float edge=fmaxf(fabsf(x),fabsf(z))-.5f;
        return 1.f-fmaxf(0.f,edge)*4.f;
    }
    float ellipse=x*x*5.f+z*z*3.f;
    if(visits==1)return .58f*(1.f-ellipse);
    float wide_dent=.58f*(1.f-ellipse*.64f);
    if(visits==2)return wide_dent; // Widen the first dent below the snow cutout threshold.
    if(visits==3)return fmaxf(wide_dent,1.25f*(1.f-ellipse));
    return 1.32f*(1.f-ellipse*.5f);
}
static inline float su_snow_recovering_stamp(int wear,float x,float z){
    int visits=wear/SU_SNOW_STEPS_PER_VISIT,step=wear%SU_SNOW_STEPS_PER_VISIT;
    float a=su_snow_stamp(visits,x,z);
    if(!step)return a;
    // Interpolate the two visible depths so the cutout and shaded rim
    // recede together between the original visit stages.
    a=fmaxf(0.f,fminf(1.f,a));
    float b=fmaxf(0.f,fminf(1.f,su_snow_stamp(visits+1,x,z)));
    return a+(b-a)*(step*(1.f/SU_SNOW_STEPS_PER_VISIT));
}
static inline uint8_t su_snow_depth(const uint8_t *packed,int x,int z){
    int cx=x/4,cz=z/4;
    float fx=(x%4+.5f)*.25f-.5f,fz=(z%4+.5f)*.25f-.5f,depth=0;
    for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++){
        int wear=su_snow_wear(packed,cx+dx,cz+dz);
        if(!wear)continue;
        depth=fmaxf(depth,su_snow_recovering_stamp(wear,fx-dx,fz-dz));
    }
    return (uint8_t)(fminf(1.f,depth)*255.f+.5f);
}
static inline void su_snow_block(uint8_t *dest,const uint8_t *packed,int bx,int bz){
    for(int z=0;z<4;z++)for(int x=0;x<8;x++)dest[z*8+x]=su_snow_depth(packed,bx*8+x,bz*4+z);
}
static inline void su_terrain_reset(SuState *s){
    s->terrain_ready=0;memset(s->terrain,0,SU_TERRAIN_BYTES);su_terrain_changed(s);
}
static inline void su_terrain_publish(SuState *s){s->terrain_ready=1;s->light_dirty=1;su_terrain_changed(s);}
static inline uint8_t su_field_channel(const uint8_t *field,float x,float z,int light){
    float fx=x*SU_TERRAIN_TEXELS_PER_METRE+.5f,fz=z*SU_TERRAIN_TEXELS_PER_METRE+.5f;
    if(fx<0||fz<0||fx>=SU_TERRAIN_WIDTH-1||fz>=SU_TERRAIN_HEIGHT-1)return 0;
    int ix=(int)fx,iz=(int)fz;float tx=fx-ix,tz=fz-iz;
    uint8_t av=field[su_terrain_offset(ix,iz)],bv=field[su_terrain_offset(ix+1,iz)],cv=field[su_terrain_offset(ix,iz+1)],dv=field[su_terrain_offset(ix+1,iz+1)];
    float a=light?(av>>4)*17:su_shadow(av),b=light?(bv>>4)*17:su_shadow(bv);
    float c=light?(cv>>4)*17:su_shadow(cv),d=light?(dv>>4)*17:su_shadow(dv);
    return (uint8_t)((a+(b-a)*tx)*(1-tz)+(c+(d-c)*tx)*tz+.5f);
}
static inline uint8_t su_field_sample(const uint8_t *field,float x,float z){return su_field_channel(field,x,z,0);}
static inline uint8_t su_terrain_sample(const SuState *s,float x,float z){return s->terrain_ready?su_field_sample(s->terrain,x,z):0;}
static inline void su_seed_terrain(const SuState *s,const SuPage *p,uint8_t *dest){
    memset(dest,0,SU_BYTES);
    if(p->space||!s->terrain_ready)return;
    float step=s->span/SU_INNER;
    for(int y=0;y<SU_SIDE;y++)for(int x=0;x<SU_SIDE;x++)
        dest[((y/4)*(SU_SIDE/8)+x/8)*32+y%4*8+x%8]=su_pack(su_terrain_sample(s,p->x*s->span+(x-.5f)*step,p->z*s->span+(y-.5f)*step),0);
}
static inline unsigned su_hash(int space,int x,int z){
    uint32_t h=(uint32_t)x*0x9e3779b1u^(uint32_t)z*0x85ebca6bu^(uint32_t)space*0xc2b2ae35u;
    h^=h>>16;return h&(SU_LOOKUP-1);
}
static inline void su_index(SuState *s,int slot){
    const SuPage *p=&s->page[slot];unsigned at=su_hash(p->space,p->x,p->z);
    while(s->lookup[at])at=(at+1)&(SU_LOOKUP-1);
    s->lookup[at]=(uint16_t)(slot+1);
}
static inline void su_unindex(SuState *s,int slot){
    if(!s->page[slot].used)return;
    const SuPage *p=&s->page[slot];unsigned at=su_hash(p->space,p->x,p->z);
    while(s->lookup[at]&&s->lookup[at]!=slot+1)at=(at+1)&(SU_LOOKUP-1);
    if(!s->lookup[at])return;
    s->lookup[at]=0;
    /* Reinsert the remaining probe cluster so deleting an evicted page cannot
     * hide a colliding page. Occupancy stays below one third of the table. */
    for(at=(at+1)&(SU_LOOKUP-1);s->lookup[at];at=(at+1)&(SU_LOOKUP-1)){
        int next=s->lookup[at]-1;s->lookup[at]=0;su_index(s,next);
    }
}
static inline int su_find_space(const SuState *s,int space,int x,int z){
    unsigned at=su_hash(space,x,z);
    while(s->lookup[at]){
        int slot=s->lookup[at]-1;const SuPage *p=&s->page[slot];
        if(p->space==space&&p->x==x&&p->z==z)return slot;
        at=(at+1)&(SU_LOOKUP-1);
    }
    return -1;
}
static inline int su_find(const SuState *s,int x,int z){return su_find_space(s,s->space,x,z);}
static inline int su_offset(int x,int y){return ((y/4)*(SU_SIDE/8)+x/8)*32+y%4*8+x%8;}
static inline int su_floor(float x){int i=(int)x;return i-(i>x);}
static inline int su_cloud_offset(int x,int z){return ((z/4)*(SU_CLOUD_SIDE/8)+x/8)*32+z%4*8+x%8;}
static inline uint8_t su_cloud_sample(const SuState *s,float x,float z){
    if(!s->cloud_ready||s->cloud_opacity<=0)return 0;
    float fx=(x+s->cloud_x*.125f)*s->cloud_scale-.5f;
    float fz=(z+s->cloud_z*.125f)*s->cloud_scale-.5f;
    int ix=su_floor(fx),iz=su_floor(fz);float tx=fx-ix,tz=fz-iz;
    int ax=ix&(SU_CLOUD_SIDE-1),bx=(ix+1)&(SU_CLOUD_SIDE-1),az=iz&(SU_CLOUD_SIDE-1),bz=(iz+1)&(SU_CLOUD_SIDE-1);
    float a=s->clouds[su_cloud_offset(ax,az)],b=s->clouds[su_cloud_offset(bx,az)];
    float c=s->clouds[su_cloud_offset(ax,bz)],d=s->clouds[su_cloud_offset(bx,bz)];
    float value=(a+(b-a)*tx)*(1-tz)+(c+(d-c)*tx)*tz;
    if(s->cloud_contrast!=1||s->cloud_bias!=0){value=value*s->cloud_contrast+s->cloud_bias;if(value<0)value=0;if(value>255)value=255;}
    return (uint8_t)(value*s->cloud_opacity+.5f);
}
static inline void su_cloud_style(SuState *s,float period,float edge,float strength,float cutoff){
    float scale=SU_CLOUD_SIDE/period,edge_contrast=255/edge,contrast=edge_contrast*255/(255-cutoff),opacity=strength/255;
    float bias=127.5f*(1-edge_contrast)-cutoff*contrast;
    if(s->cloud_scale==scale&&s->cloud_contrast==contrast&&s->cloud_opacity==opacity&&s->cloud_bias==bias)return;
    s->cloud_scale=scale;s->cloud_contrast=contrast;s->cloud_opacity=opacity;s->cloud_bias=bias;s->cloud_dirty=1;
}
static inline void su_cloud_motion(SuState *s,float x,float z){
    if(s->cloud_opacity<=0)return;
    // One eighth of a world metre: bounded updates with sub-texel drift.
    float units=SU_CLOUD_SIDE*8/s->cloud_scale;
    int px=(int)(x*units),pz=(int)(z*units);
    if(px!=s->cloud_x||pz!=s->cloud_z){s->cloud_x=px;s->cloud_z=pz;s->cloud_dirty=1;}
}
static inline void su_lights(SuState *s,const float *sources,int count){
    if(s->light_count==count&&!memcmp(s->lights,sources,(size_t)count*16))return;
    s->light_count=count;memcpy(s->lights,sources,(size_t)count*16);s->light_dirty=s->cloud_dirty=1;
}
static inline int su_clamp(int x,int max){return x<0?0:x>max?max:x;}
static inline void su_bake_lights(SuState *s){
    if(!s->light_dirty)return;
    for(int i=0;i<SU_TERRAIN_BYTES;i++)s->terrain[i]&=15;
    for(int n=0;n<s->light_count;n++){
        const float *p=s->lights+n*4;
        float radius=p[2],radius_squared=radius*radius;
        /* Sample x is at (x-.5)/scale. Expand both bounds by one texel
         * for rounding, then clip before converting even far-off sources. */
        float left=(p[0]-radius)*SU_TERRAIN_TEXELS_PER_METRE-.5f;
        float right=(p[0]+radius)*SU_TERRAIN_TEXELS_PER_METRE+1.5f;
        float top=(p[1]-radius)*SU_TERRAIN_TEXELS_PER_METRE-.5f;
        float bottom=(p[1]+radius)*SU_TERRAIN_TEXELS_PER_METRE+1.5f;
        if(right<0||bottom<0||left>SU_TERRAIN_WIDTH-1||top>SU_TERRAIN_HEIGHT-1)continue;
        int first_x=(int)fmaxf(left,0),last_x=(int)fminf(right,SU_TERRAIN_WIDTH-1);
        int first_z=(int)fmaxf(top,0),last_z=(int)fminf(bottom,SU_TERRAIN_HEIGHT-1);
        for(int z=first_z;z<=last_z;z++){
            float dz=(z-.5f)/SU_TERRAIN_TEXELS_PER_METRE-p[1];
            for(int x=first_x;x<=last_x;x++){
                float dx=(x-.5f)/SU_TERRAIN_TEXELS_PER_METRE-p[0];
                float r=1-(dx*dx+dz*dz)/radius_squared;
                if(r>0){
                    float strength=r*r*p[3];
                    int light=su_clamp((int)((strength+8)/17),15);
                    uint8_t *texel=s->terrain+su_terrain_offset(x,z);
                    if(light>(*texel>>4))*texel=(*texel&15)|(light<<4);
                }
            }
        }
    }
    s->light_dirty=0;s->cloud_dirty=1;
}
static inline void su_cloud_prepare(SuState *s){
    su_bake_lights(s);
    if(!s->cloud_dirty)return;
    for(int y=0;y<SU_TERRAIN_HEIGHT;y++)for(int x=0;x<SU_TERRAIN_WIDTH;x++){
        int at=su_terrain_offset(x,y);uint8_t a=s->terrain_ready?su_shadow(s->terrain[at]):0;
        uint8_t b=x<=SU_WORLD_WIDTH+1?su_cloud_sample(s,(x-.5f)/SU_TERRAIN_TEXELS_PER_METRE,(y-.5f)/SU_TERRAIN_TEXELS_PER_METRE):0;
        s->coverage[at]=su_pack(a>b?a:b,(s->terrain[at]>>4)*17);
    }
    for(int i=0;i<SU_PAGES;i++)if(s->page[i].used&&!s->page[i].space)s->page[i].dirty|=1;
    s->cloud_dirty=0;
}
static inline void su_page_clouds(const SuState *s,const SuPage *p,uint8_t *dest){
    if(p->space)return;
    float step=s->span/SU_INNER;
    for(int y=0;y<SU_SIDE;y++)for(int x=0;x<SU_SIDE;x++){
        int at=su_offset(x,y);
        float px=p->x*s->span+(x-.5f)*step,pz=p->z*s->span+(y-.5f)*step;
        uint8_t a=su_field_sample(s->coverage,px,pz),b=su_shadow(dest[at]);
        dest[at]=su_pack(a>b?a:b,su_field_channel(s->coverage,px,pz,1));
    }
}
static inline void su_bounds(const SuCaster *c,float b[4]){
    if(c->profile==4){b[0]=c->x+.12f-.55f;b[1]=c->z+.1f-.55f;b[2]=b[0]+1.1f;b[3]=b[1]+1.1f;}
    else memcpy(b,c->bounds,16);
}
static inline int su_intersects(const SuState *s,const SuPage *p,const float b[4]){
    float pad=s->span/SU_INNER, x=p->x*s->span,z=p->z*s->span;
    return b[0]<x+s->span+pad&&b[2]>x-pad&&b[1]<z+s->span+pad&&b[3]>z-pad;
}
static inline void su_dirty(SuState *s,const SuCaster *c){
    if(!c->count)return;
    float b[4];su_bounds(c,b);float pad=s->span/SU_INNER;
    int l=su_floor((b[0]-pad)/s->span),r=su_floor((b[2]+pad)/s->span),t=su_floor((b[1]-pad)/s->span),bot=su_floor((b[3]+pad)/s->span);
    for(int z=t;z<=bot;z++)for(int x=l;x<=r;x++){
        int at=su_find_space(s,c->space,x,z);if(at<0||!su_intersects(s,&s->page[at],b))continue;
        s->page[at].dirty|=c->profile==4?2:1;if(c->profile!=4){s->page[at].static_dirty=1;s->page[at].base_blank=0;s->page[at].version=++s->serial;}
    }
}
static inline void su_begin(SuState *s,int space,int enabled,int size){
    if(s->space!=space||s->enabled!=enabled)s->layout_dirty=1;
    s->space=space;s->enabled=enabled;s->scope=0;s->overflow=0;
    /* Current rooms fit one page; larger catalog rooms keep that property. */
    float span=space&&size>8?(float)size:8;
    if(s->span!=span){memset(s->page,0,sizeof(s->page));memset(s->lookup,0,sizeof(s->lookup));memset(s->support,0,sizeof(s->support));s->static_page=-1;s->span=span;s->layout_dirty=1;}
    for(int i=SU_STATIC;i<SU_CASTERS;i++)s->caster[i].requested=0;
}
static inline void su_mesh(SuState *s,int slot,int count,int profile,const float *uv,const float *bounds){
    SuCaster *c=&s->caster[slot];s->layout_dirty=1;if(c->count)su_dirty(s,c);
    c->revision++;if(slot>=SU_STATIC)for(int i=0;i<SU_SUPPORT_PAGES;i++)s->support[(slot-SU_STATIC)*SU_SUPPORT_PAGES+i].used=0;
    c->count=count;c->profile=profile;c->space=slot<SU_STATIC?0:s->space;
    memcpy(c->bounds,bounds,16);if(count)memcpy(c->uv,uv,(size_t)count*8);
    if(!count)c->active=0;
    su_dirty(s,c);
}
static inline void su_active(SuState *s,int slot,int active,float x,float z){
    SuCaster *c=&s->caster[slot];active=active&&c->count>0;
    if(c->profile==5&&(c->x!=x||c->z!=z)){su_dirty(s,c);c->x=x;c->z=z;su_dirty(s,c);}
    if(slot>=SU_STATIC){
        c->requested=active;
        if(c->x!=x||c->z!=z){s->layout_dirty=1;su_dirty(s,c);c->x=x;c->z=z;su_dirty(s,c);}
    }else if(c->active!=active){s->layout_dirty=1;su_dirty(s,c);c->active=active;su_dirty(s,c);}
}
static inline float su_saturate(float x){return x<0?0:x>1?1:x;}
/* The post's base, narrow pole and roof project from the same fixed NW light
 * as the scenery profiles. Dimensions match DrawStreetLights, including the
 * roof centre at 2.58m. No extra atlas or texture storage is required. */
static inline uint8_t su_post_shadow(const SuCaster *c,float x,float z){
    x-=c->x;z-=c->z;
    float t=su_saturate((x+z*.8f)/1.64f),dx=x-t,dz=z-t*.8f;
    float stem=su_saturate((.0121f-dx*dx-dz*dz)/.0096f);
    float roof=fmaxf(fabsf(x-1.032f)-.29f,fabsf(z-.8256f)-.29f);
    float base=fmaxf(fabsf(x-.03f)-.21f,fabsf(z-.024f)-.204f);
    float alpha=fmaxf(stem,fmaxf(su_saturate(1-roof/.07f),su_saturate(1-base/.05f)));
    return (uint8_t)(alpha*SU_OPACITY+.5f);
}
static inline uint8_t su_sample(const SuState *s,const SuCaster *c,float x,float z){
    if(c->profile==5)return su_post_shadow(c,x,z);
    float b[4];su_bounds(c,b);float u=(x-b[0])/(b[2]-b[0]),v=(z-b[1])/(b[3]-b[1]);
    if(u<=0||u>=1||v<=0||v>=1)return 0;
    int side=c->profile==4?32:64;const uint8_t *a=s->shapes+(c->profile==4?4*4096:c->profile*4096);
    float fx=c->profile==4?u*32-.5f:1.5f+u*60,fz=c->profile==4?v*32-.5f:1.5f+v*60;
    if(fx<0||fz<0||fx>=side-1||fz>=side-1)return 0;
    int ix=(int)fx,iz=(int)fz;float tx=fx-ix,tz=fz-iz;
    float a0=a[iz*side+ix]+(a[iz*side+ix+1]-a[iz*side+ix])*tx;
    float a1=a[(iz+1)*side+ix]+(a[(iz+1)*side+ix+1]-a[(iz+1)*side+ix])*tx;
    return (uint8_t)((a0+(a1-a0)*tz)*SU_OPACITY/255+.5f);
}
static inline int su_inside(float x,float z,const float *v){
    float a=(v[2]-v[0])*(z-v[1])-(v[3]-v[1])*(x-v[0]);
    float b=(v[4]-v[2])*(z-v[3])-(v[5]-v[3])*(x-v[2]);
    float c=(v[0]-v[4])*(z-v[5])-(v[1]-v[5])*(x-v[4]);
    return (a>=-.000001f&&b>=-.000001f&&c>=-.000001f)||(a<=.000001f&&b<=.000001f&&c<=.000001f);
}
static inline int su_bit(const uint8_t *bits,int at){return (bits[at>>3]>>(at&7))&1;}
static inline void su_support_build(SuState *s,const SuPage *p,const SuCaster *c,uint8_t *valid){
    memset(valid,0,SU_SUPPORT_BYTES);s->support_builds++;
    float step=s->span/SU_INNER,inv=SU_INNER/s->span,ox=p->x*s->span-1.5f*step,oz=p->z*s->span-1.5f*step;
    for(int i=0;i<c->count;i+=3){
        const float *v=c->uv+i*2;float minX=v[0],maxX=v[0],minZ=v[1],maxZ=v[1];
        for(int k=2;k<6;k+=2){if(v[k]<minX)minX=v[k];if(v[k]>maxX)maxX=v[k];if(v[k+1]<minZ)minZ=v[k+1];if(v[k+1]>maxZ)maxZ=v[k+1];}
        if(maxX<ox||minX>ox+(SU_SUPPORT_SIDE-1)*step||maxZ<oz||minZ>oz+(SU_SUPPORT_SIDE-1)*step)continue;
        int l=su_clamp((int)((minX-ox)*inv),SU_SUPPORT_SIDE-1),r=su_clamp((int)((maxX-ox)*inv)+1,SU_SUPPORT_SIDE-1);
        int t=su_clamp((int)((minZ-oz)*inv),SU_SUPPORT_SIDE-1),b=su_clamp((int)((maxZ-oz)*inv)+1,SU_SUPPORT_SIDE-1);
        for(int y=t;y<=b;y++)for(int x=l;x<=r;x++)if(su_inside(ox+x*step,oz+y*step,v)){int at=y*SU_SUPPORT_SIDE+x;valid[at>>3]|=(uint8_t)(1u<<(at&7));}
    }
}
static inline const uint8_t *su_support(SuState *s,const SuPage *p,const SuCaster *c,uint8_t *local){
    if(c->profile!=4||c<s->caster+SU_STATIC){su_support_build(s,p,c,local);return local;}
    int slot=(int)(c-s->caster)-SU_STATIC,oldest=0;SuSupport *cache=s->support+slot*SU_SUPPORT_PAGES;
    for(int i=0;i<SU_SUPPORT_PAGES;i++){
        SuSupport *q=&cache[i];
        if(q->used&&q->revision==c->revision&&q->x==p->x&&q->z==p->z&&q->space==p->space){q->use=++s->support_clock;s->support_hits++;return q->bits;}
        if(!q->used)oldest=i;else if(cache[oldest].used&&q->use<cache[oldest].use)oldest=i;
    }
    SuSupport *q=&cache[oldest];q->used=1;q->revision=c->revision;q->x=p->x;q->z=p->z;q->space=p->space;q->use=++s->support_clock;
    su_support_build(s,p,c,q->bits);return q->bits;
}
static inline void su_raster(SuState *s,const SuPage *p,const SuCaster *c,uint8_t *dest){
    float bounds[4];su_bounds(c,bounds);if(!su_intersects(s,p,bounds))return;
    uint8_t local[SU_SUPPORT_BYTES];const uint8_t *valid=su_support(s,p,c,local);
    float step=s->span/SU_INNER,inv=SU_INNER/s->span,ox=p->x*s->span-1.5f*step,oz=p->z*s->span-1.5f*step;
    int l=su_clamp((int)((bounds[0]-ox)*inv)-1,SU_SUPPORT_SIDE-1),r=su_clamp((int)((bounds[2]-ox)*inv)+2,SU_SUPPORT_SIDE-1);
    int t=su_clamp((int)((bounds[1]-oz)*inv)-1,SU_SUPPORT_SIDE-1),b=su_clamp((int)((bounds[3]-oz)*inv)+2,SU_SUPPORT_SIDE-1);
    /* Coverage is cached in world-page coordinates, independently of the
     * moving circle. Erode the union, preserving room mats/internal seams. */
    for(int y=t<1?1:t;y<=b&&y<SU_SUPPORT_SIDE-1;y++)for(int x=l<1?1:l;x<=r&&x<SU_SUPPORT_SIDE-1;x++){
        int k=y*SU_SUPPORT_SIDE+x;
        if(!su_bit(valid,k)||!su_bit(valid,k-1)||!su_bit(valid,k+1)||!su_bit(valid,k-SU_SUPPORT_SIDE)||!su_bit(valid,k-SU_SUPPORT_SIDE+1)||!su_bit(valid,k-SU_SUPPORT_SIDE-1)||!su_bit(valid,k+SU_SUPPORT_SIDE)||!su_bit(valid,k+SU_SUPPORT_SIDE+1)||!su_bit(valid,k+SU_SUPPORT_SIDE-1))continue;
        uint8_t alpha=su_sample(s,c,ox+x*step,oz+y*step);int at=su_offset(x-1,y-1);if(alpha>su_shadow(dest[at]))dest[at]=su_pack(alpha,(dest[at]>>4)*17);
    }
}
static inline int su_can_work(SuCanWork can,void *context){return !can||can(context);}
static inline void su_static_work(SuState *s,int static_budget,SuCanWork can,void *context){
    if(static_budget>0&&su_can_work(can,context)){
        if(s->static_page<0)for(int i=0;i<SU_PAGES;i++)if(s->page[i].used&&s->page[i].needed&&s->page[i].space==s->space&&s->page[i].static_dirty){
            s->static_page=i;s->static_caster=0;s->static_version=s->page[i].version;su_seed_terrain(s,&s->page[i],s->static_work);break;
        }
        while(s->static_page>=0&&static_budget>0&&su_can_work(can,context)){
            SuPage *p=&s->page[s->static_page];
            if(s->static_caster<SU_STATIC){
                SuCaster *c=&s->caster[s->static_caster++];if(c->active&&c->space==s->space){su_raster(s,p,c,s->static_work);s->static_rasters++;}
            }else{
                memcpy(s->base+s->static_page*SU_BYTES,s->static_work,SU_BYTES);p->static_dirty=0;p->base_blank=0;p->blank=0;p->dirty|=1;p->static_pending=1;s->static_page=-1;static_budget--;
                if(!can&&static_budget>0)for(int i=0;i<SU_PAGES;i++)if(s->page[i].used&&s->page[i].needed&&s->page[i].space==s->space&&s->page[i].static_dirty){s->static_page=i;s->static_caster=0;s->static_version=s->page[i].version;su_seed_terrain(s,&s->page[i],s->static_work);break;}
            }
        }
    }
}
static inline void su_prepare_budget(SuState *s,int static_budget,SuCanWork can,void *context){
    su_cloud_prepare(s);
    s->changed=s->static_rasters=s->dynamic_rasters=s->support_builds=s->support_hits=s->deferred=0;
    for(int i=SU_STATIC;i<SU_CASTERS;i++){
        SuCaster *c=&s->caster[i];if(c->active!=c->requested){s->layout_dirty=1;su_dirty(s,c);c->active=c->requested;su_dirty(s,c);}
    }
    if(!s->enabled)return;
    if(s->layout_dirty){
        for(int i=0;i<SU_PAGES;i++)s->page[i].needed=0;
        /* First protect every existing footprint, then allocate missing pages.
         * Bounded page ranges replace the pages-times-casters intersection scan. */
        for(int pass=0;pass<2;pass++)for(int j=0;j<SU_CASTERS;j++){
            SuCaster *c=&s->caster[j];if(!c->active||c->space!=s->space)continue;
            float b[4];su_bounds(c,b);float pad=s->span/SU_INNER;
            int l=su_floor((b[0]-pad)/s->span),r=su_floor((b[2]+pad)/s->span),t=su_floor((b[1]-pad)/s->span),bot=su_floor((b[3]+pad)/s->span);
            for(int z=t;z<=bot;z++)for(int x=l;x<=r;x++){
                int at=su_find(s,x,z);
                if(at<0&&pass){
                    for(int i=0;i<SU_PAGES;i++)if(!s->page[i].needed){at=i;break;}
                    if(at<0){s->overflow++;continue;}
                    su_unindex(s,at);s->page[at]=(SuPage){.used=1,.x=x,.z=z,.space=s->space,.needed=1,.static_dirty=1,.dirty=3,.base_blank=1,.version=++s->serial};su_index(s,at);
                    /* Never expose an evicted tile while static work waits. */
                    memset(s->base+at*SU_BYTES,0,SU_BYTES);memset(s->pixels+at*SU_BYTES,0,SU_BYTES);
                }else if(at>=0)s->page[at].needed=1;
            }
        }
        s->layout_dirty=s->overflow!=0;
    }
    /* Clear invalid static coverage immediately, before any budgeted work,
     * so deleted/moved scenery cannot linger while its replacement waits. */
    for(int i=0;i<SU_PAGES;i++){
        SuPage *p=&s->page[i];if(!p->used||p->space!=s->space)continue;
        if(p->static_dirty&&!p->base_blank){memset(s->base+i*SU_BYTES,0,SU_BYTES);p->base_blank=1;p->blank=0;p->dirty|=2;}
    }
    if(s->static_page>=0){SuPage *p=&s->page[s->static_page];
        if(!p->used||!p->needed||p->space!=s->space||p->version!=s->static_version)s->static_page=-1;
    }
    if(!can)su_static_work(s,static_budget,can,context);
    int first=s->cursor;
    for(int n=0;n<SU_PAGES;n++){
        int i=(first+n)%SU_PAGES;SuPage *p=&s->page[i];if(p->space!=s->space||!p->used)continue;
        uint8_t *base=s->base+i*SU_BYTES,*dest=s->pixels+i*SU_BYTES;
        if(!p->needed){
            if(p->dirty||p->static_dirty){memset(base,0,SU_BYTES);memset(dest,0,SU_BYTES);p->dirty=p->static_dirty=p->static_pending=0;p->publish=1;p->blank=1;s->changed++;}
            continue;
        }
        if(!p->dirty)continue;
        if(!(p->dirty&2)&&!su_can_work(can,context)){
            /* Cloud/static refresh may wait. Keep the last complete page;
             * erasing it here made walking contact shadows blink. Actor moves,
             * removals and newly allocated pages always publish this frame. */
            s->deferred++;continue;
        }
        memcpy(dest,base,SU_BYTES);
        for(int j=SU_STATIC;j<SU_CASTERS;j++){SuCaster *c=&s->caster[j];if(c->active&&c->space==s->space){su_raster(s,p,c,dest);s->dynamic_rasters++;}}
        su_page_clouds(s,p,dest);
        p->dirty=0;p->blank=0;p->static_pending=0;p->publish=1;s->changed++;s->cursor=(i+1)%SU_PAGES;
    }
    if(can)su_static_work(s,static_budget,can,context);
}
static inline void su_prepare(SuState *s,int static_budget){su_prepare_budget(s,static_budget,NULL,NULL);}
#endif
