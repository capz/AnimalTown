"""Install game-owned, bounded GX command reuse in copied native sources."""
from pathlib import Path
import sys,shutil
root=Path(sys.argv[1]);game=Path(__file__).resolve().parent
shutil.copyfile(game/'native-geometry-lists.h',root/'include/native-geometry-lists.h');shutil.copyfile(game/'native-geometry-lists.c',root/'source/animaltown_geometry_lists.c')
def patch(path,changes,extra=''):
 s=path.read_text();s='#include "native-geometry-lists.h"\n'+s
 for old,new in changes:
  if s.count(old)!=1:raise RuntimeError('Pinned geometry list hook changed: '+old)
  s=s.replace(old,new)
 path.write_text(s+extra)
patch(root/'source/draw_resources.c',[
 ('    Resource *base=mesh_geometry(r);if(!base)return -1;\n    DndDrawVertex *v=', '    Resource *base=mesh_geometry(r);if(!base)return -1;\n    town_geometry_mesh_owner(base->dynamic?0:(((int)(base-slots)+1)|(base->generation<<10)));\n    DndDrawVertex *v='),
 ('static void release(Resource *r) {','static void release(Resource *r) {\n    if(r->kind==MESH)town_geometry_forget(0,((int)(r-slots)+1)|(r->generation<<10));')],'''
int town_geometry_mesh_source(int token,const void **indices,unsigned *count,unsigned *vertices,int *width){
    Resource *r=find(token,MESH);if(!r)r=held_geometry(token);
    if(!r||r->dynamic||r->geometry)return 0;
    *count=(unsigned)(r->indices?r->indices:r->vertices);*vertices=(unsigned)r->vertices;*width=16;
    *indices=r->indices?r->data+r->vertices*sizeof(DndDrawVertex):NULL;return 1;
}
''')
patch(root/'include/dnd_indexed_mesh.inc',[
 ('    for(int start=0;start<count;) {','''    TownGeometryList town_list;
    unsigned town_mask=(vertex_colors?2u:0u)|((texture||material->secondary)?4u:0u)|(material->secondary?8u:0u);
    if(town_geometry_mesh_list(town_mask,(unsigned)count,&town_list)){
        GX_CallDispList((void*)town_list.data,town_list.bytes);
        for(unsigned p=0;p<town_list.primitives;p++)dnd_performance_add_draw(p?0:state.bindings);
        indexed_layout=true;return true;
    }
    for(int start=0;start<count;) {''')])
patch(root/'include/dnd_indexed_model.inc',[
 ('  for(unsigned first=0;first<mesh->indices;){','''  TownGeometryList town_list;
  unsigned town_mask=((lit||town_rim_enabled)?1u:0u)|(textured?4u:0u);
  if(town_geometry_model_list(town_mask,mesh->indices,&town_list)){
    GX_CallDispList((void*)town_list.data,town_list.bytes);
    for(unsigned p=0;p<town_list.primitives;p++)dnd_performance_add_draw(p?0:bindings);
    return true;
  }
  for(unsigned first=0;first<mesh->indices;){''')])
patch(root/'source/model_graphics.c',[
 ('    if(!authored&&indexed_model(mesh,mat,normal,custom_scene,anim!=NULL,mesh->skin>=0?in->vertices[mi]:NULL,bindings))continue;', '    town_geometry_model_owner(model->token,mi,model->hash,mesh->skin<0&&model->clips==0);\n    if(!authored&&indexed_model(mesh,mat,normal,custom_scene,anim!=NULL,mesh->skin>=0?in->vertices[mi]:NULL,bindings))continue;')])
patch(root/'source/model.c',[
 ('  for (int i = 0; i < DND_MODEL_SLOTS; i++)\n    if (dnd_models[i] == m)', '  town_geometry_forget(1,m->token);\n  for (int i = 0; i < DND_MODEL_SLOTS; i++)\n    if (dnd_models[i] == m)')])
