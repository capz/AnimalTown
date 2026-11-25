#define _POSIX_C_SOURCE 200809L
#include <assert.h>
#include <float.h>
#include <stdio.h>
#include <stdlib.h>
#include <time.h>
#include "../tools/native-shadow-union.h"
#include "native-shadow-combiner.inc"
#include "native-shadow-cloud-reuse.inc"
static SuState *s;
static const float rect[]={0,0,8,0,8,8,0,0,8,8,0,8};
static const float bounds[]={1,1,7,7};
static int assertions;
#define CHECK(x) do{assert(x);assertions++;}while(0)
static void reset(void){su_init(s);memset(s->shapes,255,sizeof(s->shapes));su_begin(s,0,1,0);}
static void caster(int i,int profile,const float *uv,int n,const float *b){su_mesh(s,i,n,profile,uv,b);su_active(s,i,1,4,4);}
static void next(void){su_begin(s,0,1,0);}
static int pixel(float x,float z){int p=su_find(s,(int)floorf(x/8),(int)floorf(z/8));CHECK(p>=0);int a=(int)((x-s->page[p].x*8)*SU_INNER/8+1),b=(int)((z-s->page[p].z*8)*SU_INNER/8+1);return su_shadow(s->pixels[p*SU_BYTES+su_offset(a,b)]);}
static int credits;
static int allow_work(void *unused){(void)unused;return credits-->0;}

/* Original texel-first bake, retained as an independent byte oracle. */
static void legacy_bake_lights(SuState *state){
    if(!state->light_dirty)return;
    for(int z=0;z<SU_TERRAIN_HEIGHT;z++)for(int x=0;x<SU_TERRAIN_WIDTH;x++){
        float px=(x-.5f)/SU_TERRAIN_TEXELS_PER_METRE,pz=(z-.5f)/SU_TERRAIN_TEXELS_PER_METRE,light=0;
        for(int n=0;n<state->light_count;n++){
            const float *p=state->lights+n*4;
            float dx=px-p[0],dz=pz-p[1],r=1-(dx*dx+dz*dz)/(p[2]*p[2]);
            if(r>0){float strength=r*r*p[3];if(strength>light)light=strength;}
        }
        int at=su_terrain_offset(x,z);
        state->terrain[at]=(state->terrain[at]&15)|((int)((light+8)/17)<<4);
    }
    state->light_dirty=0;state->cloud_dirty=1;
}

static float light_random(uint32_t *seed){
    *seed=*seed*1664525u+1013904223u;
    return (*seed>>8)/16777216.f;
}

static void compare_light_fields(SuState *reference,const float *sources,int count){
    su_lights(s,sources,count);
    su_lights(reference,sources,count);
    legacy_bake_lights(reference);
    su_cloud_prepare(reference);
    su_cloud_prepare(s);
    CHECK(!memcmp(s->terrain,reference->terrain,SU_TERRAIN_BYTES));
    CHECK(!memcmp(s->coverage,reference->coverage,SU_TERRAIN_BYTES));
}

static void light_equivalence_checks(void){
    reset();
    SuState *reference=aligned_alloc(32,su_storage_bytes());CHECK(reference!=NULL);
    su_init(reference);
    uint32_t seed=0xcafe114u;
    for(int i=0;i<SU_TERRAIN_BYTES;i++)s->terrain[i]=reference->terrain[i]=(uint8_t)(light_random(&seed)*256);
    for(int i=0;i<SU_CLOUD_BYTES;i++)s->clouds[i]=reference->clouds[i]=(uint8_t)(light_random(&seed)*256);
    s->cloud_ready=reference->cloud_ready=1;
    su_terrain_publish(s);su_terrain_publish(reference);
    const float edges[]={-.5f,-.5f,.1f,255, 255.5f,127.5f,3,210, 127.5f,7.5f,16,255,
                         127.5f,7.5f,3,210, 127.5f,7.5f,2,0, -18,4,16,255,
                         FLT_MAX,-FLT_MAX,16,255, 4.5f,4.5f,FLT_MIN,255, 4.5f,4.5f,1,9};
    compare_light_fields(reference,edges,0);
    compare_light_fields(reference,edges,1);
    CHECK(s->terrain[su_terrain_offset(0,0)]>>4==15);
    compare_light_fields(reference,edges,(int)(sizeof(edges)/sizeof(edges[0])/4));
    for(int trial=0;trial<48;trial++){
        float sources[SU_LIGHTS*4];
        int count=trial%3==0?SU_LIGHTS:trial%3==1?1:0;
        for(int n=0;n<count;n++){
            float *p=sources+4*n;
            p[0]=light_random(&seed)*(SU_TERRAIN_WIDTH+32)-16;
            p[1]=light_random(&seed)*(SU_TERRAIN_HEIGHT+32)-16;
            p[2]=.1f+light_random(&seed)*15.9f;
            p[3]=light_random(&seed)*255;
        }
        su_cloud_motion(s,trial/48.f,.25f);su_cloud_motion(reference,trial/48.f,.25f);
        compare_light_fields(reference,sources,count);
    }
    /* Republish cliff geometry with an unchanged source list. */
    compare_light_fields(reference,edges,1);
    su_terrain_reset(s);su_terrain_reset(reference);
    s->terrain[0]=reference->terrain[0]=su_pack(SU_OPACITY,0);
    su_terrain_publish(s);su_terrain_publish(reference);
    compare_light_fields(reference,edges,1);
    CHECK(s->terrain[0]==255);
    compare_light_fields(reference,edges,0);
    CHECK(s->terrain[0]==15);
    free(reference);
}

