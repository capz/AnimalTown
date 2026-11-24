"""Pinned native bridges for bounded inland-water lighting and bed refraction."""
from pathlib import Path
import re
import shutil
import sys


def replace_once(text, old, new, label):
    if text.count(old) != 1:
        raise SystemExit(f'Pinned water optics hook changed: {label}')
    return text.replace(old, new)


def patch(root):
    generated_path = root / 'source/generated_program.c'
    generated = generated_path.read_text()
    methods = {'Initialize': ['DndArray*'], 'Prepare': ['DndArray*', 'int', 'int', 'int'],
               'Begin': [], 'Scope': ['int'], 'Capture': [], 'Shutdown': [], 'Stats': ['int']}
    declarations = ['#include "dnd_managed.h"']
    for method, types in methods.items():
        args = ', '.join(r'intptr_t (\w+)' for _ in types) if types else 'void'
        pattern = (r'intptr_t (dnd_value_AnimalTown_GameCube_NativeWater_' + method +
                   r'_[a-z0-9]+)\(' + args + r'\) \{.*?\r?\n\}')
        if len(re.findall(pattern, generated, re.S)) != 1:
            raise SystemExit(f'Pinned water optics bridge changed: NativeWater.{method}')
        native = 'animaltown_water_' + method.lower()
        declarations.append(f'extern int {native}({",".join(types) if types else "void"});')

        def bridge(hit, types=types, native=native):
            names = [hit.group(i+2) for i in range(len(types))]
            parameters = ', '.join('intptr_t ' + n for n in names) if types else 'void'
            casts = ','.join('(' + t + ')' + n for t, n in zip(types, names))
            return f'intptr_t {hit.group(1)}({parameters}) {{ return {native}({casts}); }}'

        generated = re.sub(pattern, bridge, generated, flags=re.S)
    generated = '\n'.join(declarations) + '\n' + generated
    path = root / 'source/graphics_gx.c'
    graphics = path.read_text()
    marker = 'typedef struct { Mtx modelview,normal,inverse_view; uint32_t bindings; } DndVertexState;'
    graphics = replace_once(graphics, marker, marker + '\n#include "animaltown_water_optics.inc"', 'GX include boundary')
    direct = '    town_shadow_ground(v,NULL,count,texture,material,&state.bindings);'
    graphics = replace_once(graphics, direct, direct + '\n    town_water_optics_apply(material,world,&state.bindings);', 'direct post-footer')
    clear = 'uint32_t clear=rgba(r,g,b,a);GX_SetCopyClear(color(clear),0x00ffffff);'
    graphics = replace_once(graphics, clear, 'uint32_t clear=rgba(r,g,b,a);town_water_optics_clear(clear);GX_SetCopyClear(color(clear),0x00ffffff);', 'frame clear color')
    mesh_path = root / 'include/dnd_indexed_mesh.inc'
    mesh = mesh_path.read_text()
    indexed = '    town_shadow_ground(vertices,indices,count,texture,material,&state.bindings);'
    mesh = replace_once(mesh, indexed, indexed + '\n    town_water_optics_apply(material,world,&state.bindings);', 'indexed post-footer')
    # Every generated bridge remains linkable by non-console AOT fixtures.
    stubs = ['#ifndef DND_GAMECUBE_STORAGE']
    for method, types in methods.items():
        names = [f'a{i}' for i in range(len(types))]
        parameters = ','.join(t + ' ' + n for t, n in zip(types, names)) if types else 'void'
        unused = ''.join(f'(void){n};' for n in names)
        stubs.append(f'int animaltown_water_{method.lower()}({parameters}){{{unused}return 0;}}')
    stubs.append('float animaltown_water_setting(int index){(void)index;return 0;}\n#endif')
    graphics += '\n' + '\n'.join(stubs) + '\n'
    generated_path.write_text(generated)
    path.write_text(graphics)
    mesh_path.write_text(mesh)
    shutil.copyfile(Path(__file__).with_name('native-water-optics.inc'), root / 'source/animaltown_water_optics.inc')
    print('Water optics: accounted IA8 slopes/lookup, wet-only pre-world capture and bounded GX composition')


if __name__ == '__main__':
    patch(Path(sys.argv[1]))
