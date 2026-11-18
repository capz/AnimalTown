/* Game ground shader hook. Independent of the villager rim scope. */
extern int animaltown_shadows_mode(void);
extern const uint8_t *animaltown_path_mask(void);
extern const uint8_t *animaltown_shadows_page(float,float,float*);
extern int animaltown_environment_color(void);
extern int animaltown_light_strength(void);
extern int animaltown_shadow_strength(void);
static uint8_t town_shadow_empty[32*32] __attribute__((aligned(32)));
static void town_alpha_pass(int stage){
    GX_SetTevAlphaIn((u8)stage,GX_CA_APREV,GX_CA_ZERO,GX_CA_ZERO,GX_CA_ZERO);
    GX_SetTevAlphaOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
}
/* Blend the shared dirt page on existing ground triangles. A compact I4
 * world mask changes independently of the immutable terrain archive. */
static int town_paths_apply(const DndDrawMaterial *mat,int stage,unsigned *bindings){
    const uint8_t *mask=animaltown_path_mask();
    if(!mask||!mat->secondary)return stage;
    GXTexObj tex;GX_InitTexObj(&tex,(void*)mask,128,128,GX_TF_I4,GX_CLAMP,GX_CLAMP,GX_FALSE);
    GX_InitTexObjLOD(&tex,GX_NEAR,GX_NEAR,0,0,0,GX_FALSE,GX_FALSE,GX_ANISO_1);GX_LoadTexObj(&tex,GX_TEXMAP3);(*bindings)++;
    GX_SetNumTexGens(4);GX_SetTexCoordGen(GX_TEXCOORD1,GX_TG_MTX2x4,GX_TG_TEX0,GX_IDENTITY);
    Mtx uv={{0}};uv[0][0]=uv[1][1]=2.f/128;
    GX_LoadTexMtxImm(uv,GX_TEXMTX7,GX_MTX2x4);GX_SetTexCoordGen(GX_TEXCOORD3,GX_TG_MTX2x4,GX_TG_TEX0,GX_TEXMTX7);
    GX_SetTexCoordScaleManually(GX_TEXCOORD3,GX_FALSE,1,1);GX_SetTexCoordBias(GX_TEXCOORD3,GX_FALSE,GX_FALSE);
    GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
    /* Select the receiver channel consistently across added passes; the
     * mask and following shadow stages consume its alpha. */
    GX_SetTevOrder((u8)stage,GX_TEXCOORD1,GX_TEXMAP1,GX_COLOR0A0);
    GX_SetTevColorIn((u8)stage,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ZERO,GX_CC_TEXC);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVREG1);town_alpha_pass(stage++);
    GX_SetTevSwapModeTable(GX_TEV_SWAP1,GX_CH_RED,GX_CH_GREEN,GX_CH_BLUE,GX_CH_RED);
    GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP1);
    GX_SetTevOrder((u8)stage,GX_TEXCOORD3,GX_TEXMAP3,GX_COLOR0A0);
    GX_SetTevColorIn((u8)stage,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ZERO,GX_CC_CPREV);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
    GX_SetTevAlphaIn((u8)stage,GX_CA_ZERO,GX_CA_TEXA,GX_CA_RASA,GX_CA_ZERO);
    GX_SetTevAlphaOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVREG2);stage++;
    GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
    GX_SetTevOrder((u8)stage,GX_TEXCOORD_NULL,GX_TEXMAP_NULL,GX_COLOR0A0);
    GX_SetTevColorIn((u8)stage,GX_CC_CPREV,GX_CC_C1,GX_CC_A2,GX_CC_ZERO);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);town_alpha_pass(stage++);
    return stage;
}
static GXColor town_environment_tint(void){
    int rgb=animaltown_environment_color(),alpha=(66*animaltown_shadow_strength()+127)/255;
    return (GXColor){rgb>>16,(rgb>>8)&255,rgb&255,(u8)alpha};
}
static int town_environment_apply(const DndDrawEffect *effect,int stage){
    int slot=effect?effect->uniforms:0;if(slot>=4)return stage;
    if(animaltown_environment_color()==0xffffff)return stage;
    /* Wide TEV RGB must reach its ordinary output clamp before entering
     * an 8-bit TEV multiply input. Otherwise highlights wrap instead of tint. */
    if(effect&&effect->count>0&&(effect->stages[effect->count-1].dest&128)&&!(effect->stages[effect->count-1].dest&64)){
        GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
        GX_SetTevOrder((u8)stage,GX_TEXCOORD_NULL,GX_TEXMAP_NULL,GX_COLOR_NULL);
        GX_SetTevColorIn((u8)stage,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ZERO,GX_CC_CPREV);
        GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);town_alpha_pass(stage++);
    }
    GX_SetNumTevStages((u8)(stage+1));GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
    GX_SetTevOrder((u8)stage,GX_TEXCOORD_NULL,GX_TEXMAP_NULL,GX_COLOR_NULL);
    GX_SetTevKColor((u8)slot,town_environment_tint());GX_SetTevKColorSel((u8)stage,GX_TEV_KCSEL_K0+slot);
    GX_SetTevColorIn((u8)stage,GX_CC_ZERO,GX_CC_CPREV,GX_CC_KONST,GX_CC_ZERO);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);town_alpha_pass(stage);
    return stage+1;
}
/* Evaluate the signed green-channel difference before cast-shadow tint.
 * TEV's D input retains the signed register value for the final addition. */
