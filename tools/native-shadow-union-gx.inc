/* Game ground shader hook. Independent of the villager rim scope. */
extern int animaltown_shadows_mode(void);
extern const uint8_t *animaltown_shadows_page(float,float,float*);
static uint8_t town_shadow_empty[4096] __attribute__((aligned(32)));
static void town_shadow_apply(float x,float z,const float *world,const DndDrawEffect *effect,unsigned *bindings){
    int mode=animaltown_shadows_mode();if(!mode)return;
    float origin[4];const uint8_t *pixels=animaltown_shadows_page(x,z,origin);
    static int empty_ready;if(!empty_ready){DCFlushRange(town_shadow_empty,sizeof(town_shadow_empty));empty_ready=1;}
    GXTexObj tex;int side=pixels?(int)origin[3]:64;GX_InitTexObj(&tex,(void*)(pixels?pixels:town_shadow_empty),(u16)side,(u16)side,GX_TF_I8,GX_CLAMP,GX_CLAMP,GX_FALSE);
    GX_InitTexObjLOD(&tex,GX_LINEAR,GX_LINEAR,0,0,0,GX_FALSE,GX_FALSE,GX_ANISO_1);GX_LoadTexObj(&tex,GX_TEXMAP2);(*bindings)++;
    int stage=effect?effect->count:1;
    GX_SetNumTevStages((u8)(stage+2));GX_SetNumTexGens(3);
    if(!effect)GX_SetTexCoordGen(GX_TEXCOORD1,GX_TG_MTX2x4,GX_TG_TEX0,GX_IDENTITY);
    GX_SetTexCoordScaleManually(GX_TEXCOORD2,GX_FALSE,1,1);GX_SetTexCoordBias(GX_TEXCOORD2,GX_FALSE,GX_FALSE);
    float scale=(side-2.f)/(side*origin[2]);Mtx uv={{0}};
    if(mode==2){for(int c=0;c<4;c++){uv[0][c]=world[c*4]*scale;uv[1][c]=world[c*4+2]*scale;}}
    else{uv[0][0]=uv[1][1]=(mode==3?4:2)*scale;}
    uv[0][3]+=1.f/side-origin[0]*scale;uv[1][3]+=1.f/side-origin[1]*scale;
    GX_LoadTexMtxImm(uv,GX_TEXMTX8,GX_MTX2x4);GX_SetTexCoordGen(GX_TEXCOORD2,GX_TG_MTX2x4,mode==2?GX_TG_POS:GX_TG_TEX0,GX_TEXMTX8);
    /* Texture intensity is already coverage*shared opacity. Raster alpha is
     * a receiver flag outdoors: caps=255, vertical lips/banks=0. */
    GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
    GX_SetTevOrder((u8)stage,GX_TEXCOORD2,GX_TEXMAP2,GX_COLOR0A0);
    GX_SetTevColorIn((u8)stage,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ZERO);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVREG2);
    GX_SetTevKAlphaSel((u8)stage,GX_TEV_KASEL_1);
    GX_SetTevAlphaIn((u8)stage,GX_CA_ZERO,GX_CA_TEXA,mode==2?GX_CA_KONST:GX_CA_RASA,GX_CA_ZERO);
    GX_SetTevAlphaOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVREG2);
    stage++;GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
    GX_SetTevOrder((u8)stage,GX_TEXCOORD_NULL,GX_TEXMAP_NULL,GX_COLOR_NULL);
    GX_SetTevKColor(GX_KCOLOR3,(GXColor){35,49,66,255});GX_SetTevKColorSel((u8)stage,GX_TEV_KCSEL_K3);
    GX_SetTevColorIn((u8)stage,GX_CC_CPREV,GX_CC_KONST,GX_CC_A2,GX_CC_ZERO);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
    GX_SetTevAlphaIn((u8)stage,GX_CA_ZERO,GX_CA_ZERO,GX_CA_ZERO,GX_CA_APREV);
    GX_SetTevAlphaOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
}
static inline void town_shadow_ground(const DndDrawVertex *v,const uint16_t *ix,int count,const DndDrawMaterial *mat,unsigned *bindings){
    int mode=animaltown_shadows_mode();if((mode!=1&&mode!=3)||count<3)return;
    int a=0,b=1,c=2;
    for(int i=0;i+2<count;i+=3){a=ix?ix[i]:i;b=ix?ix[i+1]:i+1;c=ix?ix[i+2]:i+2;if(v[a].color&255u)break;}
    /* Every cached ground batch belongs to one 8x4m section. A triangle's
     * interior identifies its 8m page even when a vertex lies on its border. */
    float factor=(mode==3?4.f:2.f)/3;
    town_shadow_apply((v[a].u+v[b].u+v[c].u)*factor,(v[a].v+v[b].v+v[c].v)*factor,NULL,mat->effect,bindings);
}