static void static_base_equivalence_checks(void){
    uint8_t reference[SU_BYTES],restored[SU_BYTES],packed[SU_BASE_BYTES];
    /* Exercise every possible pairing of shadow nibbles. */
    for(int i=0;i<SU_BASE_BYTES;i++){
        reference[2*i]=i&15;
        reference[2*i+1]=(i>>4)&15;
    }
    su_store_base(packed,reference);
    su_load_base(restored,packed);
    CHECK(!memcmp(restored,reference,SU_BYTES));

    reset();
    const float across[]={4,0,12,0,12,8,4,0,12,8,4,8},b[]={5,1,11,7};
    const float lights[]={8,4,7,255};
    caster(0,0,across,6,b);
    caster(SU_STATIC,4,rect,6,bounds);
    for(int z=0;z<64;z++)for(int x=0;x<64;x++)s->shapes[z*64+x]=(uint8_t)(2*x+2*z);
    for(int z=0;z<16;z++)for(int x=0;x<16;x++)s->terrain[su_terrain_offset(x,z)]=(x+3*z)&3;
    su_terrain_publish(s);
    su_lights(s,lights,1);
    memset(s->clouds,128,sizeof(s->clouds));s->cloud_ready=1;
    su_cloud_style(s,32,255,33,0);
    su_cloud_motion(s,.25f,0);
    su_prepare(s,SU_PAGES);

    int pages=0,lit=0;
    for(int i=0;i<SU_PAGES;i++){
        const SuPage *p=&s->page[i];
        if(!p->needed)continue;
        pages++;
        /* Compose through an unpacked base as before, then compare all IA4
         * texels, including the filtering gutters on both sides of a seam. */
        su_seed_terrain(s,p,reference);
        su_raster(s,p,&s->caster[0],reference);
        su_load_base(restored,s->base+i*SU_BASE_BYTES);
        CHECK(!memcmp(restored,reference,SU_BYTES));
        su_raster(s,p,&s->caster[SU_STATIC],reference);
        su_page_clouds(s,p,reference);
        for(int j=0;j<SU_BYTES;j++)lit+=reference[j]>>4!=0;
        CHECK(!memcmp(s->pixels+i*SU_BYTES,reference,SU_BYTES));
    }
    CHECK(pages>1&&lit>0);
    CHECK(s->pixels-s->base==SU_PAGES*SU_BASE_BYTES);
    CHECK(s->terrain-s->pixels==SU_PAGES*SU_BYTES);
}

static double light_bake_time(void (*bake)(SuState *),int iterations){
    struct timespec start,end;
    assert(!clock_gettime(CLOCK_MONOTONIC,&start));
    for(int i=0;i<iterations;i++){
        s->light_dirty=1;
        bake(s);
    }
    assert(!clock_gettime(CLOCK_MONOTONIC,&end));
    return ((end.tv_sec-start.tv_sec)*1e6+(end.tv_nsec-start.tv_nsec)*1e-3)/iterations;
}

static void light_bake_benchmark(void){
    float sources[SU_LIGHTS*4];
    for(int n=0;n<SU_LIGHTS;n++){
        sources[n*4]=4.25f+(n%6)*19;
        sources[n*4+1]=7.75f+(n/6)*27;
        sources[n*4+2]=n%2?2.7f:2.8f;
        sources[n*4+3]=n%2?255:210;
    }
    const int counts[]={0,1,SU_LIGHTS},iterations=1000;
    for(int i=0;i<3;i++){
        int count=counts[i];
        su_lights(s,sources,count);
        double old=light_bake_time(legacy_bake_lights,iterations);
        uint8_t expected[SU_TERRAIN_BYTES];memcpy(expected,s->terrain,sizeof(expected));
        double bounded=light_bake_time(su_bake_lights,iterations);
        assert(!memcmp(expected,s->terrain,sizeof(expected)));
        printf("Forced bake, %2d sources (%d iterations): legacy %.3f us, bounded %.3f us\n",count,iterations,old,bounded);
    }
}