extern const uint8_t *animaltown_snow_mask(void);
static void town_snow_bind(const uint8_t *depth,unsigned *bindings){
    GXTexObj tex;
    GX_InitTexObj(&tex,(void*)depth,512,512,GX_TF_I8,GX_CLAMP,GX_CLAMP,GX_FALSE);
    GX_InitTexObjLOD(&tex,GX_LINEAR,GX_LINEAR,0,0,0,GX_FALSE,GX_FALSE,GX_ANISO_1);
    GX_LoadTexObj(&tex,GX_TEXMAP4);(*bindings)++;
    Mtx uv={{0}};
    uv[0][0]=uv[1][1]=2.f/128;
    uv[1][3]=4.f/512; /* Skip the leading padding used for GX publication. */
    GX_LoadTexMtxImm(uv,GX_TEXMTX4,GX_MTX2x4);
    GX_SetTexCoordGen(GX_TEXCOORD6,GX_TG_MTX2x4,GX_TG_TEX0,GX_TEXMTX4);
    GX_SetTexCoordScaleManually(GX_TEXCOORD6,GX_FALSE,1,1);
    GX_SetTexCoordBias(GX_TEXCOORD6,GX_FALSE,GX_FALSE);
}
static int town_snow_relief(int stage){
    Mtx uv={{0}};
    uv[0][0]=uv[1][1]=2.f/128;
    uv[1][3]=4.f/512;
    const float dx=.4f/512,dz=.3f/512;
    uv[0][3]-=dx;uv[1][3]-=dz;
    GX_LoadTexMtxImm(uv,GX_TEXMTX6,GX_MTX2x4);
    uv[0][3]+=2*dx;uv[1][3]+=2*dz;
    GX_LoadTexMtxImm(uv,GX_TEXMTX7,GX_MTX2x4);
    GX_SetTexCoordGen(GX_TEXCOORD3,GX_TG_MTX2x4,GX_TG_TEX0,GX_TEXMTX6);
    GX_SetTexCoordGen(GX_TEXCOORD4,GX_TG_MTX2x4,GX_TG_TEX0,GX_TEXMTX7);
    GX_SetTexCoordScaleManually(GX_TEXCOORD3,GX_FALSE,1,1);
    GX_SetTexCoordScaleManually(GX_TEXCOORD4,GX_FALSE,1,1);
    GX_SetTexCoordBias(GX_TEXCOORD3,GX_FALSE,GX_FALSE);
    GX_SetTexCoordBias(GX_TEXCOORD4,GX_FALSE,GX_FALSE);
    GX_SetTevSwapModeTable(GX_TEV_SWAP3,GX_CH_GREEN,GX_CH_GREEN,GX_CH_GREEN,GX_CH_GREEN);
    GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP3);
    GX_SetTevOrder((u8)stage,GX_TEXCOORD3,GX_TEXMAP4,GX_COLOR_NULL);
    GX_SetTevColorIn((u8)stage,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ZERO,GX_CC_TEXC);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVREG1);town_alpha_pass(stage++);
    GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP3);
    GX_SetTevOrder((u8)stage,GX_TEXCOORD4,GX_TEXMAP4,GX_COLOR_NULL);
    GX_SetTevColorIn((u8)stage,GX_CC_ZERO,GX_CC_TEXC,GX_CC_ONE,GX_CC_C1);
    GX_SetTevColorOp((u8)stage,GX_TEV_SUB,GX_TB_ZERO,GX_CS_DIVIDE_2,GX_FALSE,GX_TEVREG1);town_alpha_pass(stage++);
    GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
    GX_SetTevOrder((u8)stage,GX_TEXCOORD_NULL,GX_TEXMAP_NULL,GX_COLOR_NULL);
    GX_SetTevColorIn((u8)stage,GX_CC_ZERO,GX_CC_CPREV,GX_CC_ONE,GX_CC_C1);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);town_alpha_pass(stage++);
    return stage;
}
/* TerrainSection marks eligible level grass with vertex alpha 254. The
 * raised pass discards every other surface and every fully compressed texel. */
