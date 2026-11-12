/* Opt-in for permanently resident models. The SDK's shared GX texture cache
 * already owns the pixels; the validated source package embeds a second copy.
 * Retain geometry and, for animated residents, skin and animation bytes. Parsed
 * nodes/materials/authored state and GX textures have independent ownership.
 * Source-package export/unload must reject this compact representation. */
static int town_retained_buildings[DND_MODEL_SLOTS];
static int town_building_slot(const DndModel *m){for(int i=0;i<DND_MODEL_SLOTS;i++)if(dnd_models[i]==m)return i;return -1;}
static bool town_building_retained(const DndModel *m){int i=town_building_slot(m);return i>=0&&town_retained_buildings[i]==m->token;}
typedef struct {const unsigned char *base;unsigned length,offset;} TownResidentSlice;
static bool town_resident_span(const TownResidentSlice *slice,const void *pointer,size_t bytes){
    uintptr_t base=(uintptr_t)slice->base,at=(uintptr_t)pointer;
    return slice->base&&at>=base&&at-base<=slice->length&&bytes<=slice->length-(at-base);
}
static int town_model_retain(int token,bool animated){
    DndModel *m=dnd_model_find(token,false);
    if(!m||!m->ready||m->restoring||m->file||dnd_device_active())return -1;
    if(!animated&&(m->skins||m->clips))return -1;
    if(town_building_retained(m))return 1;
    if(m->refs!=1||!m->meshes||!m->package||m->length<160)return -1;
    TownResidentSlice slices[3]={{0}};
    const unsigned sections[3]={2,5,6};unsigned length=0;
    for(unsigned i=0;i<3;i++){
        if((i==1&&!m->skins)||(i==2&&!m->clips))continue;
        const unsigned char *dir=m->package+16+(sections[i]-1)*16;
        unsigned offset=dnd_model_u32(dir+4),size=dnd_model_u32(dir+8);
        if(dnd_model_u32(dir)!=sections[i]||offset<160||(offset&31)||
           offset>m->length||size<4||size>m->length-offset||length>UINT_MAX-31)return -1;
        unsigned start=(length+31u)&~31u;
        if(size>UINT_MAX-start)return -1;
        slices[i]=(TownResidentSlice){m->package+offset,size,start};length=start+size;
    }
    for(unsigned i=0;i<m->meshes;i++){
        DndMesh *mesh=&m->mesh[i];
        if((!animated&&mesh->skin!=-1)||mesh->vertices>slices[0].length/80||
           mesh->indices>slices[0].length/4||
           !town_resident_span(&slices[0],mesh->data,(size_t)mesh->vertices*80)||
           !town_resident_span(&slices[0],mesh->index,(size_t)mesh->indices*4))return -1;
    }
    for(unsigned i=0;i<m->skins;i++)if(m->skin[i].count<1||
        (unsigned)m->skin[i].count>slices[1].length/68||
        !town_resident_span(&slices[1],m->skin[i].data,(size_t)m->skin[i].count*68))return -1;
    for(unsigned i=0;i<m->clips;i++)if(m->clip[i].size<4||
        !town_resident_span(&slices[2],m->clip[i].tracks,m->clip[i].size))return -1;
    unsigned char *compact=dnd_model_alloc(length,&m->bytes,m->budget);if(!compact)return -2;
    for(unsigned i=0;i<3;i++)if(slices[i].base)memcpy(compact+slices[i].offset,slices[i].base,slices[i].length);
    /* No model fields change before all ranges validate and allocation/copy
     * finish. In particular, failure leaves package and cache ownership intact. */
    for(unsigned i=0;i<m->meshes;i++){
        DndMesh *mesh=&m->mesh[i];
        mesh->data=compact+slices[0].offset+(mesh->data-slices[0].base);
        mesh->index=compact+slices[0].offset+(mesh->index-slices[0].base);
    }
    for(unsigned i=0;i<m->skins;i++)m->skin[i].data=compact+slices[1].offset+(m->skin[i].data-slices[1].base);
    for(unsigned i=0;i<m->clips;i++)m->clip[i].tracks=compact+slices[2].offset+(m->clip[i].tracks-slices[2].base);
    dnd_model_free(m->package,&m->bytes);m->package=compact;
    town_retained_buildings[town_building_slot(m)]=m->token;
#ifdef DND_GAMECUBE_STORAGE
    DCFlushRange(compact,length);
#endif
    return 1;
}
int animaltown_building_retain(int token){return town_model_retain(token,false);}
int animaltown_model_retain(int token){return town_model_retain(token,true);}
