#include <assert.h>
#include <float.h>
#include <stdio.h>
#include <stdlib.h>
#include <time.h>
#include "../tools/native-shadow-union.h"

/* Independent pre-optimization sampler and complete mask bake. Keep the
 * interpolation/rounding order verbatim: approximate periodicity is not enough. */
static uint8_t cloud_reuse_scalar_sample(const SuState *s,float x,float z){
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

static void cloud_reuse_scalar_prepare(SuState *s){
    su_bake_lights(s);
    if(!s->cloud_dirty)return;
    for(int y=0;y<SU_TERRAIN_HEIGHT;y++)for(int x=0;x<SU_TERRAIN_WIDTH;x++){
        int at=su_terrain_offset(x,y);uint8_t a=s->terrain_ready?su_shadow(s->terrain[at]):0;
        uint8_t b=x<=SU_WORLD_WIDTH+1?cloud_reuse_scalar_sample(s,(x-.5f)/SU_TERRAIN_TEXELS_PER_METRE,(y-.5f)/SU_TERRAIN_TEXELS_PER_METRE):0;
        s->coverage[at]=su_pack(a>b?a:b,(s->terrain[at]>>4)*17);
    }
    for(int i=0;i<SU_PAGES;i++)if(s->page[i].used&&!s->page[i].space)s->page[i].dirty|=1;
    s->cloud_dirty=0;
}

static SuState *cloud_reuse_state,*cloud_reuse_reference;
static unsigned long cloud_reuse_assertions,cloud_reuse_mask_comparisons;
#define CLOUD_REUSE_CHECK(x) do{assert(x);cloud_reuse_assertions++;}while(0)
static uint32_t cloud_reuse_random_state=0x1472caf3u;
static uint32_t cloud_reuse_random_word(void){cloud_reuse_random_state=cloud_reuse_random_state*1664525u+1013904223u;return cloud_reuse_random_state;}
static const float cloud_reuse_periods[]={32,32,32,64},cloud_reuse_edges[]={255,64,255,255},cloud_reuse_strengths[]={66,66,66,33},cloud_reuse_cutoffs[]={96,176,0,96};

static void cloud_reuse_reset(void){
    su_init(cloud_reuse_state);su_init(cloud_reuse_reference);
    for(int i=0;i<SU_CLOUD_BYTES;i++)cloud_reuse_state->clouds[i]=cloud_reuse_reference->clouds[i]=(uint8_t)(cloud_reuse_random_word()>>24);
    /* Every shadow/light-nibble pairing, distributed independently from the
     * source-cloud repeat; padding is deliberately nonzero too. */
    for(int i=0;i<SU_TERRAIN_BYTES;i++)cloud_reuse_state->terrain[i]=cloud_reuse_reference->terrain[i]=(uint8_t)(cloud_reuse_random_word()>>24);
    cloud_reuse_state->cloud_ready=cloud_reuse_reference->cloud_ready=1;
    cloud_reuse_state->terrain_ready=cloud_reuse_reference->terrain_ready=1;
    for(int i=0;i<SU_PAGES;i++){
        cloud_reuse_state->page[i].used=cloud_reuse_reference->page[i].used=i%3!=0;
        cloud_reuse_state->page[i].space=cloud_reuse_reference->page[i].space=i%2;
    }
}

static void cloud_reuse_style(float period,float edge,float strength,float cutoff){
    su_cloud_style(cloud_reuse_state,period,edge,strength,cutoff);
    su_cloud_style(cloud_reuse_reference,period,edge,strength,cutoff);
}

static void cloud_reuse_compare(int x,int z,int terrain,int ready){
    cloud_reuse_state->cloud_x=cloud_reuse_reference->cloud_x=x;cloud_reuse_state->cloud_z=cloud_reuse_reference->cloud_z=z;
    cloud_reuse_state->terrain_ready=cloud_reuse_reference->terrain_ready=terrain;
    cloud_reuse_state->cloud_ready=cloud_reuse_reference->cloud_ready=ready;
    cloud_reuse_state->cloud_dirty=cloud_reuse_reference->cloud_dirty=1;
    for(int i=0;i<SU_PAGES;i++)cloud_reuse_state->page[i].dirty=cloud_reuse_reference->page[i].dirty=(uint8_t)(i&2);
    cloud_reuse_scalar_prepare(cloud_reuse_reference);su_cloud_prepare(cloud_reuse_state);
    CLOUD_REUSE_CHECK(!memcmp(cloud_reuse_state->terrain,cloud_reuse_reference->terrain,SU_TERRAIN_BYTES));
    CLOUD_REUSE_CHECK(!memcmp(cloud_reuse_state->coverage,cloud_reuse_reference->coverage,SU_TERRAIN_BYTES));
    CLOUD_REUSE_CHECK(!memcmp(cloud_reuse_state->clouds,cloud_reuse_reference->clouds,SU_CLOUD_BYTES));
    CLOUD_REUSE_CHECK(cloud_reuse_state->cloud_dirty==cloud_reuse_reference->cloud_dirty&&cloud_reuse_state->light_dirty==cloud_reuse_reference->light_dirty);
    for(int i=0;i<SU_PAGES;i++)CLOUD_REUSE_CHECK(cloud_reuse_state->page[i].dirty==cloud_reuse_reference->page[i].dirty);
    cloud_reuse_mask_comparisons++;
}

static void cloud_reuse_exact_coordinate_checks(void){
    _Static_assert(FLT_RADIX==2&&FLT_MANT_DIG>=24,"periodic-coordinate proof requires binary32 or better");
    /* Exhaust EVERY legal quantized drift on each axis and every destination
     * coordinate, including both cloud gutters and unused terrain padding.
     * Equality of index plus bit-identical fraction proves equivalence for
     * arbitrary source bytes, seasonal contrast and final rounding thresholds. */
    for(int repeat=32;repeat<=64;repeat*=2){
        float scale=64.f/repeat;
        for(int offset=0;offset<repeat*8;offset++)for(int n=0;n<SU_TERRAIN_WIDTH;n++){
            float original=((n-.5f)+offset*.125f)*scale-.5f;
            float reused=(((n&(repeat-1))-.5f)+offset*.125f)*scale-.5f;
            int a=su_floor(original),b=su_floor(reused);
            float fraction_a=original-a,fraction_b=reused-b;
            CLOUD_REUSE_CHECK((a&63)==(b&63));
            CLOUD_REUSE_CHECK(!memcmp(&fraction_a,&fraction_b,sizeof(fraction_a)));
        }
    }
}

static void cloud_reuse_seasonal_checks(void){
    cloud_reuse_reset();
    const float lamps[]={-.5f,-.5f,2,255, 32,64,3.5f,210, 64,32,4.5f,128, 128.5f,127.5f,8,255};
    su_lights(cloud_reuse_state,lamps,4);su_lights(cloud_reuse_reference,lamps,4);
    for(int season=0;season<4;season++){
        cloud_reuse_style(cloud_reuse_periods[season],cloud_reuse_edges[season],cloud_reuse_strengths[season],cloud_reuse_cutoffs[season]);
        int units=(int)(cloud_reuse_periods[season]*8),phases=season==3?8:4;
        CLOUD_REUSE_CHECK(su_cloud_repeat(cloud_reuse_state)==(int)cloud_reuse_periods[season]);
        for(int phase=0;phase<phases;phase++)for(int drift=0;drift<units;drift++){
            /* Coprime sweeps cross every horizontal/vertical motion bucket;
             * the outer loop also crosses all fractional-weight pairs. */
            int z=(drift*37+phase)%units;
            cloud_reuse_compare(drift,z,drift%3!=0,drift%97!=0);
        }
        for(int sunlight=0;sunlight<=255;sunlight++){
            cloud_reuse_style(cloud_reuse_periods[season],cloud_reuse_edges[season],cloud_reuse_strengths[season]*sunlight/255.f,cloud_reuse_cutoffs[season]);
            cloud_reuse_compare((sunlight*17)%units,(sunlight*43)%units,1,1);
        }
        /* Motion still quantizes correctly on either side of the wrap and
         * immediately around representable bucket boundaries. */
        for(int bucket=0;bucket<=units;bucket++){
            float at=bucket/(float)units;
            const float around[]={nextafterf(at,0.f),at,nextafterf(at,1.f)};
            for(int j=0;j<3;j++)if(around[j]>=0&&around[j]<1){
                su_cloud_motion(cloud_reuse_state,around[j],around[j]);
                su_cloud_motion(cloud_reuse_reference,around[j],around[j]);
                CLOUD_REUSE_CHECK(cloud_reuse_state->cloud_x==cloud_reuse_reference->cloud_x&&cloud_reuse_state->cloud_z==cloud_reuse_reference->cloud_z);
                cloud_reuse_compare(cloud_reuse_state->cloud_x,cloud_reuse_state->cloud_z,1,1);
            }
        }
    }
    /* Republish new terrain and lights while motion remains unchanged. */
    cloud_reuse_state->terrain[0]=cloud_reuse_reference->terrain[0]=255;
    su_terrain_publish(cloud_reuse_state);su_terrain_publish(cloud_reuse_reference);
    su_lights(cloud_reuse_state,lamps,0);su_lights(cloud_reuse_reference,lamps,0);
    cloud_reuse_compare(0,0,1,1);
    su_cloud_prepare(cloud_reuse_state);CLOUD_REUSE_CHECK(!cloud_reuse_state->cloud_dirty);
}

static void cloud_reuse_rounding_checks(void){
    cloud_reuse_reset();
    const uint8_t values[]={0,1,2,63,64,65,95,96,97,127,128,129,175,176,177,254,255};
    for(unsigned v=0;v<sizeof(values);v++){
        memset(cloud_reuse_state->clouds,values[v],SU_CLOUD_BYTES);memset(cloud_reuse_reference->clouds,values[v],SU_CLOUD_BYTES);
        for(int season=0;season<4;season++){
            cloud_reuse_style(cloud_reuse_periods[season],cloud_reuse_edges[season],cloud_reuse_strengths[season],cloud_reuse_cutoffs[season]);
            for(int phase=0;phase<8;phase++)cloud_reuse_compare(phase,(phase*3)%8,0,1);
        }
    }
    /* Binary-exact half-output ties, plus the adjacent representable floats.
     * Do not tolerate even a one-byte error at a rounding threshold. */
    memset(cloud_reuse_state->clouds,64,SU_CLOUD_BYTES);memset(cloud_reuse_reference->clouds,64,SU_CLOUD_BYTES);
    for(int repeat=32;repeat<=64;repeat*=2)for(int n=0;n<16;n++){
        cloud_reuse_style((float)repeat,255,66,0);
        float tie=(n+.5f)/64;
        const float opacity[]={nextafterf(tie,0.f),tie,nextafterf(tie,1.f)};
        for(int i=0;i<3;i++){
            cloud_reuse_state->cloud_opacity=cloud_reuse_reference->cloud_opacity=opacity[i];
            cloud_reuse_compare(0,0,0,1);
        }
    }
}

static void cloud_reuse_fallback_checks(void){
    cloud_reuse_reset();
    const float unsupported[]={8,16,24,nextafterf(32,0),nextafterf(32,64),48,nextafterf(64,0),nextafterf(64,128),96,128};
    for(unsigned i=0;i<sizeof(unsupported)/sizeof(*unsupported);i++){
        cloud_reuse_style(unsupported[i],64,66,176);CLOUD_REUSE_CHECK(!su_cloud_repeat(cloud_reuse_state));
        for(int j=0;j<16;j++)cloud_reuse_compare(j*3,j*5,j%2,1);
    }
    for(int repeat=32;repeat<=64;repeat*=2){
        cloud_reuse_style((float)repeat,255,66,0);
        /* Huge direct offsets lose half/eighth fractions: intentionally
         * outside the proven path, even though the scale itself is supported. */
        const int offsets[]={-1,repeat*8,(1<<26)+1,-(1<<26)+1};
        for(unsigned i=0;i<sizeof(offsets)/sizeof(*offsets);i++){
            cloud_reuse_state->cloud_x=offsets[i];CLOUD_REUSE_CHECK(!su_cloud_repeat(cloud_reuse_state));
            cloud_reuse_compare(offsets[i],0,1,1);
            cloud_reuse_state->cloud_x=0;cloud_reuse_state->cloud_z=offsets[i];CLOUD_REUSE_CHECK(!su_cloud_repeat(cloud_reuse_state));
            cloud_reuse_compare(0,offsets[i],1,1);
        }
    }
}

#ifdef ANIMALTOWN_CLOUD_COUNT_SAMPLES
/* Compiler instrumentation counts actual sampler entries without adding a
 * hook, counter or indirect call to the production rasterizer. */
static unsigned long cloud_reuse_sample_calls;
void __attribute__((no_instrument_function)) __cyg_profile_func_enter(void *function,void *caller){
    (void)caller;if(function==(void*)su_cloud_sample)cloud_reuse_sample_calls++;
}
void __attribute__((no_instrument_function)) __cyg_profile_func_exit(void *function,void *caller){(void)function;(void)caller;}

static void cloud_reuse_count_checks(void){
    cloud_reuse_reset();
    for(int season=0;season<4;season++){
        cloud_reuse_style(cloud_reuse_periods[season],cloud_reuse_edges[season],cloud_reuse_strengths[season],cloud_reuse_cutoffs[season]);
        cloud_reuse_state->cloud_x=cloud_reuse_state->cloud_z=0;cloud_reuse_state->cloud_dirty=1;cloud_reuse_sample_calls=0;
        su_cloud_prepare(cloud_reuse_state);
        unsigned long expected=season==3?4096:1024;
        CLOUD_REUSE_CHECK(cloud_reuse_sample_calls==expected);
        printf("Cloud season %d: %lu actual sampler calls, scalar 16640, avoided %lu\n",season,cloud_reuse_sample_calls,16640-cloud_reuse_sample_calls);
    }
    cloud_reuse_style(48,255,66,0);cloud_reuse_state->cloud_dirty=1;cloud_reuse_sample_calls=0;su_cloud_prepare(cloud_reuse_state);
    CLOUD_REUSE_CHECK(cloud_reuse_sample_calls==16640);puts("Unsupported period: all 16640 scalar sampler calls retained");
}
#else
static double cloud_reuse_bake_time(void (*bake)(SuState*),int iterations){
    struct timespec start,end;
    CLOUD_REUSE_CHECK(!clock_gettime(CLOCK_MONOTONIC,&start));
    for(int i=0;i<iterations;i++){cloud_reuse_state->cloud_dirty=1;bake(cloud_reuse_state);}
    CLOUD_REUSE_CHECK(!clock_gettime(CLOCK_MONOTONIC,&end));
    return ((end.tv_sec-start.tv_sec)*1e6+(end.tv_nsec-start.tv_nsec)*1e-3)/iterations;
}

static double cloud_reuse_median(double *values,int count){
    for(int i=1;i<count;i++)for(int j=i;j>0&&values[j]<values[j-1];j--){double swap=values[j];values[j]=values[j-1];values[j-1]=swap;}
    return values[count/2];
}

static void cloud_reuse_benchmark(void){
    cloud_reuse_reset();
    const float benchmark_periods[]={32,64,48};
    for(unsigned p=0;p<sizeof(benchmark_periods)/sizeof(*benchmark_periods);p++){
        cloud_reuse_style(benchmark_periods[p],255,benchmark_periods[p]==64?33:66,96);cloud_reuse_state->cloud_x=5;cloud_reuse_state->cloud_z=7;
        double original[5],reuse[5];
        for(int trial=0;trial<5;trial++){
            original[trial]=cloud_reuse_bake_time(cloud_reuse_scalar_prepare,256);
            uint8_t expected[SU_TERRAIN_BYTES];memcpy(expected,cloud_reuse_state->coverage,sizeof(expected));
            reuse[trial]=cloud_reuse_bake_time(su_cloud_prepare,256);
            CLOUD_REUSE_CHECK(!memcmp(expected,cloud_reuse_state->coverage,sizeof(expected)));
        }
        printf("Host CPU only, period %.0f, median of 5 x 256 forced updates: scalar %.3f us, reuse %.3f us\n",
               benchmark_periods[p],cloud_reuse_median(original,5),cloud_reuse_median(reuse,5));
    }
}
#endif

static void cloud_reuse_checks(int benchmark){
    cloud_reuse_state=aligned_alloc(32,su_storage_bytes());cloud_reuse_reference=aligned_alloc(32,su_storage_bytes());CLOUD_REUSE_CHECK(cloud_reuse_state&&cloud_reuse_reference);
    /* Keep helpers referenced in the instrumentation-only fixture as well. */
    if(0){cloud_reuse_exact_coordinate_checks();cloud_reuse_seasonal_checks();cloud_reuse_rounding_checks();cloud_reuse_fallback_checks();}
#ifdef ANIMALTOWN_CLOUD_COUNT_SAMPLES
    (void)benchmark;
    cloud_reuse_count_checks();
#else
    if(benchmark)cloud_reuse_benchmark();
    else{cloud_reuse_exact_coordinate_checks();cloud_reuse_seasonal_checks();cloud_reuse_rounding_checks();cloud_reuse_fallback_checks();}
#endif
    printf("Cloud reuse: %lu checks, %lu byte-exact complete-mask comparisons; no production allocation or extra state\n",cloud_reuse_assertions,cloud_reuse_mask_comparisons);
    free(cloud_reuse_reference);free(cloud_reuse_state);
}

#undef CLOUD_REUSE_CHECK
