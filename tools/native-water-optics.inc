/* Inland-water optics, included by the pinned GX renderer and recording tests.
 * All allocations belong to the SDK resource table. No geometry is expanded,
 * no framebuffer is cleared, and no texture is uploaded during a frame.
 * The opaque bed/wet-rock capture deliberately precedes every dry world draw.
 */
#include "dnd_screen_projection.h"

enum { TOWN_WATER_SIDE=64,TOWN_WATER_MAP_BYTES=8192,TOWN_WATER_SETTINGS=12,
       TOWN_WATER_MAX_CAPTURE_BYTES=640*528/2 };
static int town_optics_slopes,town_optics_lookup,town_optics_capture;
static int town_optics_active,town_optics_scope,town_optics_capture_valid;
static int town_optics_source_w,town_optics_source_h,town_optics_attempt_w,town_optics_attempt_h;
static int town_optics_copies,town_optics_bumps,town_optics_refractions,town_optics_fallbacks;
static int town_optics_stages,town_optics_indirect,town_optics_features,town_optics_base,town_optics_failures;
static unsigned town_optics_part,town_optics_captured_part;
static float town_optics_settings[TOWN_WATER_SETTINGS];
static int town_optics_sunlight,town_optics_environment=0xffffff;
static uint32_t town_optics_clear=0x95c6baffu;

