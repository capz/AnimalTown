/* Included in the pinned model.c copy: reuse its immutable texture registry and
 * accounting, with bounded copy/compare steps after verified package transport.
 * One game-owned restore runs at a time; startup model loads already completed. */
static void *asset_uninitialized(unsigned size,unsigned *used,unsigned budget){
    if(size>UINT_MAX-63)return NULL;
    unsigned total=(size+63u)&~31u;if(total>budget||*used>budget-total)return NULL;
#ifdef DND_GAMECUBE_STORAGE
    unsigned *p=memalign(32,total);
#else
    unsigned *p=aligned_alloc(32,total);
#endif
    if(!p)return NULL;
    p[0]=total;*used+=total;return (unsigned char*)p+32;
}
int animaltown_assets_stage_begin(DndModel *m){
    if(!m||m->ready||m->restoring||m->staged||dnd_device_active())return 0;
    m->staged=asset_uninitialized(m->length,&m->bytes,m->budget);if(!m->staged)return 0;
    m->restoring=true;return 1;
}
int animaltown_assets_texture_hash(DndModel *m,unsigned index,unsigned *hash){
    if(!m||!m->ready||index>=m->textures)return 0;
    DndTexture *t=m->texture+index;
    if(!t->data||t->cache<0||t->cache>=512||!texture_cache[t->cache].refs||texture_cache[t->cache].data!=t->data)return 0;
    *hash=texture_cache[t->cache].hash;return 1;
}
int animaltown_assets_texture_begin(DndModel *m,unsigned index,unsigned hash,unsigned first){
    if(!m||!m->restoring||m->ready||index>=m->textures||m->texture[index].data||dnd_device_active())return 0;
    DndTexture *t=m->texture+index;unsigned charge=(t->size+63u)&~31u;
    if(charge>m->budget||m->bytes>m->budget-charge)return 0;
    int free_slot=-1,slot=-1;
    for(int i=0;i<512;i++){
        if(!texture_cache[i].refs){if(free_slot<0)free_slot=i;}
        else if((unsigned)i>=first&&texture_cache[i].size==t->size&&texture_cache[i].hash==hash){slot=i;break;}
    }
    int kind=1;
    if(slot<0){
        if(free_slot<0)return 0;
        slot=free_slot;void *data=asset_uninitialized(t->size,&texture_cache_bytes,DND_MODEL_LIMIT);if(!data)return 0;
        texture_cache[slot].data=data;texture_cache[slot].size=t->size;texture_cache[slot].hash=hash;texture_cache[slot].refs=0;kind=2;
    }
    if(texture_cache[slot].refs==UINT_MAX)return 0;
    texture_cache[slot].refs++;t->data=texture_cache[slot].data;t->cache=slot;m->bytes+=charge;return kind;
}
int animaltown_assets_texture_step(DndModel *m,unsigned index,unsigned source,unsigned offset,unsigned count,int copy){
    if(!m||!m->restoring||m->ready||!m->staged||index>=m->textures||!count||count>32768||dnd_device_active())return -1;
    DndTexture *t=m->texture+index;
    if(!t->data||offset>t->size||count>t->size-offset||source>m->length||t->size>m->length-source)return -1;
    unsigned char *dest=(unsigned char*)t->data+offset;const unsigned char *src=m->staged+source+offset;
    if(!copy)return memcmp(dest,src,count)==0?1:0;
    memcpy(dest,src,count);
#ifdef DND_GAMECUBE_STORAGE
    DCFlushRange(dest,count);
#endif
    return 1;
}
unsigned animaltown_assets_texture_drop(DndModel *m,unsigned index){
    DndTexture *t=m->texture+index;unsigned next=(unsigned)t->cache+1;
    if(t->data){
        m->bytes-=(t->size+63u)&~31u;
        if(--texture_cache[t->cache].refs==0){dnd_model_free(texture_cache[t->cache].data,&texture_cache_bytes);texture_cache[t->cache].data=NULL;}
        t->data=NULL;
    }
    return next;
}
void animaltown_assets_abort(DndModel *m){
    if(!m)return;
    m->ready=false;drop_textures(m);
    for(int i=0;i<DND_INSTANCE_SLOTS;i++)if(dnd_instances[i]&&dnd_instances[i]->model==m)dnd_model_drop_vertices(dnd_instances[i]);
    if(m->restoring)(void)dnd_model_restore_end(m->token,0);
}
