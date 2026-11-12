/* Position-generated UVs keep retained and immediate fills/tails seamless.
 * Only the UI-owned texture is affected; font/portrait atlases keep authored UVs. */
extern int animaltown_ui_noise_match(const void*);
static void town_ui_noise_apply(const DndDrawTexture *texture,const float *world){
    if(!texture||!animaltown_ui_noise_match(texture->data))return;
    Mtx uv={{world[0]/32,world[4]/32,world[8]/32,world[12]/32},
            {world[1]/32,world[5]/32,world[9]/32,world[13]/32},{0,0,0,1}};
    GX_LoadTexMtxImm(uv,GX_TEXMTX9,GX_MTX2x4);
    GX_SetTexCoordGen(GX_TEXCOORD0,GX_TG_MTX2x4,GX_TG_POS,GX_TEXMTX9);
    /* Grain modulates RGB only: preserve the original shape's alpha exactly. */
    GX_SetTevAlphaIn(GX_TEVSTAGE0,GX_CA_ZERO,GX_CA_ZERO,GX_CA_ZERO,GX_CA_RASA);
    GX_SetTevAlphaOp(GX_TEVSTAGE0,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
}
