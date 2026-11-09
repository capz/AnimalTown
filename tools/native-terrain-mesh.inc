/* Between-frame indexed updates, retaining capacity and token when possible.
 * Validate everything before mutating a published buffer. */
int animaltown_mesh_recycle(int token,DndArray *a,int n,DndArray *indices,int m){
    Resource *r=find(token,MESH);
    if(dnd_device_active()||!r||r->terrain_parent||r->geometry||r->holds||r->commands||r->format!=2||n<1||n>65535||m<3||m>3*65535||m%3||!array_range(a,0,n)||a->element_size!=36||!array_range(indices,0,m)||indices->element_size!=2)return -1;
    if((size_t)n*sizeof(DndDrawVertex)+(size_t)m*2>(size_t)r->bytes)return 0;
    const uint16_t *ix=(const uint16_t*)indices->data;
    for(int i=0;i<m;i++)if(ix[i]>=n)return -1;
    for(int start=0;start<n;start+=768){int count=n-start;if(count>768)count=768;if(!vertices(a,start,count,2,staging))return -1;}
    if(!vertices(a,0,n,2,(DndDrawVertex*)r->data))return -1;
    memcpy(r->data+(size_t)n*sizeof(DndDrawVertex),ix,(size_t)m*2);r->vertices=n;r->indices=m;
    mesh_color(r);dnd_draw_backend_flush(r->data,r->bytes,0);dnd_draw_backend_mesh_changed();TOWN_COUNT(TOWN_MESH_UPDATES);return 1;
}
