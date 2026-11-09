/* Immutable native terrain pages. Mesh views retarget metadata between frames;
 * movement never writes vertex/index payloads or flushes geometry to GX. */
#define TA_SECTIONS 4096
#define TA_PARTS 16384
#define TA_PAGE 65536
#define TA_BYTES (8*1024*1024)
typedef struct {int page,offset,vertices,indices,kind,format;uint32_t color;int uniform;} TaPart;
static int ta_pages[128],ta_meta[TA_PARTS/256],ta_first[TA_SECTIONS+1],ta_triangles[TA_SECTIONS];
static int ta_page_count,ta_meta_count,ta_bytes,ta_tail,ta_parts,ta_sections,ta_completed;
static unsigned ta_bindings;static bool ta_sealed;
static TaPart *ta_part(int index){
    if(index<0||index>=ta_parts)return NULL;
    Resource *r=find(ta_meta[index/256],BUFFER);return r?(TaPart*)r->data+index%256:NULL;
}
static int ta_new_buffer(int bytes){
    if(bytes>TA_BYTES-ta_bytes)return 0;
    int token=create(BUFFER,bytes,32);if(token)ta_bytes+=bytes;return token;
}
int animaltown_terrain_reset(int count){
    if(dnd_device_active()||dnd_raw_depth()||count<0||count>TA_SECTIONS)return -1;
    for(int i=0;i<SLOTS;i++)if(slots[i].terrain_parent)return -1;
    for(int i=0;i<ta_page_count;i++)if(dnd_draw_release(ta_pages[i]))return -1;
    for(int i=0;i<ta_meta_count;i++)if(dnd_draw_release(ta_meta[i]))return -1;
    memset(ta_first,0,sizeof(ta_first));memset(ta_triangles,0,sizeof(ta_triangles));
    ta_page_count=ta_meta_count=ta_bytes=ta_tail=ta_parts=ta_completed=0;ta_bindings=0;ta_sections=count;ta_sealed=false;
    return 1;
}
int animaltown_terrain_append(int section,int kind,int token,int used){
    Resource *source=find(token,MESH);
    if(dnd_device_active()||dnd_raw_depth()||ta_sealed||section<0||section!=ta_completed||section>=ta_sections||kind<0||kind>=107||!source||source->geometry||source->terrain_parent||source->commands||source->format<1||source->format>2||used<0||ta_parts>=TA_PARTS)return -1;
    int n=source->indices?source->vertices:used?used:source->vertices,m=source->indices;
    if(n<1||n>source->vertices||(!m&&n%3))return -1;
    size_t bytes=(size_t)n*sizeof(DndDrawVertex)+(size_t)m*2;
    if(bytes>TA_BYTES-31)return -1;
    int padded=(int)((bytes+31u)&~31u);
    if(ta_parts/256>=ta_meta_count){int meta=ta_new_buffer(256*(int)sizeof(TaPart));if(!meta)return -2;ta_meta[ta_meta_count++]=meta;}
    Resource *page=ta_page_count?find(ta_pages[ta_page_count-1],BUFFER):NULL;
    if(!page||padded>page->bytes-ta_tail){
        if(ta_page_count==128)return -2;
        int next=ta_new_buffer(padded>TA_PAGE?padded:TA_PAGE);if(!next)return -2;
        ta_pages[ta_page_count++]=next;page=find(next,BUFFER);ta_tail=0;
    }
    unsigned char *destination=page->data+ta_tail;
    memcpy(destination,source->data,(size_t)n*sizeof(DndDrawVertex));
    if(m)memcpy(destination+(size_t)n*sizeof(DndDrawVertex),source->data+(size_t)source->vertices*sizeof(DndDrawVertex),(size_t)m*2);
    const DndDrawVertex *v=(const DndDrawVertex*)destination;uint32_t color=v[0].color;int uniform=1;
    for(int i=1;i<n;i++)if(v[i].color!=color){uniform=0;break;}
    TaPart part={ta_pages[ta_page_count-1],ta_tail,n,m,kind,source->format,color,uniform};
    TaPart *entry=(TaPart*)find(ta_meta[ta_parts/256],BUFFER)->data+ta_parts%256;*entry=part;
    ta_parts++;ta_tail+=padded;ta_triangles[section]+=(m?m:n)/3;
    dnd_draw_backend_flush(destination,(int)bytes,0);return ta_parts;
}
int animaltown_terrain_complete(int section){
    if(dnd_device_active()||dnd_raw_depth()||ta_sealed||section<0||section!=ta_completed||section>=ta_sections)return -1;
    ta_first[++ta_completed]=ta_parts;if(ta_completed==ta_sections)ta_sealed=true;return 1;
}
int animaltown_terrain_info(int section,int kind){
    if(!ta_sealed||section<0||section>=ta_sections)return -1;
    return kind==0?ta_first[section]:kind==1?ta_first[section+1]-ta_first[section]:kind==2?ta_triangles[section]:-1;
}
int animaltown_terrain_kind(int entry){TaPart *p=ta_part(entry);return ta_sealed&&p?p->kind:-1;}
int animaltown_terrain_view(void){
    if(dnd_device_active())return 0;
    int token=create(MESH,1,32);Resource *r=find(token,MESH);if(!r)return 0;
    free(r->allocation);r->allocation=NULL;r->data=NULL;return token;
}
int animaltown_terrain_unbind(int token){
    Resource *r=find(token,MESH);if(dnd_device_active()||dnd_raw_depth()||!r||r->allocation||r->bytes!=1||r->holds||r->geometry||r->commands)return -1;
    int previous=r->terrain_parent;r->terrain_parent=0;r->data=NULL;r->vertices=r->indices=0;
    if(previous&&dnd_buffer_unhold(previous))return -1;
    dnd_draw_backend_mesh_changed();return 1;
}
int animaltown_terrain_bind(int token,int index){
    Resource *r=find(token,MESH);TaPart *p=ta_part(index);
    if(dnd_device_active()||dnd_raw_depth()||!ta_sealed||!r||r->holds||r->geometry||r->commands||r->allocation||r->bytes!=1||!p)return -1;
    Resource *page=find(p->page,BUFFER);
    size_t bytes=(size_t)p->vertices*sizeof(DndDrawVertex)+(size_t)p->indices*2;
    if(!page||p->offset<0||(size_t)p->offset>(size_t)page->bytes||bytes>(size_t)page->bytes-p->offset)return -1;
    int previous=r->terrain_parent;
    if(previous!=p->page&&dnd_buffer_hold(p->page))return -1;
    r->terrain_parent=p->page;r->data=page->data+p->offset;r->vertices=p->vertices;r->indices=p->indices;r->format=p->format;
    r->uniform_color=p->uniform!=0;r->mesh_color=p->color;r->dynamic=0;
    if(previous&&previous!=p->page)(void)dnd_buffer_unhold(previous);
    ta_bindings++;dnd_draw_backend_mesh_changed();return 1;
}
int animaltown_terrain_stat(int kind){return kind==0?ta_bytes:kind==1?ta_parts:kind==2?ta_page_count:kind==3?ta_completed:kind==4?(int)ta_bindings:-1;}
