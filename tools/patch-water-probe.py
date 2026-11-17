"""Observe actual water GX commands without changing the pinned SDK package."""
from pathlib import Path
import re
import shutil
import sys


def replace_once(text, original, replacement, label):
    if text.count(original) != 1:
        raise SystemExit(f'Pinned water probe hook changed: {label}')
    return text.replace(original, replacement)


def patch(root):
    generated_path = root / 'source/generated_program.c'
    generated = generated_path.read_text()
    pattern = (r'intptr_t (dnd_value_AnimalTown_GameCube_NativeWaterProbe_Enable_[a-z0-9]+)'
               r'\(intptr_t (\w+)\) \{.*?\r?\n\}')
    if len(re.findall(pattern, generated, re.S)) != 1:
        raise SystemExit('Pinned water probe bridge changed: NativeWaterProbe.Enable')
    generated = re.sub(pattern,
                       r'intptr_t \1(intptr_t \2) { return animaltown_water_probe_enable((int)\2); }',
                       generated, flags=re.S)
    generated = 'extern int animaltown_water_probe_enable(int);\n' + generated

    graphics_path = root / 'source/graphics_gx.c'
    graphics = graphics_path.read_text()
    graphics = replace_once(graphics, '#include <ogc/timesupp.h>',
                            '#include <ogc/timesupp.h>\n#include "animaltown_water_probe.inc"',
                            'GX include boundary')
    signature = ('static bool draw_state(const DndDrawTexture *texture,const DndDrawMaterial *material,'
                 'const float *world,DndVertexState *output) {')
    graphics = replace_once(graphics, signature,
                            signature + '\n    town_water_select(texture,material);', 'draw_state')
    graphics += ('\n#ifndef DND_GAMECUBE_STORAGE\n'
                 'int animaltown_water_probe_enable(int scene){(void)scene;return 0;}\n'
                 '#endif\n')
    generated_path.write_text(generated)
    graphics_path.write_text(graphics)
    shutil.copyfile(Path(__file__).with_name('native-water-probe.inc'),
                    root / 'source/animaltown_water_probe.inc')
    print('Water probe: inspection-only actual GX matrix/combiner commands forwarded unchanged')


if __name__ == '__main__':
    patch(Path(sys.argv[1]))
