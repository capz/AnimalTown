/* Game ground shader hook. Independent of the villager rim scope. */
extern int animaltown_shadows_mode(void);
extern const uint8_t *animaltown_path_mask(void);
extern const uint8_t *animaltown_shadows_page(float,float,float*);
extern int animaltown_environment_color(void);
extern int animaltown_light_strength(void);
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
    /* Keep the receiver channel alive through every added pass. A NULL
     * raster channel here makes the later mask/shadow RASA read zero on GX. */
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
static GXColor town_environment_tint(void){int rgb=animaltown_environment_color();return (GXColor){rgb>>16,(rgb>>8)&255,rgb&255,66};}
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
static void town_shadow_apply_stage(float x,float z,const float *world,const DndDrawEffect *effect,unsigned *bindings,int stage){
    int mode=animaltown_shadows_mode();if(!mode)return;
    float origin[6];const uint8_t *pixels=animaltown_shadows_page(x,z,origin);
    static int empty_ready;if(!empty_ready){DCFlushRange(town_shadow_empty,sizeof(town_shadow_empty));empty_ready=1;}
    GXTexObj tex;int width=(int)origin[3],height=(int)origin[5];GX_InitTexObj(&tex,(void*)(pixels?pixels:town_shadow_empty),(u16)width,(u16)height,GX_TF_IA4,GX_CLAMP,GX_CLAMP,GX_FALSE);
    GX_InitTexObjLOD(&tex,GX_LINEAR,GX_LINEAR,0,0,0,GX_FALSE,GX_FALSE,GX_ANISO_1);GX_LoadTexObj(&tex,GX_TEXMAP2);(*bindings)++;
    GX_SetNumTevStages((u8)(stage+4));GX_SetNumTexGens(stage>(effect?effect->count:1)?4:3);
    if(!effect)GX_SetTexCoordGen(GX_TEXCOORD1,GX_TG_MTX2x4,GX_TG_TEX0,GX_IDENTITY);
    GX_SetTexCoordScaleManually(GX_TEXCOORD2,GX_FALSE,1,1);GX_SetTexCoordBias(GX_TEXCOORD2,GX_FALSE,GX_FALSE);
    float scaleX=(width-2.f)/(width*origin[2]),scaleZ=(height-2.f)/(height*origin[4]);Mtx uv={{0}};
    if(mode==2){for(int c=0;c<4;c++){uv[0][c]=world[c*4]*scaleX;uv[1][c]=world[c*4+2]*scaleZ;}}
    else{uv[0][0]=(mode==3?4:2)*scaleX;uv[1][1]=(mode==3?4:2)*scaleZ;}
    uv[0][3]+=1.f/width-origin[0]*scaleX;uv[1][3]+=1.f/height-origin[1]*scaleZ;
    GX_LoadTexMtxImm(uv,GX_TEXMTX8,GX_MTX2x4);GX_SetTexCoordGen(GX_TEXCOORD2,GX_TG_MTX2x4,mode==2?GX_TG_POS:GX_TG_TEX0,GX_TEXMTX8);
    /* IA4 retains the existing one-byte texel: I=shadow, A=static light.
     * First sample I through texture alpha; receiver alpha rejects cliff lips. */
    GX_SetTevSwapModeTable(GX_TEV_SWAP1,GX_CH_RED,GX_CH_GREEN,GX_CH_BLUE,GX_CH_RED);
    GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP1);
    GX_SetTevOrder((u8)stage,GX_TEXCOORD2,GX_TEXMAP2,GX_COLOR0A0);
    GXColor tint=town_environment_tint();GX_SetTevKColor(GX_KCOLOR2,tint);GX_SetTevKColorSel((u8)stage,GX_TEV_KCSEL_K2);
    GX_SetTevColorIn((u8)stage,GX_CC_ZERO,GX_CC_CPREV,GX_CC_KONST,GX_CC_ZERO);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
    GX_SetTevKAlphaSel((u8)stage,GX_TEV_KASEL_1);
    GX_SetTevAlphaIn((u8)stage,GX_CA_ZERO,GX_CA_TEXA,mode==2?GX_CA_KONST:GX_CA_RASA,GX_CA_ZERO);
    GX_SetTevAlphaOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVREG2);
    stage++;GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
    GX_SetTevOrder((u8)stage,GX_TEXCOORD_NULL,GX_TEXMAP_NULL,GX_COLOR_NULL);
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
    GX_SetTevKAlphaSel((u8)stage,GX_TEV_KASEL_1);
    GX_SetTevAlphaIn((u8)stage,GX_CA_ZERO,GX_CA_TEXA,mode==2?GX_CA_KONST:GX_CA_RASA,GX_CA_ZERO);
    GX_SetTevAlphaOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVREG2);
    stage++;GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
    GX_SetTevOrder((u8)stage,GX_TEXCOORD_NULL,GX_TEXMAP_NULL,GX_COLOR_NULL);
    int strength=animaltown_light_strength();GX_SetTevKColor(GX_KCOLOR1,(GXColor){220*strength/255,158*strength/255,74*strength/255,255});GX_SetTevKColorSel((u8)stage,GX_TEV_KCSEL_K1);
    GX_SetTevColorIn((u8)stage,GX_CC_ZERO,GX_CC_KONST,GX_CC_A2,GX_CC_CPREV);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);town_alpha_pass(stage);
}
static inline void town_shadow_apply(float x,float z,const float *world,const DndDrawEffect *effect,unsigned *bindings){
    town_shadow_apply_stage(x,z,world,effect,bindings,effect?effect->count:1);
}
static inline void town_shadow_ground(const DndDrawVertex *v,const uint16_t *ix,int count,const DndDrawMaterial *mat,unsigned *bindings){
    int mode=animaltown_shadows_mode();if(mode!=1&&mode!=3){town_environment_apply(mat->effect,mat->effect?mat->effect->count:1);return;}if(count<3)return;
    int a=0,b=1,c=2;
    for(int i=0;i+2<count;i+=3){a=ix?ix[i]:i;b=ix?ix[i+1]:i+1;c=ix?ix[i+2]:i+2;if(v[a].color&255u)break;}
    /* Every cached ground batch belongs to one 8x8m shadow page. A triangle's
     * interior identifies its 8m page even when a vertex lies on its border. */
    float factor=(mode==3?4.f:2.f)/3;
    int stage=mat->effect?mat->effect->count:1;
    if(mode==1)stage=town_paths_apply(mat,stage,bindings);
    town_shadow_apply_stage((v[a].u+v[b].u+v[c].u)*factor,(v[a].v+v[b].v+v[c].v)*factor,NULL,mat->effect,bindings,stage);
}