extern int animaltown_shadows_mode(void);
extern int animaltown_environment_color(void);
static float town_optics_clamp(float x,float a,float b){return fmaxf(a,fminf(b,x));}
static int town_optics_byte(float x){return (int)(town_optics_clamp(x,0,1)*255.f+.5f);}
static void town_optics_retire_capture(void){
    if(town_optics_capture)dnd_draw_release(town_optics_capture);
    town_optics_capture=town_optics_capture_valid=0;
    town_optics_source_w=town_optics_source_h=town_optics_attempt_w=town_optics_attempt_h=0;
    town_optics_captured_part=0;
}
int animaltown_water_shutdown(void){
    town_optics_active=town_optics_scope=0;town_optics_retire_capture();
    if(town_optics_lookup)dnd_draw_release(town_optics_lookup);
    if(town_optics_slopes)dnd_draw_release(town_optics_slopes);
    town_optics_slopes=town_optics_lookup=0;
    dnd_draw_backend_invalidate_cache();return 1;
}
int animaltown_water_initialize(DndArray *slopes){
    if(dnd_device_active()||dnd_raw_depth()||!slopes||slopes->elements_are_references||
       slopes->element_size!=1||slopes->length!=TOWN_WATER_MAP_BYTES)return 0;
    animaltown_water_shutdown();
    town_optics_slopes=dnd_texture_allocate_native(64,64,GX_TF_IA8,1);
    if(!town_optics_slopes||dnd_texture_update_native(town_optics_slopes,slopes,0,TOWN_WATER_MAP_BYTES)<0)goto failed;
    town_optics_lookup=dnd_texture_allocate_native(64,64,GX_TF_IA8,1);
    DndDrawTexture lookup;
    if(!town_optics_lookup||dnd_texture_resolve(town_optics_lookup,&lookup)<0)goto failed;
    /* Fixed stylized sunlight and directional view. The lookup reconstructs
     * the upper normal hemisphere. I is a Blinn lobe, A is a Fresnel-shaped
     * environment response; their gains stay independent at draw time. */
    float lx=-.25f,ly=.70f,lz=-.67f;
    float inv=1.f/sqrtf(lx*lx+ly*ly+lz*lz);lx*=inv;ly*=inv;lz*=inv;
    float hx=lx,hy=ly+.6f,hz=lz+.8f;
    inv=1.f/sqrtf(hx*hx+hy*hy+hz*hz);hx*=inv;hy*=inv;hz*=inv;
    unsigned char *pixels=(unsigned char*)lookup.data;
    for(int y=0;y<64;y++)for(int x=0;x<64;x++){
        float nx=(x+.5f)/32.f-1.f,nz=(y+.5f)/32.f-1.f,r=nx*nx+nz*nz;
        float ny=sqrtf(fmaxf(0,1.f-r));
        float spec=r<=1?fmaxf(0,nx*hx+ny*hy+nz*hz):0;
        for(int power=0;power<6;power++)spec*=spec;
        float f=1.f-town_optics_clamp(ny*.6f+nz*.8f,0,1),f2=f*f;
        float sheen=.04f+.96f*f2*f2*f;
        unsigned at=(((unsigned)y/4*16+(unsigned)x/4)*16+(unsigned)y%4*4+(unsigned)x%4)*2;
        pixels[at]=(unsigned char)town_optics_byte(sheen);pixels[at+1]=(unsigned char)town_optics_byte(spec);
    }
    dnd_draw_backend_flush(pixels,TOWN_WATER_MAP_BYTES,2);
    return 1;
failed:
    town_optics_failures++;animaltown_water_shutdown();return 0;
}
int animaltown_water_prepare(DndArray *settings,int active,int sunlight,int environment){
    if(dnd_device_active()||dnd_raw_depth())return 0;
    town_optics_capture_valid=0;town_optics_scope=0;
    if(!active){town_optics_active=0;town_optics_retire_capture();dnd_draw_backend_invalidate_cache();return 0;}
    if(!settings||settings->elements_are_references||settings->element_size!=4||settings->length!=TOWN_WATER_SETTINGS||
       !town_optics_slopes||!town_optics_lookup){town_optics_active=0;return 0;}
    const float *p=(const float*)settings->data;
    for(int i=0;i<TOWN_WATER_SETTINGS;i++)if(!isfinite(p[i])){town_optics_active=0;return 0;}
    if(p[1]<1.f||p[4]<=0||p[7]<=0){town_optics_active=0;return 0;}
    memcpy(town_optics_settings,p,sizeof town_optics_settings);
    town_optics_settings[8]=town_optics_clamp(p[8],0,.35f);
    /* The 8-row split-AA overlap covers this 4-pixel maximum plus filtering. */
    town_optics_settings[9]=town_optics_clamp(p[9],0,4);
    town_optics_settings[10]=town_optics_clamp(p[10],0,1);
    town_optics_settings[11]=town_optics_clamp(p[11],0,1);
    town_optics_sunlight=sunlight<0?0:sunlight>255?255:sunlight;
    town_optics_environment=environment&0xffffff;town_optics_active=1;
    int w=dnd_device_width(),h=dnd_device_height();
    if(town_optics_capture&&(w!=town_optics_source_w||h!=town_optics_source_h))town_optics_retire_capture();
    if(!town_optics_capture&&(w!=town_optics_attempt_w||h!=town_optics_attempt_h)){
        town_optics_attempt_w=w;town_optics_attempt_h=h;
        if(w>=8&&h>=8&&w<=640&&h<=528&&!((w|h)&7)&&w*h/2<=TOWN_WATER_MAX_CAPTURE_BYTES){
            town_optics_capture=dnd_texture_allocate_native(w/2,h/2,GX_TF_RGB565,1);
            if(town_optics_capture){town_optics_source_w=w;town_optics_source_h=h;}
            else town_optics_failures++;
        }
    }
    dnd_draw_backend_invalidate_cache();return town_optics_capture?3:1;
}
int animaltown_water_begin(void){
    town_optics_scope=town_optics_capture_valid=0;
    town_optics_features=town_optics_stages=town_optics_indirect=town_optics_base=0;
    town_optics_part=(town_optics_part+1u)&0x7fffffffu;if(!town_optics_part)town_optics_part=1;
    town_optics_captured_part=0;dnd_draw_backend_invalidate_cache();return town_optics_active;
}
int animaltown_water_scope(int scope){
    int previous=town_optics_scope;
    town_optics_scope=scope>=0&&scope<=2?scope:0;
    dnd_draw_backend_invalidate_cache();return previous;
}
int animaltown_water_capture(void){
    DndDrawTexture texture;
    if(!town_optics_active||!town_optics_capture||!dnd_device_active()||dnd_device_pass()!=1||dnd_raw_depth()||
       !town_optics_part||dnd_texture_resolve(town_optics_capture,&texture)<0){town_optics_capture_valid=0;return 0;}
    if(town_optics_capture_valid&&town_optics_captured_part==town_optics_part)return 1;
    if(dnd_device_width()!=town_optics_source_w||dnd_device_height()!=town_optics_source_h){town_optics_capture_valid=0;return 0;}
    /* This existing SDK helper handles each full-AA resident band in the
     * full logical capture, then CopyTex(false), PixModeSync, DrawDone and
     * texture-cache invalidation. Full logical projection remains correct. */
    dnd_draw_backend_copy(&texture,0,0,town_optics_source_w,town_optics_source_h);
    town_optics_capture_valid=1;town_optics_captured_part=town_optics_part;town_optics_copies++;
    dnd_draw_backend_invalidate_cache();return 1;
}
int animaltown_water_stats(int kind){
    DndDrawTexture texture;
    int capture=town_optics_capture&&dnd_texture_resolve(town_optics_capture,&texture)==0;
    switch(kind){
        case 0:return (town_optics_slopes?8192:0)+(town_optics_lookup?8192:0)+(capture?(int)texture.bytes:0);
        case 1:return town_optics_capture_valid&&town_optics_captured_part==town_optics_part;
        case 2:return town_optics_copies;case 3:return town_optics_bumps;case 4:return town_optics_refractions;
        case 5:return town_optics_fallbacks;case 6:return town_optics_stages;case 7:return town_optics_indirect;
        case 8:return capture?texture.width:0;case 9:return capture?texture.height:0;
        case 10:return town_optics_slopes&&town_optics_lookup;case 11:return town_optics_scope;
        case 12:return town_optics_source_w;case 13:return town_optics_source_h;
        case 14:return (int)town_optics_part;case 15:return (int)town_optics_captured_part;
        case 16:return town_optics_features;case 17:return town_optics_base;case 18:return town_optics_failures;
        case 19:return town_optics_sunlight;case 20:return town_optics_environment;
        default:return 0;
    }
}
float animaltown_water_setting(int index){return index>=0&&index<TOWN_WATER_SETTINGS?town_optics_settings[index]:0;}
static void town_water_optics_clear(uint32_t rgba){town_optics_clear=rgba;}

