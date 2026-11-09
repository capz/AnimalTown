"""Enforce the game policy in pinned generated SDK copies, including authored TEV."""
from pathlib import Path
import shutil,sys
root=Path(sys.argv[1]);here=Path(__file__).resolve().parent
shutil.copyfile(here/'native-face-culling.h',root/'include/animaltown_face_culling.h')
def replace(path,old,new):
    text=path.read_text();assert text.count(old)==1,'Pinned face culling hook changed: '+old[:80]
    path.write_text(text.replace(old,new))
gx=root/'source/graphics_gx.c';model=root/'source/model_graphics.c'
for path in (gx,model):path.write_text('#include "animaltown_face_culling.h"\n'+path.read_text())
replace(gx,'#include <gccore.h>','#include <gccore.h>\n_Static_assert(GX_CULL_NONE==0&&GX_CULL_FRONT==1&&GX_CULL_BACK==2,"Pinned GX cull enum changed");')
replace(gx,'GX_SetCullMode((material->flags&4)?(determinant<0?GX_CULL_FRONT:GX_CULL_BACK):GX_CULL_NONE);','GX_SetCullMode(town_face_cull(dnd_device_pass()==1||dnd_device_pass()==4,material->flags&4,determinant));')
replace(model,'''GX_SetCullMode(mat->two          ? GX_CULL_NONE
                   : determinant < 0 ? GX_CULL_FRONT
                                     : GX_CULL_BACK);''','GX_SetCullMode(town_face_cull(1,0,determinant));')
# An authored GX material can override the earlier imported raster state.
replace(model,'authored_model_material(authored,mat,normal,custom_scene,in,(unsigned)mesh->material);','authored_model_material(authored,mat,normal,custom_scene,in,(unsigned)mesh->material);\n      GX_SetCullMode(town_face_cull(1,0,determinant));')
# Developer UV overlays use the same winding policy as ordinary model draws.
uv=root/'source/animaltown_uv_debug.inc'
replace(uv,'GX_SetCullMode(mat->two?GX_CULL_NONE:determinant<0?GX_CULL_FRONT:GX_CULL_BACK);','GX_SetCullMode(town_face_cull(1,0,determinant));')
