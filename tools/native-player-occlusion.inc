/* Included after the pinned model renderer's batch state declarations.
 * Native indexed geometry and evaluated skin buffers are borrowed unchanged.
 * No allocation, readback, depth writes, extra skinning or category queries. */
static bool town_occluded_player;
int animaltown_player_occlusion(int enabled) {
    if((enabled!=0&&enabled!=1)||!dnd_model_frame_active)return -1;
    town_occluded_player=enabled!=0;
#if defined(DND_GAMECUBE_STORAGE) || defined(DND_MODEL_GX_TEST)
    /* Neither the normal model batch nor ordinary meshes may inherit the
     * overridden pipeline. The next draw must set up its complete material. */
    batch_model=NULL;batch_material=NULL;
    model_batch_vertex_ready=false;model_batch_light_ready=false;
    dnd_draw_backend_mesh_changed();
    GX_SetZMode(GX_TRUE,GX_LEQUAL,GX_TRUE);
#endif
    return enabled;
}
#if defined(DND_GAMECUBE_STORAGE) || defined(DND_MODEL_GX_TEST)
static void town_player_white(int stages,const DndMaterial *material) {
    /* Keep every stage's alpha calculation, UVs and texture samplers. Replace
     * only RGB, including imported multiply/emission material combinations. */
    for(int stage=0;stage<stages;stage++) {
        GX_SetTevColorIn(stage,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ZERO,GX_CC_ONE);
        GX_SetTevColorOp(stage,GX_TEV_ADD,GX_TB_ZERO,GX_CS_SCALE_1,GX_TRUE,GX_TEVPREV);
    }
    GX_SetZMode(GX_TRUE,GX_GREATER,GX_FALSE);
    GX_SetBlendMode(GX_BM_NONE,GX_BL_ONE,GX_BL_ZERO,GX_LO_CLEAR);
    /* Preserve authored cutouts. Blended surfaces become solid white wherever
     * alpha is nonzero; fully transparent texels/vertices stay transparent. */
    GX_SetAlphaCompare(material->mode&1?GX_GEQUAL:GX_GREATER,
        material->mode&1?(u8)(material->cutoff*255):0,GX_AOP_AND,GX_ALWAYS,0);
}
#endif