static void town_snow_cutout(int stage,int depth){
    GX_SetAlphaCompare(GX_EQUAL,254,GX_AOP_AND,GX_ALWAYS,0);
    GX_SetTevSwapModeTable(GX_TEV_SWAP3,GX_CH_GREEN,GX_CH_GREEN,GX_CH_GREEN,GX_CH_GREEN);
    GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP3);
    GX_SetTevOrder((u8)stage,depth?GX_TEXCOORD6:GX_TEXCOORD_NULL,depth?GX_TEXMAP4:GX_TEXMAP_NULL,GX_COLOR0A0);
    GX_SetTevColorIn((u8)stage,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ZERO,GX_CC_CPREV);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
    GX_SetTevKAlphaSel((u8)stage,GX_TEV_KASEL_3_4);
    GX_SetTevAlphaIn((u8)stage,GX_CA_KONST,depth?GX_CA_TEXA:GX_CA_ZERO,GX_CA_RASA,GX_CA_ZERO);
    GX_SetTevAlphaOp((u8)stage,GX_TEV_COMP_A8_GT,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
}
/* Traffic paths now share the ordinary ground mesh. Reuse their existing I4
 * mask to open the snow layer without rebaking geometry when traffic changes. */
static void town_snow_paths(int stage,const uint8_t *mask,unsigned *bindings){
    GXTexObj tex;GX_InitTexObj(&tex,(void*)mask,128,128,GX_TF_I4,GX_CLAMP,GX_CLAMP,GX_FALSE);
    GX_InitTexObjLOD(&tex,GX_NEAR,GX_NEAR,0,0,0,GX_FALSE,GX_FALSE,GX_ANISO_1);GX_LoadTexObj(&tex,GX_TEXMAP3);(*bindings)++;
    Mtx uv={{0}};uv[0][0]=uv[1][1]=2.f/128;
    GX_LoadTexMtxImm(uv,GX_TEXMTX5,GX_MTX2x4);GX_SetTexCoordGen(GX_TEXCOORD5,GX_TG_MTX2x4,GX_TG_TEX0,GX_TEXMTX5);
    GX_SetTexCoordScaleManually(GX_TEXCOORD5,GX_FALSE,1,1);GX_SetTexCoordBias(GX_TEXCOORD5,GX_FALSE,GX_FALSE);
    GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
    GX_SetTevOrder((u8)stage,GX_TEXCOORD5,GX_TEXMAP3,GX_COLOR_NULL);
    GX_SetTevColorIn((u8)stage,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ZERO,GX_CC_CPREV);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
    GX_SetTevAlphaIn((u8)stage,GX_CA_TEXA,GX_CA_ZERO,GX_CA_APREV,GX_CA_ZERO);
    GX_SetTevAlphaOp((u8)stage,GX_TEV_COMP_A8_EQ,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
}
static int town_shadow_lamp_register(const DndDrawEffect *effect){
    /* WaterWarp owns K0/K1; environment and shadow occupy K2/K3. Its authored
     * stages use only PREV, leaving a regular RGB register for the lamp color.
     * Check reads as well as writes so an effect's zero-initialized input is
     * never repurposed. RGB and alpha registers are independent; the preload
     * below preserves the backend's initial alpha zero. Other layouts retain
     * the existing footer policy, not a claim of general effect composition. */
    if(!effect||effect->uniforms!=2)return 0;
    unsigned used=1;
    for(int i=0;i<effect->count;i++){
        const DndTevStage *s=&effect->stages[i];used|=1u<<(s->dest&3);
        const u8 inputs[]={s->a,s->b,s->c,s->d};
        for(int j=0;j<4;j++)if(inputs[j]>=GX_CC_C0&&inputs[j]<=GX_CC_C2&&!(inputs[j]&1))used|=1u<<(inputs[j]/2);
    }
    for(int reg=GX_TEVREG0;reg<=GX_TEVREG2;reg++)if(!(used&(1u<<reg)))return reg;
    return 0;
}
static void town_shadow_apply_stage(float x,float z,const float *world,const DndDrawEffect *effect,unsigned *bindings,int stage){
    int mode=animaltown_shadows_mode();if(!mode)return;
    float origin[6];const uint8_t *pixels=animaltown_shadows_page(x,z,origin);
    static int empty_ready;if(!empty_ready){DCFlushRange(town_shadow_empty,sizeof(town_shadow_empty));empty_ready=1;}
    int snow=mode==4||mode==5;
    const uint8_t *depth=snow?animaltown_snow_mask():NULL;
    const uint8_t *snow_paths=snow?animaltown_path_mask():NULL;
    /* The solid white pass starts at stage zero and skips the ground texture. */
    if(snow)stage=0;
    GXTexObj tex;int width=(int)origin[3],height=(int)origin[5];GX_InitTexObj(&tex,(void*)(pixels?pixels:town_shadow_empty),(u16)width,(u16)height,GX_TF_IA4,GX_CLAMP,GX_CLAMP,GX_FALSE);
    GX_InitTexObjLOD(&tex,GX_LINEAR,GX_LINEAR,0,0,0,GX_FALSE,GX_FALSE,GX_ANISO_1);GX_LoadTexObj(&tex,GX_TEXMAP2);(*bindings)++;
    GX_SetNumTevStages((u8)(stage+4+(depth?3:0)+(snow?1:0)+(snow_paths?1:0)));GX_SetNumTexGens(depth?7:snow_paths?6:mode==1&&stage>(effect?effect->count:1)?4:3);
    if(depth)town_snow_bind(depth,bindings);
    if(!effect||snow)GX_SetTexCoordGen(GX_TEXCOORD1,GX_TG_MTX2x4,GX_TG_TEX0,GX_IDENTITY);
    GX_SetTexCoordScaleManually(GX_TEXCOORD2,GX_FALSE,1,1);GX_SetTexCoordBias(GX_TEXCOORD2,GX_FALSE,GX_FALSE);
    float scaleX=(width-2.f)/(width*origin[2]),scaleZ=(height-2.f)/(height*origin[4]);Mtx uv={{0}};
    if(mode==2){for(int c=0;c<4;c++){uv[0][c]=world[c*4]*scaleX;uv[1][c]=world[c*4+2]*scaleZ;}}
    else{uv[0][0]=(mode==3?4:2)*scaleX;uv[1][1]=(mode==3?4:2)*scaleZ;}
    uv[0][3]+=1.f/width-origin[0]*scaleX;uv[1][3]+=1.f/height-origin[1]*scaleZ;
    GX_LoadTexMtxImm(uv,GX_TEXMTX8,GX_MTX2x4);GX_SetTexCoordGen(GX_TEXCOORD2,GX_TG_MTX2x4,mode==2?GX_TG_POS:GX_TG_TEX0,GX_TEXMTX8);
    GX_SetTevSwapModeTable(GX_TEV_SWAP1,GX_CH_RED,GX_CH_GREEN,GX_CH_BLUE,GX_CH_RED);
    GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP1);
    GX_SetTevOrder((u8)stage,GX_TEXCOORD2,GX_TEXMAP2,GX_COLOR0A0);
    GXColor tint=town_environment_tint();GX_SetTevKColor(GX_KCOLOR2,tint);GX_SetTevKColorSel((u8)stage,GX_TEV_KCSEL_K2);
    GX_SetTevColorIn((u8)stage,GX_CC_ZERO,snow?GX_CC_ONE:GX_CC_CPREV,GX_CC_KONST,GX_CC_ZERO);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
    /* Outdoors both packed-map reads gate on raster alpha. Only the indoor
     * position projection uses a konst alpha input for an always-on receiver. */
    if(mode==2)GX_SetTevKAlphaSel((u8)stage,GX_TEV_KASEL_1);
    GX_SetTevAlphaIn((u8)stage,GX_CA_ZERO,GX_CA_TEXA,mode==2?GX_CA_KONST:GX_CA_RASA,GX_CA_ZERO);
    GX_SetTevAlphaOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVREG2);
    stage++;
    if(depth)stage=town_snow_relief(stage);
    GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
    GX_SetTevOrder((u8)stage,GX_TEXCOORD_NULL,GX_TEXMAP_NULL,GX_COLOR0A0);
    GX_SetTevColorIn((u8)stage,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ZERO,GX_CC_CPREV);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
    GX_SetTevKAlphaSel((u8)stage,GX_TEV_KASEL_K2_A);
    GX_SetTevAlphaIn((u8)stage,GX_CA_ZERO,GX_CA_A2,GX_CA_KONST,GX_CA_ZERO);
    GX_SetTevAlphaOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVREG2);
    stage++;GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
    GX_SetTevOrder((u8)stage,GX_TEXCOORD2,GX_TEXMAP2,GX_COLOR0A0);
    GX_SetTevKColor(GX_KCOLOR3,(GXColor){35*tint.r/255,49*tint.g/255,66*tint.b/255,255});GX_SetTevKColorSel((u8)stage,GX_TEV_KCSEL_K3);
    GX_SetTevColorIn((u8)stage,GX_CC_CPREV,GX_CC_KONST,GX_CC_A2,GX_CC_ZERO);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
    if(mode==2)GX_SetTevKAlphaSel((u8)stage,GX_TEV_KASEL_1);
    GX_SetTevAlphaIn((u8)stage,GX_CA_ZERO,GX_CA_TEXA,mode==2?GX_CA_KONST:GX_CA_RASA,GX_CA_ZERO);
    GX_SetTevAlphaOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVREG2);
    stage++;GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
    GX_SetTevOrder((u8)stage,GX_TEXCOORD_NULL,GX_TEXMAP_NULL,GX_COLOR_NULL);
    int strength=animaltown_light_strength(),lamp_register=town_shadow_lamp_register(effect);
    GXColor lamp={220*strength/255,158*strength/255,74*strength/255,0};
    int lamp_input=GX_CC_KONST;
    if(lamp_register){GX_SetTevColor((u8)lamp_register,lamp);lamp_input=GX_CC_C0+2*(lamp_register-GX_TEVREG0);}
    else{lamp.a=255;GX_SetTevKColor(GX_KCOLOR1,lamp);GX_SetTevKColorSel((u8)stage,GX_TEV_KCSEL_K1);}
    GX_SetTevColorIn((u8)stage,GX_CC_ZERO,lamp_input,GX_CC_A2,GX_CC_CPREV);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);town_alpha_pass(stage);
    if(snow){town_snow_cutout(stage+1,depth!=NULL);if(snow_paths)town_snow_paths(stage+2,snow_paths,bindings);}
}
static inline void town_shadow_apply(float x,float z,const float *world,const DndDrawEffect *effect,unsigned *bindings){
    town_shadow_apply_stage(x,z,world,effect,bindings,effect?effect->count:1);
}
/* Trilinear midpoint samples bracket the real nearest-mip cutoff. */
static int town_mip_line(int stage){
    GX_SetTevKColor(GX_KCOLOR0,(GXColor){255,172,255,148});
    GX_SetTevSwapModeTable(GX_TEV_SWAP1,GX_CH_RED,GX_CH_GREEN,GX_CH_BLUE,GX_CH_RED);
    for(int side=0;side<2;side++,stage++){
        GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP1);
        GX_SetTevOrder((u8)stage,GX_TEXCOORD0,GX_TEXMAP0,side?GX_COLOR_NULL:GX_COLOR0A0);
        GX_SetTevColorIn((u8)stage,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ZERO,GX_CC_CPREV);
        GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
        GX_SetTevKAlphaSel((u8)stage,side?GX_TEV_KASEL_K0_G:GX_TEV_KASEL_K0_A);
        GX_SetTevAlphaIn((u8)stage,side?GX_CA_KONST:GX_CA_TEXA,side?GX_CA_TEXA:GX_CA_KONST,side?GX_CA_A0:GX_CA_RASA,GX_CA_ZERO);
        GX_SetTevAlphaOp((u8)stage,GX_TEV_COMP_A8_GT,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVREG0);
    }
    GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
    GX_SetTevOrder((u8)stage,GX_TEXCOORD_NULL,GX_TEXMAP_NULL,GX_COLOR_NULL);
    GX_SetTevKColorSel((u8)stage,GX_TEV_KCSEL_K0);
    GX_SetTevColorIn((u8)stage,GX_CC_CPREV,GX_CC_KONST,GX_CC_A0,GX_CC_ZERO);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
    town_alpha_pass(stage);
    return stage+1;
}
static inline void town_shadow_ground(const DndDrawVertex *v,const uint16_t *ix,int count,const DndDrawMaterial *mat,unsigned *bindings){
    int mode=animaltown_shadows_mode();if(mode!=1&&mode!=3&&mode!=4&&mode!=5&&mode!=6){town_environment_apply(mat->effect,mat->effect?mat->effect->count:1);return;}if(count<3)return;
    int a=0,b=1,c=2;
    for(int i=0;i+2<count;i+=3){a=ix?ix[i]:i;b=ix?ix[i+1]:i+1;c=ix?ix[i+2]:i+2;if(v[a].color&255u)break;}
    /* Every cached ground batch belongs to one 8x8m shadow page. A triangle's
     * interior identifies its 8m page even when a vertex lies on its border. */
    float factor=(mode==3?4.f:2.f)/3;
    int stage=mat->effect?mat->effect->count:1;
    if(mode==1)stage=town_paths_apply(mat,stage,bindings);
    if(mode==6)stage=town_mip_line(stage);
    town_shadow_apply_stage((v[a].u+v[b].u+v[c].u)*factor,(v[a].v+v[b].v+v[c].v)*factor,NULL,mat->effect,bindings,stage);
}
