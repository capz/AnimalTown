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
for name in ('native-managed-allocator.inc','native-frame-counters.h','native-terrain-mesh.inc'):
    shutil.copyfile(here/name,root/'include'/name)
shutil.copyfile(here/'native-frame-counters.c',root/'source/animaltown_frame_counters.c')
resource=root/'source/draw_resources.c'
text=resource.read_text()
def replace(old,new):
    global text
    assert text.count(old)==1, 'Pinned resource hook changed: '+old[:60]
    text=text.replace(old,new)
text='#include "native-frame-counters.h"\n'+text
replace('return (generation<<10)|(i+1);','if(kind==MESH){TOWN_COUNT(TOWN_MESH_CREATES);}\n        if(kind==TEXTURE){TOWN_COUNT(TOWN_TEXTURE_CREATES);}\n        return (generation<<10)|(i+1);')
replace('int parent=r->geometry;','if(r->kind==MESH)TOWN_COUNT(TOWN_MESH_RELEASES);\n    int parent=r->geometry;')
replace('dnd_draw_backend_flush(r->data,count,2);','dnd_draw_backend_flush(r->data,count,2);TOWN_COUNT(TOWN_TEXTURE_UPDATES);')
replace('dnd_draw_backend_flush(texture->data,count,2);','dnd_draw_backend_flush(texture->data,count,2);TOWN_COUNT(TOWN_TEXTURE_UPDATES);')
assert text.count('dnd_draw_backend_flush(r->data,count,0);return 0;')==2
text=text.replace('dnd_draw_backend_flush(r->data,count,0);return 0;','dnd_draw_backend_flush(r->data,count,0);TOWN_COUNT(TOWN_TEXTURE_UPDATES);return 0;')
replace('dnd_draw_backend_flush(r->data,r->bytes,2);return 0;', 'dnd_draw_backend_flush(r->data,r->bytes,2);TOWN_COUNT(TOWN_TEXTURE_UPDATES);return 0;')
replace('mesh_color(r);dnd_draw_backend_flush(r->data,r->bytes,0);dnd_draw_backend_mesh_changed();return 0;', 'mesh_color(r);dnd_draw_backend_flush(r->data,r->bytes,0);dnd_draw_backend_mesh_changed();TOWN_COUNT(TOWN_MESH_UPDATES);return 0;')
replace('#include "dnd_mesh_streams.inc"','#include "native-terrain-mesh.inc"\n#include "dnd_mesh_streams.inc"')
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