static void town_optics_alpha(int stage,int d,int dest){
    GX_SetTevAlphaIn((u8)stage,GX_CA_ZERO,GX_CA_ZERO,GX_CA_ZERO,(u8)d);
    GX_SetTevAlphaOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,(u8)dest);
}
static void town_optics_stage(int stage,int coord,int map,int a,int b,int c,int d,int dest){
    GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
    GX_SetTevOrder((u8)stage,(u8)coord,(u32)map,GX_COLOR0A0);
    GX_SetTevColorIn((u8)stage,(u8)a,(u8)b,(u8)c,(u8)d);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,(u8)dest);
    town_optics_alpha(stage,GX_CA_APREV,GX_TEVPREV);
}
static void town_optics_bind(const DndDrawTexture *texture,int map,int filter,unsigned *bindings){
    GXTexObj object;GX_InitTexObj(&object,(void*)texture->data,(u16)texture->width,(u16)texture->height,(u8)texture->format,
        map==3?GX_REPEAT:GX_CLAMP,map==3?GX_REPEAT:GX_CLAMP,GX_FALSE);
    GX_InitTexObjLOD(&object,(u8)filter,(u8)filter,0,0,0,GX_FALSE,GX_FALSE,GX_ANISO_1);
    GX_LoadTexObj(&object,(u8)map);(*bindings)++;
}
static void town_optics_coord(int coord,int slot,int source,Mtx matrix,int three){
    GX_LoadTexMtxImm(matrix,(u32)slot,three?GX_MTX3x4:GX_MTX2x4);
    GX_SetTexCoordGen((u16)coord,three?GX_TG_MTX3x4:GX_TG_MTX2x4,(u32)source,(u32)slot);
    GX_SetTexCoordScaleManually((u8)coord,GX_FALSE,1,1);GX_SetTexCoordBias((u8)coord,GX_FALSE,GX_FALSE);
}
static GXColor town_optics_capture_clear(void){
    /* RGB565 copy/texture expansion, with dither disabled by ordinary draw state. */
    unsigned r=town_optics_clear>>27,g=(town_optics_clear>>18)&63,b=(town_optics_clear>>11)&31;
    return (GXColor){(u8)((r<<3)|(r>>2)),(u8)((g<<2)|(g>>4)),(u8)((b<<3)|(b>>2)),0};
}
static void town_water_optics_apply(const DndDrawMaterial *material,const float *world,unsigned *bindings){
    const DndDrawEffect *effect=material->effect;
    int count=effect?effect->count:1,shadow=animaltown_shadows_mode();
    int base=count+(shadow==3?4:animaltown_environment_color()!=0xffffff?1:0);
    if(town_optics_scope==1){
        /* Only the first rock pass uses its original waterline raster alpha.
         * The later ordinary pass restores the dry caps with normal opacity. */
        town_optics_alpha(base-1,GX_CA_RASA,GX_TEVPREV);
        GX_SetAlphaCompare(GX_GREATER,0,GX_AOP_AND,GX_ALWAYS,0);return;
    }
    if(town_optics_scope!=2)return;
    town_optics_base=base;town_optics_stages=base;town_optics_features=0;town_optics_indirect=effect&&effect->indirect_strength?1:0;
    DndDrawTexture slopes,lookup,capture;
    if(!town_optics_active||!effect||count!=4||effect->uniforms!=2||!effect->indirect_strength||
       (material->flags&3)!=1||!(material->flags&8)||!material->secondary||base>8||
       dnd_texture_resolve(town_optics_slopes,&slopes)<0||dnd_texture_resolve(town_optics_lookup,&lookup)<0){town_optics_fallbacks++;return;}
    int refract=town_optics_capture_valid&&town_optics_captured_part==town_optics_part&&
        dnd_texture_resolve(town_optics_capture,&capture)==0;
    const float *p=town_optics_settings;
    town_optics_bind(&slopes,3,GX_LINEAR,bindings);town_optics_bind(&lookup,4,GX_LINEAR,bindings);
    if(refract){town_optics_bind(&capture,5,GX_LINEAR,bindings);town_optics_bind(&capture,6,GX_NEAR,bindings);}
    /* Preserve coord2/matrix8 for the legacy shadow footer. No indexed normal
     * attribute or expanded vertex path is needed for this cylindrical world. */
    if(shadow!=3){GX_SetTexCoordGen(GX_TEXCOORD2,GX_TG_MTX2x4,GX_TG_TEX0,GX_IDENTITY);GX_SetTexCoordScaleManually(GX_TEXCOORD2,GX_FALSE,1,1);GX_SetTexCoordBias(GX_TEXCOORD2,GX_FALSE,GX_FALSE);}
    Mtx wave0={{p[4],0,0,p[2]},{0,p[4],0,p[3]},{0,0,1,0}};
    Mtx wave1={{p[7],0,0,p[5]},{0,p[7],0,p[6]},{0,0,1,0}};
    town_optics_coord(3,GX_TEXMTX2,GX_TG_TEX0,wave0,0);town_optics_coord(4,GX_TEXMTX3,GX_TG_TEX0,wave1,0);
    float s=.5f/p[1];
    Mtx normal={{0,0,0,.5f},{world[2]*s,world[6]*s,world[10]*s,.5f+(world[14]-p[0])*s},{0,0,0,1}};
    town_optics_coord(5,GX_TEXMTX4,GX_TG_POS,normal,0);
    if(refract){
        float mv[16],clip[16];multiply(mv,view_matrix,world);multiply(clip,projection_matrix,mv);
        Mtx screen;dnd_screen_projection_matrix(screen,clip,NULL);town_optics_coord(6,GX_TEXMTX5,GX_TG_POS,screen,1);
    }
    GX_SetNumTexGens(refract?7:6);GX_SetNumIndStages(3);
    GX_SetIndTexOrder(GX_INDTEXSTAGE1,GX_TEXCOORD3,GX_TEXMAP3);
    GX_SetIndTexOrder(GX_INDTEXSTAGE2,GX_TEXCOORD4,GX_TEXMAP3);
    GX_SetIndTexCoordScale(GX_INDTEXSTAGE1,GX_ITS_1,GX_ITS_1);GX_SetIndTexCoordScale(GX_INDTEXSTAGE2,GX_ITS_1,GX_ITS_1);
    /* IA8 indirect .abg is (alphaX,intensityZ,intensityZ). Each full signed
     * axis contributes p[8] normal units, or 32*p[8] lookup texels. */
    float gain=p[8]*.25f;f32 bump[2][3]={{gain,0,0},{0,gain,0}};GX_SetIndTexMatrix(GX_ITM_1,bump,0);
    float pixels=p[9]/256.f;f32 warp[2][3]={{0,pixels,0},{0,0,pixels}};GX_SetIndTexMatrix(GX_ITM_2,warp,0);
    /* Konst registers are global to the entire draw: preserve the authored
     * ripple RGB and shadow K2/K3. Only unused K0/K1 alpha carry new gains. */
    uint32_t first=effect->colors[0],second=effect->colors[1];
    GX_SetTevKColor(GX_KCOLOR0,(GXColor){first>>24,first>>16,first>>8,(u8)town_optics_byte(p[10]*town_optics_sunlight/255.f)});
    GX_SetTevKColor(GX_KCOLOR1,(GXColor){second>>24,second>>16,second>>8,(u8)town_optics_byte(p[11])});
    GXColor clear=town_optics_capture_clear();GX_SetTevColor(GX_TEVREG2,clear);
    /* Base stage1 initializes C0; neither the base nor footer reads A0. */
    GX_SetTevColor(GX_TEVREG0,(GXColor){0,0,0,96});
    int stage=base;
    town_optics_stage(stage,GX_TEXCOORD5,GX_TEXMAP_DISABLE,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ZERO,GX_CC_CPREV,GX_TEVPREV);
    GX_SetTevIndirect((u8)stage++,GX_INDTEXSTAGE1,GX_ITF_8,GX_ITB_ST,GX_ITM_1,GX_ITW_0,GX_ITW_0,GX_FALSE,GX_FALSE,GX_ITBA_OFF);
    town_optics_stage(stage,GX_TEXCOORD5,GX_TEXMAP4,GX_CC_ZERO,GX_CC_TEXC,GX_CC_KONST,GX_CC_CPREV,GX_TEVPREV);
    GX_SetTevKColorSel((u8)stage,GX_TEV_KCSEL_K0_A);
    GX_SetTevIndirect((u8)stage,GX_INDTEXSTAGE2,GX_ITF_8,GX_ITB_ST,GX_ITM_1,GX_ITW_OFF,GX_ITW_OFF,GX_TRUE,GX_FALSE,GX_ITBA_OFF);
    GX_SetTevKAlphaSel((u8)stage,GX_TEV_KASEL_K1_A);
    GX_SetTevAlphaIn((u8)stage,GX_CA_ZERO,GX_CA_TEXA,GX_CA_KONST,GX_CA_ZERO);
    GX_SetTevAlphaOp((u8)stage++,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVREG2);
    town_optics_stage(stage++,GX_TEXCOORD_NULL,GX_TEXMAP_NULL,GX_CC_CPREV,GX_CC_C2,GX_CC_A2,GX_CC_ZERO,GX_TEVPREV);
    town_optics_features=1|(p[11]>0?2:0);town_optics_bumps++;
    if(refract){
        town_optics_stage(stage,GX_TEXCOORD6,GX_TEXMAP5,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ZERO,GX_CC_TEXC,GX_TEVREG0);
        GX_SetTevIndirect((u8)stage,GX_INDTEXSTAGE0,GX_ITF_8,GX_ITB_TU,GX_ITM_2,GX_ITW_OFF,GX_ITW_OFF,GX_FALSE,GX_FALSE,GX_ITBA_OFF);
        /* Shore weight is clamp(4*(opacity-96),0,255). Its conservative fade
         * reaches 192 at deepest alpha144, retaining a small undistorted share. */
        GX_SetTevKAlphaSel((u8)stage,GX_TEV_KASEL_1);
        GX_SetTevAlphaIn((u8)stage,GX_CA_ZERO,GX_CA_A0,GX_CA_KONST,GX_CA_RASA);
        GX_SetTevAlphaOp((u8)stage++,GX_TEV_SUB,GX_TB_ZERO,GX_CS_SCALE_4,GX_TRUE,GX_TEVREG1);
        town_optics_stage(stage,GX_TEXCOORD6,GX_TEXMAP6,GX_CC_TEXC,GX_CC_C2,GX_CC_A1,GX_CC_ZERO,GX_TEVREG1);
        GX_SetTevColorOp((u8)stage,GX_TEV_COMP_BGR24_EQ,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVREG1);
        GX_SetTevIndirect((u8)stage++,GX_INDTEXSTAGE0,GX_ITF_8,GX_ITB_TU,GX_ITM_2,GX_ITW_OFF,GX_ITW_OFF,GX_FALSE,GX_FALSE,GX_ITBA_OFF);
        town_optics_stage(stage,GX_TEXCOORD_NULL,GX_TEXMAP_NULL,GX_CC_ZERO,GX_CC_C1,GX_CC_ONE,GX_CC_A1,GX_TEVREG1);
        GX_SetTevColorOp((u8)stage++,GX_TEV_SUB,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVREG1);
        town_optics_stage(stage++,GX_TEXCOORD6,GX_TEXMAP5,GX_CC_TEXC,GX_CC_C0,GX_CC_C1,GX_CC_ZERO,GX_TEVREG0);
        town_optics_stage(stage,GX_TEXCOORD_NULL,GX_TEXMAP_NULL,GX_CC_C0,GX_CC_CPREV,GX_CC_RASA,GX_CC_ZERO,GX_TEVPREV);
        GX_SetTevKAlphaSel((u8)stage,GX_TEV_KASEL_1);town_optics_alpha(stage++,GX_CA_KONST,GX_TEVPREV);
        GX_SetBlendMode(GX_BM_NONE,GX_BL_ONE,GX_BL_ZERO,GX_LO_CLEAR);
        town_optics_features|=4;town_optics_refractions++;
    }else{
        /* The lookup alpha belongs to optics, never to surface coverage. */
        town_optics_alpha(stage-1,GX_CA_RASA,GX_TEVPREV);town_optics_fallbacks++;
    }
    GX_SetNumTevStages((u8)stage);town_optics_stages=stage;town_optics_indirect=3;
}
