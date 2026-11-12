"""Patch generated native copies only; reject a changed pinned runtime."""
from pathlib import Path
import re
import shutil
import sys

root=Path(sys.argv[1])
header=root/'include/dnd_managed.h'
text=header.read_text()
assert text.count('    void *free_list;')==1, 'Pinned heap layout changed'
header.write_text(text.replace('    void *free_list;', '    void *free_list;\n    void *allocation_tail;'))
source=root/'source/managed_runtime.c'
text=source.read_text()
start=text.index('static void rebuild_free_list(DndManagedHeap *heap) {')
end=text.index('DndObject *dnd_object_new(',start)
assert text[start:end].count('rebuild_free_list(heap);')==1
source.write_text(text[:start]+'#include "native-managed-allocator.inc"\n\n'+text[end:])

here=Path(__file__).resolve().parent
for name in ('native-managed-allocator.inc','native-frame-counters.h','native-terrain-mesh.inc','native-terrain-archive.inc'):
    shutil.copyfile(here/name,root/'include'/name)
shutil.copyfile(here/'native-frame-counters.c',root/'source/animaltown_frame_counters.c')
# Retain exact resident geometry/skin/animation while dropping source textures.
# All streaming/copy operations must reject the opt-in compact representation.
model_source=root/'source/model.c'
model=model_source.read_text()
anchor='/* Texture payloads are immutable and shared across independently loaded models.'
assert model.count(anchor)==1, 'Pinned model retention anchor changed'
model=model.replace(anchor,'#include "native-building-retained.inc"\n'+anchor)
for name in ('dnd_model_copy','dnd_model_unload'):
    start=model.index('int '+name+'(')
    end=model.index('\n}',start)+2
    body=model[start:end]
    lookup='DndModel *m = dnd_model_find(token, false);'
    assert body.count(lookup)==1, 'Pinned resident building lookup changed: '+name
    body=body.replace(lookup,lookup+'\n  if (!m || town_building_retained(m)) return -1;')
    model=model[:start]+body+model[end:]
model_source.write_text(model)
shutil.copyfile(here/'native-building-retained.inc',root/'include/native-building-retained.inc')
resource=root/'source/draw_resources.c'
text=resource.read_text()
def replace(old,new):
    global text
    assert text.count(old)==1, 'Pinned resource hook changed: '+old[:60]
    text=text.replace(old,new)
text='#include "native-frame-counters.h"\n'+text
replace('int holds,geometry,extra_bytes;','int holds,geometry,extra_bytes,terrain_parent,terrain_entry;')
replace('return (generation<<10)|(i+1);','if(kind==MESH){TOWN_COUNT(TOWN_MESH_CREATES);}\n        if(kind==TEXTURE){TOWN_COUNT(TOWN_TEXTURE_CREATES);}\n        return (generation<<10)|(i+1);')
replace('static void release(Resource *r) {','static void terrain_detach(Resource *r);\nstatic void release(Resource *r) {\n    if(r->terrain_entry)terrain_detach(r);')
replace('int parent=r->geometry;','if(r->kind==MESH)TOWN_COUNT(TOWN_MESH_RELEASES);\n    int parent=r->geometry,terrain_parent=r->terrain_parent;')
replace('if(base&&--base->holds==0&&base->pending)release(base);','if(base&&--base->holds==0&&base->pending)release(base);\n    if(terrain_parent)(void)dnd_buffer_unhold(terrain_parent);')
replace('dnd_draw_backend_flush(r->data,count,2);','dnd_draw_backend_flush(r->data,count,2);TOWN_COUNT(TOWN_TEXTURE_UPDATES);')
replace('dnd_draw_backend_flush(texture->data,count,2);','dnd_draw_backend_flush(texture->data,count,2);TOWN_COUNT(TOWN_TEXTURE_UPDATES);')
assert text.count('dnd_draw_backend_flush(r->data,count,0);return 0;')==2
text=text.replace('dnd_draw_backend_flush(r->data,count,0);return 0;','dnd_draw_backend_flush(r->data,count,0);TOWN_COUNT(TOWN_TEXTURE_UPDATES);return 0;')
replace('dnd_draw_backend_flush(r->data,r->bytes,2);return 0;', 'dnd_draw_backend_flush(r->data,r->bytes,2);TOWN_COUNT(TOWN_TEXTURE_UPDATES);return 0;')
replace('mesh_color(r);dnd_draw_backend_flush(r->data,r->bytes,0);dnd_draw_backend_mesh_changed();return 0;', 'mesh_color(r);dnd_draw_backend_flush(r->data,r->bytes,0);dnd_draw_backend_mesh_changed();TOWN_COUNT(TOWN_MESH_UPDATES);return 0;')
replace('#include "dnd_mesh_streams.inc"','#include "native-terrain-mesh.inc"\n#include "native-terrain-archive.inc"\n#include "dnd_mesh_streams.inc"')
resource.write_text(text)

