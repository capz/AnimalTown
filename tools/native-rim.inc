/* Included before the pinned imported-model renderer. Game policy, not an SDK
 * fork. All view-angle shading is GX normal TexGen + TEV, including skins.
 * One immutable 64x64 I8 lookup, outside the resource handle table. */
#include "native-rim-policy.h"
static int town_rim_enabled,town_rim_intensity=100;
static int town_rim_ready;
static unsigned char town_rim_pixels[64*64] __attribute__((aligned(32)));
int animaltown_rim_enable(int percent){
  town_rim_intensity=town_rim_percent(percent);town_rim_enabled=town_rim_intensity>0;
#if defined(DND_GAMECUBE_STORAGE) || defined(DND_MODEL_GX_TEST)
  if(town_rim_enabled&&!town_rim_ready){
    for(int y=0;y<64;y++)for(int x=0;x<64;x++){
      float nx=(x+.5f)/32-1,ny=(y+.5f)/32-1,r=nx*nx+ny*ny;
      float f=1-sqrtf(fmaxf(0,1-fminf(1,r)));
      /* Narrow, smooth grazing-angle band; intensity comes from K3. */
      f=f*f*f;
      unsigned offset=((y/4)*8+x/8)*32+(y%4)*8+x%8;
      town_rim_pixels[offset]=(unsigned char)(255*f);
    }
    DCFlushRange(town_rim_pixels,sizeof(town_rim_pixels));town_rim_ready=1;
  }
  /* Crossing the scope boundary must invalidate imported material reuse. */
  batch_model=NULL;batch_material=NULL;model_batch_vertex_ready=false;
#endif
  return town_rim_enabled;
}
#if defined(DND_GAMECUBE_STORAGE) || defined(DND_MODEL_GX_TEST)
static void town_rim_apply(const float normal[16],const DndDrawEffect *effect,bool reuse,unsigned *bindings){
  if(!reuse){
    GXTexObj tex;GX_InitTexObj(&tex,town_rim_pixels,64,64,GX_TF_I8,GX_CLAMP,GX_CLAMP,GX_FALSE);
    GX_InitTexObjLOD(&tex,GX_LINEAR,GX_LINEAR,0,0,0,GX_FALSE,GX_FALSE,GX_ANISO_1);
    GX_LoadTexObj(&tex,GX_TEXMAP2);(*bindings)++;
    int stage=effect?effect->count:1;
    GX_SetNumTevStages((u8)(stage+1));
    GX_SetTevDirect((u8)stage);GX_SetTevSwapMode((u8)stage,GX_TEV_SWAP0,GX_TEV_SWAP0);
    GX_SetTevOrder((u8)stage,GX_TEXCOORD2,GX_TEXMAP2,GX_COLOR0A0);
    GX_SetTevKColor(GX_KCOLOR3,(GXColor){town_rim_channel(92,town_rim_intensity),town_rim_channel(108,town_rim_intensity),town_rim_channel(124,town_rim_intensity),255});
    GX_SetTevKColorSel((u8)stage,GX_TEV_KCSEL_K3);
    GX_SetTevColorIn((u8)stage,GX_CC_ZERO,GX_CC_TEXC,GX_CC_KONST,GX_CC_CPREV);
    GX_SetTevColorOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
    /* A masks signed-wide APREV to its final GX byte before clamping. */
    GX_SetTevAlphaIn((u8)stage,GX_CA_APREV,GX_CA_ZERO,GX_CA_ZERO,GX_CA_ZERO);
    GX_SetTevAlphaOp((u8)stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
    GX_SetNumTexGens(3);
    GX_SetTexCoordScaleManually(GX_TEXCOORD2,GX_FALSE,1,1);
    GX_SetTexCoordBias(GX_TEXCOORD2,GX_FALSE,GX_FALSE);
    /* Coordinate 1 may be unused, but all enabled generators are defined. */
    if(!effect)GX_SetTexCoordGen(GX_TEXCOORD1,GX_TG_MTX2x4,GX_TG_TEX0,GX_IDENTITY);
  }
  Mtx n,post={{.5f,0,0,.5f},{0,.5f,0,.5f},{0,0,0,1}};
  for(int r=0;r<3;r++)for(int c=0;c<4;c++)n[r][c]=c<3?normal[r*4+c]:0;
  GX_LoadTexMtxImm(n,GX_TEXMTX9,GX_MTX3x4);
  GX_LoadTexMtxImm(post,GX_DTTMTX0,GX_MTX3x4);
  /* Normalize AFTER inverse-transpose, then bias normal XY into the lookup.
   * Refresh this matrix for every instance/node even when materials reuse. */
  GX_SetTexCoordGen2(GX_TEXCOORD2,GX_TG_MTX3x4,GX_TG_NRM,GX_TEXMTX9,GX_TRUE,GX_DTTMTX0);
}
#endif
