/* Opt-in for permanently resident static building parts. The SDK already shares
 * decoded textures, but the original package still embeds another pixel copy.
 * Keep the exact geometry section; parsed nodes/materials/authored state and
 * shared texture data already have independent ownership. No quality changes. */
static int town_retained_buildings[DND_MODEL_SLOTS];
static int town_building_slot(const DndModel *m){for(int i=0;i<DND_MODEL_SLOTS;i++)if(dnd_models[i]==m)return i;return -1;}
static bool town_building_retained(const DndModel *m){int i=town_building_slot(m);return i>=0&&town_retained_buildings[i]==m->token;}
int animaltown_building_retain(int token){
    DndModel *m=dnd_model_find(token,false);
    if(!m||!m->ready||m->restoring||m->file||dnd_device_active())return -1;
    if(town_building_retained(m))return 1;
    if(m->refs!=1||m->skins||m->clips||!m->meshes||!m->package||m->length<160)return -1;
    const unsigned char *dir=m->package+32;
    unsigned offset=dnd_model_u32(dir+4),length=dnd_model_u32(dir+8);
    if(dnd_model_u32(dir)!=2||offset>m->length||length>m->length-offset)return -1;
    const unsigned char *base=m->package+offset;
    for(unsigned i=0;i<m->meshes;i++){
        DndMesh *mesh=&m->mesh[i];
        if(mesh->skin!=-1||mesh->data<base||mesh->index<base||
           (size_t)(mesh->data-base)>length||(size_t)(mesh->index-base)>length||
           (size_t)mesh->vertices*80>length-(size_t)(mesh->data-base)||
           (size_t)mesh->indices*4>length-(size_t)(mesh->index-base))return -1;
    }
    unsigned char *compact=dnd_model_alloc(length,&m->bytes,m->budget);if(!compact)return -2;
    memcpy(compact,base,length);
    for(unsigned i=0;i<m->meshes;i++){
        DndMesh *mesh=&m->mesh[i];mesh->data=compact+(mesh->data-base);mesh->index=compact+(mesh->index-base);
    }
    dnd_model_free(m->package,&m->bytes);m->package=compact;
    // The immutable DNMD package has been replaced by a geometry-only view.
    // Never expose its original length to the SDK's native snapshot DMA path.
    m->residency_disabled=true;
    town_retained_buildings[town_building_slot(m)]=m->token;
#ifdef DND_GAMECUBE_STORAGE
    DCFlushRange(compact,length);
#endif
    return 1;
}
