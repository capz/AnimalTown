"""Game-owned scoped UI grain; fail closed when pinned SDK/AOT hooks change."""
from pathlib import Path
import re,shutil,sys
root=Path(sys.argv[1]);here=Path(__file__).resolve().parent
def replace(path,old,new):
 s=path.read_text();assert s.count(old)==1,'Pinned UI noise hook changed: '+old[:90]
 path.write_text(s.replace(old,new))
p=root/'source/generated_program.c';s=p.read_text()
for method,decl,call in [('NativePrepare','DndArray*','(DndArray*)'),('NativeScope','int','(int)')]:
 pattern=r'intptr_t (dnd_value_AnimalTown_GameCube_UiNoise_'+method+r'_[a-z0-9]+)\(intptr_t (\w+)\) \{.*?\r?\n\}'
 assert len(re.findall(pattern,s,re.S))==1,'UI noise AOT bridge changed: '+method
 native='animaltown_ui_noise_'+('prepare' if method=='NativePrepare' else 'scope')
 s=re.sub(pattern,lambda m:'intptr_t '+m[1]+'(intptr_t '+m[2]+') { return '+native+'('+call+m[2]+'); }',s,flags=re.S)
 s='extern int '+native+'('+decl+');\n'+s
s='#include "dnd_managed.h"\n'+s;p.write_text(s)
p=root/'source/draw_resources.c';s=p.read_text()
# State is declared before canvas flush; implementations follow SDK functions.
assert s.count('static int total_bytes,active_canvas;')==1,'UI canvas state hook changed'
s=s.replace('static int total_bytes,active_canvas;','static int total_bytes,active_canvas;\nstatic int town_ui_noise_texture,town_ui_noise_scope;')
assert 'static int town_ui_noise_texture' in s,'UI canvas state hook changed'
a=s.index('int dnd_canvas_flush(int token) {');b=s.index('int dnd_canvas_vertices(',a)
part=s[a:b].replace('        DndDrawTexture tx;','        int selected=r->texture==0&&town_ui_noise_scope?town_ui_noise_texture:r->texture;\n        DndDrawTexture tx;').replace('r->texture','selected')
# Keep the selected initializer based on the queued (unmodified) texture.
part=part.replace('int selected=selected==0&&town_ui_noise_scope?town_ui_noise_texture:selected;','int selected=r->texture==0&&town_ui_noise_scope?town_ui_noise_texture:r->texture;')
s=s[:a]+part+s[b:]
needle='int dnd_canvas_geometry(int token,int mesh,int tex,DndArray *world,uint32_t color){'
assert s.count(needle)==1
s=s.replace(needle,needle+'\n    if(tex==0&&town_ui_noise_scope)tex=town_ui_noise_texture;')
# Single tentative definition above is shared by the included implementation.
s+='\n#include "animaltown_ui_noise.inc"\n';p.write_text(s)
shutil.copyfile(here/'native-ui-noise.inc',root/'source/animaltown_ui_noise.inc')
shutil.copyfile(here/'native-ui-noise-gx.inc',root/'include/animaltown_ui_noise_gx.inc')
p=root/'source/graphics_gx.c'
needle='void dnd_draw_backend_vertices(const DndDrawVertex *v,int count,int primitive,const DndDrawTexture *texture,const DndDrawMaterial *material,const float *world) {'
replace(p,needle,'#include "animaltown_ui_noise_gx.inc"\n'+needle)
needle='    if(!draw_state(texture,material,world,&state))return;'
replace(p,needle,needle+'\n    town_ui_noise_apply(texture,world);')
p=root/'include/dnd_indexed_mesh.inc';needle='    if(!draw_state(texture,material,world,&state))return true;'
replace(p,needle,needle+'\n    town_ui_noise_apply(texture,world);')
print('UI grain: scoped SDK texture, painter-order flush, position UVs and alpha preservation patched')