generated=root/'source/generated_program.c'
if generated.exists():
    text=generated.read_text()
    bodies={
        'Reset':(1,'animaltown_frame_reset((int){0},dnd_value_heap);return 0;'),
        'Snapshot':(1,'animaltown_frame_snapshot((DndArray*){0},dnd_value_heap);return 0;'),
        'Service':(0,'animaltown_frame_service();return 0;'),
        'Create':(4,'return dnd_mesh_create_range((DndArray*){0},(int){1},(DndArray*){2},(int){3},2,0);'),
        'Recycle':(5,'return animaltown_mesh_recycle((int){0},(DndArray*){1},(int){2},(DndArray*){3},(int){4});')}
    for method,(count,body) in bodies.items():
        pattern=r'intptr_t (dnd_value_AnimalTown_GameCube_NativeFrame_'+method+r'_[a-z0-9]+)\(([^)]*)\) \{.*?\n\}'
        matches=list(re.finditer(pattern,text,re.S));assert len(matches)==1, 'NativeFrame bridge changed: '+method
        m=matches[0];args=re.findall(r'intptr_t (\w+)',m[2]);assert len(args)==count
        text=text[:m.start()]+'intptr_t '+m[1]+'('+m[2]+') { '+body.format(*args)+' }'+text[m.end():]
    text='''#include "dnd_draw.h"
extern void animaltown_frame_reset(int,DndManagedHeap*);
extern void animaltown_frame_snapshot(DndArray*,DndManagedHeap*);
extern void animaltown_frame_service(void);
extern int animaltown_mesh_recycle(int,DndArray*,int,DndArray*,int);
'''+text
    generated.write_text(text)
    model_getter=re.search(r'intptr_t dnd_value_DolphinDotNet_GameCube_Dolphin_Graphics_Model_get_NativeBytes_[a-z0-9]+\(intptr_t a0\) \{.*?\n\}',text,re.S)
    assert model_getter and '+sizeof(DndObject)+0+0)' in model_getter[0], 'Pinned Model.Token layout changed'
    bridge=r'intptr_t (dnd_value_AnimalTown_GameCube_NativeBuildings_Retain_[a-z0-9]+)\(intptr_t a0\) \{.*?\n\}'
    assert len(re.findall(bridge,text,re.S))==1, 'Pinned building retention bridge changed'
    text=re.sub(bridge,r'intptr_t \1(intptr_t a0) { int32_t token=0;if(!a0)return -1;memcpy(&token,(uint8_t*)a0+sizeof(DndObject),4);return animaltown_building_retain(token); }',text,flags=re.S)
    text='extern int animaltown_building_retain(int);\n'+text
    bridge=r'intptr_t (dnd_value_AnimalTown_GameCube_NativeModels_Retain_[a-z0-9]+)\(intptr_t a0\) \{.*?\n\}'
    assert len(re.findall(bridge,text,re.S))==1, 'Pinned resident model retention bridge changed'
    text=re.sub(bridge,r'intptr_t \1(intptr_t a0) { int32_t token=0;if(!a0)return -1;memcpy(&token,(uint8_t*)a0+sizeof(DndObject),4);return animaltown_model_retain(token); }',text,flags=re.S)
    text='extern int animaltown_model_retain(int);\n'+text
    generated.write_text(text)
    # Mesh.Token is an internal SDK field. Pin its actual generated getter
    # layout before exposing only the token to the game's archive bridge.
    getter=re.search(r'intptr_t dnd_value_DolphinDotNet_GameCube_Dolphin_Graphics_Mesh_get_NativeBytes_[a-z0-9]+\(intptr_t a0\) \{.*?\n\}',text,re.S)
    assert getter and '+sizeof(DndObject)+0+0)' in getter[0], 'Pinned Mesh.Token layout changed'
    archive={
        'Reset':(1,'return animaltown_terrain_reset((int){0});'),
        'Append':(4,'int32_t token=0;if(!{2})return -1;memcpy(&token,(uint8_t*){2}+sizeof(DndObject),4);return animaltown_terrain_append((int){0},(int){1},token,(int){3});'),
        'Complete':(1,'return animaltown_terrain_complete((int){0});'),
        'Info':(2,'return animaltown_terrain_info((int){0},(int){1});'),
        'Kind':(1,'return animaltown_terrain_kind((int){0});'),
        'View':(0,'return animaltown_terrain_view();'),
        'Bind':(2,'return animaltown_terrain_bind((int){0},(int){1});'),
        'BindStep':(2,'return animaltown_terrain_bind_step((int){0},(int){1});'),
        'CancelBind':(0,'return animaltown_terrain_cancel_bind();'),
        'Suspend':(0,'return animaltown_terrain_suspend();'),
        'Resume':(0,'return animaltown_terrain_resume();'),
        'Unbind':(1,'return animaltown_terrain_unbind((int){0});'),
        'Cache':(1,'return animaltown_terrain_cache((int){0});'),
        'Stat':(1,'return animaltown_terrain_stat((int){0});')}
    for method,(count,body) in archive.items():
        pattern=r'intptr_t (dnd_value_AnimalTown_GameCube_NativeTerrain_'+method+r'_[a-z0-9]+)\(([^)]*)\) \{.*?\n\}'
        matches=list(re.finditer(pattern,text,re.S));assert len(matches)==1, 'NativeTerrain bridge changed: '+method
        m=matches[0];args=re.findall(r'intptr_t (\w+)',m[2]);assert len(args)==count
        text=text[:m.start()]+'intptr_t '+m[1]+'('+m[2]+') { '+body.format(*args)+' }'+text[m.end():]
    text='''extern int animaltown_terrain_reset(int);
extern int animaltown_terrain_append(int,int,int,int);
extern int animaltown_terrain_complete(int);
extern int animaltown_terrain_info(int,int);
extern int animaltown_terrain_kind(int);
extern int animaltown_terrain_view(void);
extern int animaltown_terrain_bind(int,int);
extern int animaltown_terrain_bind_step(int,int);
extern int animaltown_terrain_cancel_bind(void);
extern int animaltown_terrain_suspend(void);
extern int animaltown_terrain_resume(void);
extern int animaltown_terrain_unbind(int);
extern int animaltown_terrain_stat(int);
extern int animaltown_terrain_cache(int);
'''+text
    generated.write_text(text)
    # Persistence runs during blocking save/load screens. Keep these AOT
    # functions compact and out of gameplay's O3 inlining budget: their text
    # shares the same 24 MiB MEM1 as card buffers and the full GX cache.
    persistence=r'(?m)^(intptr_t dnd_value_AnimalTown_Domain_AnimalTown_(?:SaveCodec|SplitSaveCodec|ProfileCodec|SaveRepository|IncrementalSaveCodec)_[^;\n]*\) \{)'
    text,compact_count=re.subn(persistence,r'__attribute__((noinline,optimize("Os"))) \1',text)
    assert compact_count>=30, 'Persistence AOT function boundaries changed'
    for codec in ('SaveCodec','SplitSaveCodec','ProfileCodec','SaveRepository','IncrementalSaveCodec'):
        assert re.search(r'__attribute__.*dnd_value_AnimalTown_Domain_AnimalTown_'+codec+r'_',text), 'Missing compact persistence codec: '+codec
    generated.write_text(text)
    print('Persistence: '+str(compact_count)+' cold AOT functions compiled for size; gameplay optimization retained')
