"""Instrument bundled allocation calls and bind system MEM1 sampling."""
from pathlib import Path
import re
import shutil
import sys


def patch(root):
    sources = [source for source in (root / 'source').glob('*.c')
               if source.name != 'animaltown_memory_pressure.c']
    include = '#include "native-memory-pressure.h"\n'
    for source in sources:
        if include in source.read_text():
            raise SystemExit(f'Native memory instrumentation was applied twice: {source.name}')
    generated_path = root / 'source/generated_program.c'
    generated = generated_path.read_text()
    pattern = (r'intptr_t (dnd_value_AnimalTown_GameCube_NativeMemory_Read_[a-z0-9]+)'
               r'\(intptr_t (\w+)\) \{.*?\r?\n\}')
    if len(re.findall(pattern, generated, re.S)) != 1:
        raise SystemExit('Native MEM1 sampler bridge changed: NativeMemory.Read')
    generated = re.sub(pattern,
                       r'intptr_t \1(intptr_t \2) { return animaltown_memory_read((DndArray*)\2); }',
                       generated, flags=re.S)
    generated = '#include "dnd_managed.h"\nextern int animaltown_memory_read(DndArray*);\n' + generated
    generated_path.write_text(generated)
    here = Path(__file__).resolve().parent
    shutil.copyfile(here / 'native-memory-pressure.h', root / 'include/native-memory-pressure.h')
    shutil.copyfile(here / 'native-memory-pressure.c', root / 'source/animaltown_memory_pressure.c')
    for source in sources:
        text = source.read_text()
        source.write_text(include + text)
    print('MEM1: actual free libc/arena bytes and allocation failures; no allocation probes')


if __name__ == '__main__':
    patch(Path(sys.argv[1]))