static void post_shadow_checks(void){
    reset();SuCaster post={.profile=5,.x=4,.z=4};
    CHECK(su_sample(s,&post,4,4)==SU_OPACITY);
    CHECK(su_sample(s,&post,4.5f,4.4f)==SU_OPACITY);
    CHECK(su_sample(s,&post,5.032f,4.8256f)==SU_OPACITY);
    CHECK(su_sample(s,&post,3.5f,3.6f)==0);
    const float b[]={3.4f,3.4f,5.75f,5.55f},quad[]={3.4f,3.4f,5.75f,3.4f,5.75f,5.55f,3.4f,3.4f,5.75f,5.55f,3.4f,5.55f};
    caster(0,5,quad,6,b);su_prepare(s,SU_PAGES);
    CHECK(pixel(5,4.8f)>0&&pixel(3,4)==0);
    CHECK(pixel(5.33f,4.8f)>0); // Padding protects the roof edge from receiver erosion.
    const float half[]={0,0,8,0,0,8};su_mesh(s,0,3,5,half,b);su_prepare(s,SU_PAGES);
    CHECK(pixel(5,4.8f)==0); // Roof projection cannot cross a clipped receiver edge.
    su_mesh(s,0,6,5,quad,b);su_prepare(s,SU_PAGES);CHECK(pixel(5,4.8f)>0);
    su_active(s,0,0,4,4);su_prepare(s,SU_PAGES);CHECK(pixel(5,4.8f)==0);
    /* The post's projection crosses page boundaries without a filtering seam. */
    reset();float uv[12],edge[]={7.42f,7.12f,9.12f,8.62f};
    for(int i=0;i<6;i++){uv[i*2]=rect[i*2]+4;uv[i*2+1]=rect[i*2+1]+4;}
    caster(0,5,uv,6,edge);su_active(s,0,1,7.7f,7.4f);su_prepare(s,SU_PAGES);
    int left=su_find(s,0,1),right=su_find(s,1,1);CHECK(left>=0&&right>=0);
    for(int z=0;z<SU_SIDE;z++)for(int x=0;x<2;x++)CHECK(s->pixels[left*SU_BYTES+su_offset(SU_INNER+x,z)]==s->pixels[right*SU_BYTES+su_offset(x,z)]);
}
static void light_checks(void){
    reset();float sources[]={8,8,3,255,8,8,3,255};
    su_lights(s,sources,1);su_prepare(s,SU_PAGES);
    CHECK(su_field_channel(s->coverage,8,8,1)>200&&su_field_channel(s->coverage,16,8,1)==0);
    CHECK(su_field_sample(s->coverage,8,8)==0);
    uint8_t *saved=malloc(SU_TERRAIN_BYTES);CHECK(saved!=NULL);memcpy(saved,s->coverage,SU_TERRAIN_BYTES);
    su_lights(s,sources,2);su_prepare(s,SU_PAGES);CHECK(!memcmp(saved,s->coverage,SU_TERRAIN_BYTES));free(saved);
    // Static lamps survive moving cloud/actor coverage, including page seams.
    float uv[12];for(int i=0;i<6;i++){uv[i*2]=rect[i*2]+4;uv[i*2+1]=rect[i*2+1]+4;}
    const float b[]={6,6,10,10};caster(32,4,uv,6,b);su_active(s,32,1,8,8);su_prepare(s,SU_PAGES);
    int left=su_find(s,0,1),right=su_find(s,1,1);CHECK(left>=0&&right>=0);
    for(int z=0;z<SU_SIDE;z++)for(int x=0;x<2;x++)CHECK((s->pixels[left*SU_BYTES+su_offset(SU_INNER+x,z)]>>4)==(s->pixels[right*SU_BYTES+su_offset(x,z)]>>4));
    next();su_active(s,32,1,9,9);su_prepare(s,SU_PAGES);CHECK(su_field_channel(s->coverage,8,8,1)>200&&!s->light_dirty);
    su_lights(s,sources,0);su_prepare(s,SU_PAGES);CHECK(su_field_channel(s->coverage,8,8,1)==0);
    su_lights(s,sources,1);su_prepare(s,SU_PAGES);su_begin(s,16,1,4);caster(32,4,rect,6,bounds);su_prepare(s,SU_PAGES);
    int page=su_find(s,0,0);CHECK(page>=0);for(int i=0;i<SU_BYTES;i++)CHECK(!(s->pixels[page*SU_BYTES+i]>>4));
    for(int shade=0;shade<=SU_OPACITY;shade++)for(int light=0;light<=255;light++){
        uint8_t packed=su_pack(shade,light);CHECK(abs(su_shadow(packed)-shade)<=2);CHECK(abs((packed>>4)*17-light)<=8);
    }
}
static void seasonal_cloud_checks(void){
    reset();s->cloud_ready=1;
    for(int z=0;z<SU_CLOUD_SIDE;z++)for(int x=0;x<SU_CLOUD_SIDE;x++)s->clouds[su_cloud_offset(x,z)]=(uint8_t)(x*255/63);
    uint8_t mask[SU_CLOUD_BYTES];memcpy(mask,s->clouds,sizeof(mask));
    int base_middle=0,summer_middle=0,base_core=0,summer_core=0;
    for(int x=0;x<512;x++){
        float px=x/16.f;
        su_cloud_style(s,32,255,66,0);int base=su_cloud_sample(s,px,4);
        su_cloud_style(s,32,64,66,0);int summer=su_cloud_sample(s,px,4);
        base_middle+=base>6&&base<60;summer_middle+=summer>6&&summer<60;
        base_core+=base>=60;summer_core+=summer>=60;
        su_cloud_style(s,64,255,33,0);int winter=su_cloud_sample(s,px*2,8);
        CHECK(abs(winter*2-base)<=1&&winter<=33);
        CHECK(su_cloud_sample(s,px*2-64,8)==winter);
    }
    CHECK(summer_middle*2<base_middle&&summer_core>base_core);
    CHECK(!memcmp(mask,s->clouds,sizeof(mask))); // No second pattern or blurred texture.
    int covered[4]={0};
    const float periods[]={32,32,32,64},edges[]={255,64,255,255},strengths[]={66,66,66,33},cutoffs[]={96,176,0,96};
    for(int season=0;season<4;season++)for(int x=0;x<512;x++){
        su_cloud_style(s,periods[season],edges[season],strengths[season],cutoffs[season]);
        covered[season]+=su_cloud_sample(s,x/512.f*periods[season],4.f*periods[season]/32)>strengths[season]*.1f;
    }
    CHECK(covered[1]<covered[0]&&covered[0]<covered[2]);
    CHECK(abs(covered[0]-covered[3])<=2); // Equal footprint; winter opacity/scale remain independent.
    CHECK(!memcmp(mask,s->clouds,sizeof(mask)));
    su_prepare(s,SU_PAGES);CHECK(!s->cloud_dirty);
    su_cloud_style(s,64,255,33,96);CHECK(!s->cloud_dirty);
    su_cloud_style(s,64,255,33,0);CHECK(s->cloud_dirty); // A cutoff change invalidates coverage even with no wind.
    su_cloud_style(s,64,255,33,0);
    su_cloud_motion(s,.25f,0);CHECK(s->cloud_x==128); // Same 16m drift in a 64m repeat.
    su_prepare(s,SU_PAGES);CHECK(!s->cloud_dirty);
    su_cloud_style(s,64,255,33,0);CHECK(!s->cloud_dirty); // Stable season needs no field rebuild.
    su_cloud_style(s,32,64,66,0);CHECK(s->cloud_dirty);su_cloud_motion(s,.5f,0);CHECK(s->cloud_x==128);
    su_prepare(s,SU_PAGES);CHECK(!s->cloud_dirty);
    // Faint winter clouds cannot weaken cliff/actor coverage or static lamps.
    su_cloud_style(s,64,255,33,0);su_cloud_motion(s,0,0);
    for(int i=0;i<SU_TERRAIN_BYTES;i++)s->terrain[i]=su_pack(SU_OPACITY,0);
    su_terrain_publish(s);float light[]={4,4,3,255};su_lights(s,light,1);caster(32,4,rect,6,bounds);su_prepare(s,SU_PAGES);
    CHECK(pixel(4,4)==SU_OPACITY&&su_field_sample(s->coverage,24,4)==SU_OPACITY);
    CHECK(su_field_channel(s->coverage,4,4,1)>200);
    su_begin(s,16,1,4);caster(32,4,rect,6,bounds);su_prepare(s,SU_PAGES);CHECK(pixel(4,4)==SU_OPACITY&&pixel(1,1)==0);
}
static void night_cloud_checks(void){
    reset();s->cloud_ready=1;memset(s->clouds,255,sizeof s->clouds);
    for(int i=0;i<SU_TERRAIN_BYTES;i++)s->terrain[i]=su_pack(33,0);
    su_terrain_publish(s);float light[]={4,4,3,255};su_lights(s,light,1);
    caster(32,4,rect,6,bounds);su_prepare(s,SU_PAGES);
    CHECK(su_field_sample(s->coverage,24,4)==SU_OPACITY);
    int light_before=su_field_channel(s->coverage,4,4,1);
    su_cloud_style(s,32,255,0,0);su_prepare(s,SU_PAGES);
    CHECK(su_cloud_sample(s,24,4)==0);
    CHECK(su_field_sample(s->coverage,24,4)==su_shadow(su_pack(33,0)));
    CHECK(su_field_channel(s->coverage,4,4,1)==light_before&&light_before>200);
    // Once cleared, sleeping clouds do not dirty fields/pages as wind advances.
    for(int frame=0;frame<1000;frame++){
        su_cloud_motion(s,(frame%32)/32.f,(frame%16)/16.f);
        CHECK(!s->cloud_dirty);su_prepare(s,SU_PAGES);CHECK(!s->changed);
    }
    su_cloud_style(s,64,255,33,96);su_cloud_motion(s,.75f,.5f);su_prepare(s,SU_PAGES);
    CHECK(s->cloud_x==384&&s->cloud_z==256);
    CHECK(su_cloud_sample(s,24,4)==33);
    CHECK(su_field_channel(s->coverage,4,4,1)==light_before);
}
static void cloud_checks(void){
    reset();
    for(int z=0;z<SU_CLOUD_SIDE;z++)for(int x=0;x<SU_CLOUD_SIDE;x++)s->clouds[su_cloud_offset(x,z)]=x<32?0:255;
    s->cloud_ready=s->cloud_dirty=1;su_prepare(s,SU_PAGES);
    // Clouds shade ground without needing a nearby tree/actor page.
    CHECK(su_field_sample(s->coverage,4,4)==0);
    CHECK(su_field_sample(s->coverage,24,4)==SU_OPACITY);
    CHECK(su_cloud_sample(s,-8,4)==su_cloud_sample(s,24,4));
    CHECK(su_cloud_sample(s,24,4)==su_cloud_sample(s,56,36));
    for(int i=0;i<SU_TERRAIN_BYTES;i++)s->terrain[i]=su_pack(33,0);
    su_terrain_publish(s);caster(0,0,rect,6,bounds);caster(32,4,rect,6,bounds);su_prepare(s,SU_PAGES);
    CHECK(pixel(4,4)==SU_OPACITY); // Tree, villager and cliff union never adds.
    memset(s->shapes,0,sizeof(s->shapes));su_dirty(s,&s->caster[0]);next();su_prepare(s,SU_PAGES);
    CHECK(pixel(4,4)==su_shadow(su_pack(33,0)));
    su_cloud_motion(s,.5f,0);su_prepare(s,SU_PAGES);
    CHECK(pixel(4,4)==SU_OPACITY&&su_field_sample(s->coverage,4,4)==SU_OPACITY);
    CHECK(su_field_sample(s->coverage,24,4)==su_shadow(su_pack(33,0))); // Vacated cloud restores the cliff base.
    su_cloud_motion(s,0,0);credits=0;su_prepare_budget(s,0,allow_work,NULL);CHECK(pixel(4,4)==SU_OPACITY); // Only the cloud refresh waits; the last complete page stays visible.
    su_prepare(s,SU_PAGES);CHECK(pixel(4,4)==su_shadow(su_pack(33,0)));
    for(int i=0;i<SU_TERRAIN_BYTES;i++)CHECK(s->coverage[i]<=SU_OPACITY);
    // A cloud crosses an 8 m page seam, including both filtering gutters.
    float uv[12];for(int i=0;i<6;i++){uv[i*2]=rect[i*2]+4;uv[i*2+1]=rect[i*2+1];}
    const float b[]={6,1,10,7};caster(1,0,uv,6,b);su_cloud_motion(s,.25f,0);su_prepare(s,SU_PAGES);
    int left=su_find(s,0,0),right=su_find(s,1,0);CHECK(left>=0&&right>=0);
    for(int y=0;y<SU_SIDE;y++)for(int x=0;x<2;x++)
        CHECK(abs((int)s->pixels[left*SU_BYTES+su_offset(SU_INNER+x,y)]-s->pixels[right*SU_BYTES+su_offset(x,y)])<=1);
    su_begin(s,9,1,8);memset(s->shapes,255,sizeof(s->shapes));caster(32,4,rect,6,bounds);su_prepare(s,SU_PAGES);
    CHECK(pixel(4,4)==SU_OPACITY&&pixel(1,1)==0); // Indoor actors work; outdoor clouds/cliffs never leak in.
}
int main(int argc,char **argv){
    s=aligned_alloc(32,su_storage_bytes());CHECK(s!=NULL);CHECK(su_storage_bytes()<448*1024);
    if(argc==2&&!strcmp(argv[1],"--benchmark")){reset();light_bake_benchmark();cloud_reuse_checks(1);free(s);return 0;}
    native_shadow_combiner_checks();
    post_shadow_checks();
    light_checks();
    light_equivalence_checks();
    static_base_equivalence_checks();
    CHECK(SU_PAGES*SU_BASE_BYTES==80*1024);
    CHECK(SU_PAGES*(SU_BASE_BYTES+SU_BYTES)+SU_TERRAIN_BYTES*2==304*1024);
    reset();caster(0,0,rect,6,bounds);su_prepare(s,SU_PAGES);CHECK(pixel(4,4)==SU_OPACITY);
    uint8_t original[SU_BYTES];int page=su_find(s,0,0);memcpy(original,s->pixels+page*SU_BYTES,SU_BYTES);
    for(int i=1;i<32;i++)caster(i,i%4,rect,6,bounds);
    su_prepare(s,SU_PAGES);CHECK(memcmp(original,s->pixels+page*SU_BYTES,SU_BYTES)==0);
    /* Duplicate trees/houses and opaque moving circles cannot compound. */
    for(int i=32;i<39;i++)caster(i,4,rect,6,bounds);
    su_prepare(s,SU_PAGES);CHECK(pixel(4,4)==SU_OPACITY);
    for(int i=0;i<SU_BYTES;i++)CHECK(s->pixels[page*SU_BYTES+i]<=SU_OPACITY);
    /* Idling does not republish or rasterize mask pages. */
    next();for(int i=32;i<39;i++)su_active(s,i,1,4,4);su_prepare(s,SU_PAGES);CHECK(s->changed==0);
    /* Remove scenery, move every circle, and verify the previous footprint
     * is restored from the static cache rather than leaving dark trails. */
    for(int i=0;i<32;i++)su_active(s,i,0,0,0);
    next();for(int i=32;i<39;i++)su_active(s,i,1,6,6);su_prepare(s,SU_PAGES);CHECK(pixel(4,4)==0);CHECK(pixel(6,6)==SU_OPACITY);
    next();su_prepare(s,SU_PAGES);CHECK(pixel(6,6)==0);
    /* Soft masks merge by maximum, independently of caster draw order. */
    reset();memset(s->shapes,128,4096);memset(s->shapes+4096,192,4096);
    caster(0,0,rect,6,bounds);su_prepare(s,SU_PAGES);int faint=pixel(4,4);
    caster(1,1,rect,6,bounds);su_prepare(s,SU_PAGES);int strong=pixel(4,4);CHECK(strong>faint&&strong<SU_OPACITY);
    su_active(s,0,0,0,0);su_prepare(s,SU_PAGES);CHECK(pixel(4,4)==strong);
    /* Adjacent triangle diagonals and overlapping mat/floor geometry union
     * before the support guard, without cracks or duplicate darkening. */
    reset();caster(0,0,rect,6,bounds);su_prepare(s,SU_PAGES);CHECK(pixel(3,3)==SU_OPACITY);CHECK(pixel(5,5)==SU_OPACITY);
    float duplicate[24];memcpy(duplicate,rect,sizeof(rect));memcpy(duplicate+12,rect,sizeof(rect));
    su_mesh(s,0,12,0,duplicate,bounds);su_prepare(s,SU_PAGES);CHECK(pixel(4,4)==SU_OPACITY);
    /* A cliff half-plane leaves a guard against bilinear sampling onto the
     * disconnected lower surface. No coverage outside the clipped receiver. */
    reset();const float half[]={0,0,8,0,0,8};const float all[]={0,0,8,8};caster(0,0,half,3,all);su_prepare(s,SU_PAGES);
    CHECK(pixel(1,1)==SU_OPACITY);CHECK(pixel(6,6)==0);CHECK(pixel(4,4)==0);
    /* Texture gutters on both sides of a page boundary use identical world
     * sample positions. Native placement never aliases distant world tiles. */
    reset();float across[12];for(int i=0;i<6;i++){across[i*2]=rect[i*2]+4;across[i*2+1]=rect[i*2+1];}
    const float ab[]={6,1,10,7};caster(0,0,across,6,ab);su_prepare(s,SU_PAGES);int left=su_find(s,0,0),right=su_find(s,1,0);CHECK(left>=0&&right>=0);
    CHECK(s->pixels[left*SU_BYTES+su_offset(SU_INNER,SU_SIDE/2-1)]==s->pixels[right*SU_BYTES+su_offset(0,SU_SIDE/2-1)]);
    CHECK(s->pixels[left*SU_BYTES+su_offset(SU_SIDE-1,SU_SIDE/2-1)]==s->pixels[right*SU_BYTES+su_offset(1,SU_SIDE/2-1)]);
    /* A nonuniform source detects coordinate/stride mistakes hidden by a
     * solid profile, across both duplicated gutter columns. */
    for(int y=0;y<64;y++)for(int x=0;x<64;x++)s->shapes[y*64+x]=(uint8_t)(x+y);
    su_dirty(s,&s->caster[0]);su_prepare(s,SU_PAGES);
    for(int y=0;y<SU_SIDE;y++)for(int x=0;x<2;x++)
        CHECK(abs((int)s->pixels[left*SU_BYTES+su_offset(SU_INNER+x,y)]-s->pixels[right*SU_BYTES+su_offset(x,y)])<=1);
    memset(s->shapes,255,sizeof(s->shapes));su_dirty(s,&s->caster[0]);su_prepare(s,SU_PAGES);
    su_begin(s,3,1,4);su_prepare(s,SU_PAGES);CHECK(su_find(s,0,0)<0);su_begin(s,0,1,0);su_prepare(s,SU_PAGES);CHECK(pixel(7,4)==SU_OPACITY);
    /* Worst-case separated footprints: four pages for every caster. */
    reset();for(int i=0;i<SU_CASTERS;i++){
        float x=(i%8+1)*16.f,z=(i/8+1)*16.f,uv[12],b[4]={x-1,z-1,x+1,z+1};
        for(int j=0;j<6;j++){uv[j*2]=rect[j*2]+x-4;uv[j*2+1]=rect[j*2+1]+z-4;}
        su_mesh(s,i,6,i<SU_STATIC?0:4,uv,b);su_active(s,i,1,x,z);
    }su_prepare(s,SU_PAGES);int used=0;for(int i=0;i<SU_PAGES;i++)used+=s->page[i].needed;
    CHECK(used==SU_CASTERS*4);CHECK(s->overflow==0);
    /* Static work stays bounded while dynamic coverage remains current. */
    reset();caster(0,0,rect,6,bounds);caster(32,4,rect,6,bounds);
    su_prepare(s,0);CHECK(s->static_rasters==0);CHECK(pixel(4,4)==SU_OPACITY);
    next();su_active(s,32,1,5,5);su_prepare(s,1);CHECK(s->static_rasters<=SU_STATIC);CHECK(pixel(5,5)==SU_OPACITY);
    next();su_prepare(s,1);CHECK(pixel(4,4)==SU_OPACITY);
    /* Hash collisions, deletion within a probe cluster, and slot recycling. */
    reset();int keys[3],found=0;unsigned hash=su_hash(0,0,0);
    for(int x=0;found<3;x++)if(su_hash(0,x,0)==hash)keys[found++]=x;
    for(int i=0;i<3;i++){s->page[i]=(SuPage){.used=1,.x=keys[i]};su_index(s,i);}
    for(int i=0;i<3;i++)CHECK(su_find(s,keys[i],0)==i);
    su_unindex(s,0);s->page[0].used=0;
    CHECK(su_find(s,keys[0],0)==-1);CHECK(su_find(s,keys[1],0)==1);CHECK(su_find(s,keys[2],0)==2);
    su_unindex(s,1);s->page[1].used=0;CHECK(su_find(s,keys[2],0)==2);
    /* Travel forces repeated eviction; every hash entry must agree with the
     * page keys across outdoor/indoor spaces and resized room pages. */
    reset();for(int frame=0;frame<240;frame++){
        int space=frame%17==0?3:0;su_begin(s,space,1,space?(frame>230?12:4):0);
        float x=frame*8.f+4,z=(frame%7)*8.f+4,uv[12],b[4]={x-3,z-3,x+3,z+3};
        for(int j=0;j<6;j++){uv[j*2]=rect[j*2]+x-4;uv[j*2+1]=rect[j*2+1]+z-4;}
        su_mesh(s,32,6,4,uv,b);su_active(s,32,1,x,z);su_prepare(s,1);
        CHECK(!s->overflow);CHECK(su_find(s,su_floor(x/s->span),su_floor(z/s->span))>=0);
        for(int i=0;i<SU_PAGES;i++)if(s->page[i].used){
            int current=s->space;s->space=s->page[i].space;
            CHECK(su_find(s,s->page[i].x,s->page[i].z)==i);s->space=current;
        }
        su_prepare(s,1);CHECK(!s->layout_dirty);
    }
    /* Moving a circle reuses support; changing its receiver invalidates it. */
    reset();caster(32,4,rect,6,bounds);su_prepare(s,SU_PAGES);
    next();su_active(s,32,1,4.1f,4);su_prepare(s,SU_PAGES);
    CHECK(s->support_builds==0&&s->support_hits>0);
    su_mesh(s,32,3,4,half,all);su_active(s,32,1,6,6);su_prepare(s,SU_PAGES);
    CHECK(s->support_builds>0);CHECK(pixel(6,6)==0);
    /* Moving contact pages cannot blink when the optional field budget is
     * exhausted: current coverage publishes, and the old footprint clears. */
    reset();caster(32,4,rect,6,bounds);su_prepare(s,SU_PAGES);CHECK(pixel(4,4)==SU_OPACITY);
    next();su_active(s,32,1,6,6);credits=0;su_prepare_budget(s,1,allow_work,NULL);
    CHECK(pixel(4,4)==0);CHECK(pixel(6,6)==SU_OPACITY);
    credits=1000;su_prepare_budget(s,1,allow_work,NULL);CHECK(pixel(6,6)==SU_OPACITY);CHECK(pixel(4,4)==0);
    /* Static composition yields between casters. Invalidating a paused job
     * discards its scratch union, so a removed tree cannot reappear. */
    reset();caster(0,0,rect,6,bounds);credits=2;su_prepare_budget(s,1,allow_work,NULL);
    CHECK(s->static_page>=0&&s->static_caster<SU_STATIC);CHECK(pixel(4,4)==0);
    su_active(s,0,0,0,0);credits=1000;su_prepare_budget(s,1,allow_work,NULL);
    credits=1000;su_prepare_budget(s,1,allow_work,NULL);CHECK(pixel(4,4)==0);
    /* The two-triangle flat shortcut and the old subdivided receiver must
     * produce identical final pixels, including the erosion guard. */
    reset();float grid[432];int at=0;
    for(int z=0;z<6;z++)for(int x=0;x<6;x++){
        float v[]={x,z,x+1,z,x+1,z+1,x,z,x+1,z+1,x,z+1};memcpy(grid+at,v,sizeof(v));at+=12;
    }
    const float square[]={0,0,6,0,6,6,0,0,6,6,0,6},box[]={0,0,6,6};
    caster(32,4,grid,216,box);su_prepare(s,SU_PAGES);
    uint8_t *pixels=malloc(SU_PAGES*SU_BYTES);CHECK(pixels!=NULL);memcpy(pixels,s->pixels,SU_PAGES*SU_BYTES);
    su_mesh(s,32,6,4,square,box);su_active(s,32,1,4,4);su_prepare(s,SU_PAGES);
    CHECK(memcmp(pixels,s->pixels,SU_PAGES*SU_BYTES)==0);free(pixels);
    /* Cliff coverage survives object movement, merges by maximum, and never
     * leaks into interiors. Global sampling is independent of page allocation. */
    reset();CHECK((uintptr_t)s->terrain%32==0);memset(s->shapes,0,sizeof(s->shapes));
    for(int z=0;z<32;z++)for(int x=0;x<32;x++)s->terrain[su_terrain_offset(x,z)]=su_pack(33,0);
    CHECK(su_terrain_sample(s,4,4)==0);su_terrain_publish(s);
    CHECK(su_terrain_sample(s,4,4)==su_shadow(su_pack(33,0)));CHECK(su_terrain_sample(s,80,80)==0);
    CHECK(su_terrain_sample(s,-1,4)==0);
    caster(0,0,rect,6,bounds);su_prepare(s,SU_PAGES);CHECK(pixel(4,4)==su_shadow(su_pack(33,0)));
    memset(s->shapes,255,4096);su_dirty(s,&s->caster[0]);su_prepare(s,SU_PAGES);
    CHECK(pixel(4,4)==SU_OPACITY); /* max(33,66), never 99 */
    memset(s->shapes,0,4096);su_dirty(s,&s->caster[0]);su_prepare(s,SU_PAGES);CHECK(pixel(4,4)==su_shadow(su_pack(33,0)));
    su_begin(s,16,1,4);caster(32,4,rect,6,bounds);su_prepare(s,SU_PAGES);
    CHECK(pixel(4,4)==0);
    su_begin(s,0,1,0);su_prepare(s,SU_PAGES);CHECK(pixel(4,4)==su_shadow(su_pack(33,0)));
    /* Contact updates retain the cliff base even when optional work waits. */
    memset(s->shapes+4*4096,255,1024);
    next();caster(32,4,rect,6,bounds);credits=1000;su_prepare_budget(s,1,allow_work,NULL);
    int terrain_page=su_find(s,0,0);
    CHECK(terrain_page>=0&&!s->page[terrain_page].static_pending);
    next();su_active(s,32,1,5,5);credits=0;su_prepare_budget(s,1,allow_work,NULL);
    CHECK(!s->page[terrain_page].dirty&&!s->page[terrain_page].static_dirty&&!s->page[terrain_page].static_pending);
    CHECK(pixel(5,5)==SU_OPACITY);
    CHECK(pixel(4,4)==su_shadow(su_pack(33,0)));
    su_terrain_reset(s);CHECK(!s->terrain_ready&&su_terrain_sample(s,4,4)==0);
    su_prepare(s,SU_PAGES);CHECK(pixel(4,4)==0);
    /* Continuous walking across page seams must retain contact coverage even
     * with zero optional-work credits and moving cloud-field invalidations. */
    reset();int old_bucket=-100;
    for(int frame=0;frame<180;frame++){
        float x=7.4f+frame*.04f,z=4.f;int bucket=(int)floorf(x/2)*2-2;
        next();
        if(bucket!=old_bucket){
            float uv[]={bucket,2,bucket+6,2,bucket+6,8,bucket,2,bucket+6,8,bucket,8};
            float b[]={bucket,2,bucket+6,8};su_mesh(s,32,6,4,uv,b);old_bucket=bucket;
        }
        su_active(s,32,1,x,z);s->cloud_dirty=1;credits=0;su_prepare_budget(s,0,allow_work,NULL);
        CHECK(pixel(x+.12f,z+.1f)>0);CHECK(!s->overflow);
    }
    cloud_reuse_checks(0);
    cloud_checks();
    seasonal_cloud_checks();
    night_cloud_checks();
    printf("Shadow storage: %zu bytes total; %d-byte pages, two %d-byte terrain fields\n",su_storage_bytes(),SU_BYTES,SU_TERRAIN_BYTES);
    free(s);printf("Shadow union: %d checks; max overlap, soft edges, trails, clipping, seams, rooms and %d-page bound passed\n",assertions,SU_PAGES);return 0;
}
