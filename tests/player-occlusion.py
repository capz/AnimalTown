"""Compile production model submission against the pinned SDK's recording GX.

Uses the exact guarded patch table consumed by patch-native.ps1. Everything is
built in a temporary directory; the vendor checkout is never modified.
"""
import json
import os
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SDK = ROOT / 'vendor/sdk'


def replace_once(text, old, new):
    if text.count(old) != 1:
        raise RuntimeError('Pinned player occlusion test/hook changed: ' + old[:80])
    return text.replace(old, new)


def main():
    with tempfile.TemporaryDirectory(prefix='animaltown-occlusion-') as directory:
        work = Path(directory)
        source = (SDK / 'source/model_graphics.c').read_text()
        for patch in json.loads((ROOT / 'tools/player-occlusion-patches.json').read_text()):
            source = replace_once(source, patch['find'], patch['replace'])
        (work / 'model_graphics.c').write_text(source)
        (work / 'animaltown_player_occlusion.inc').write_text(
            (ROOT / 'tools/native-player-occlusion.inc').read_text())
        header = (SDK / 'tests/model_effect_sdk/gccore.h').read_text()
        (work / 'gccore.h').write_text(header + '\n#define GX_GREATER 4\n#define GX_CC_ONE 12\n')
        fixture = (SDK / 'tests/model_effect_test.c').read_text()
        fixture = replace_once(fixture, 'int count,texgens,cull,zwrite,blend,src,dst,compare,cutoff;',
                               'int count,texgens,cull,zwrite,zcompare,blend,src,dst,compare,cutoff;')
        fixture = replace_once(fixture,
            'void GX_SetZMode(int e,int c,int w){assert(e&&c==GX_LEQUAL);state.zwrite=w;}',
            'void GX_SetZMode(int e,int c,int w){assert(e&&(c==GX_LEQUAL||c==GX_GREATER));state.zwrite=w;state.zcompare=c;}')
        marker = ' assert(dnd_draw_release(short_effect)==0&&'
        checks = (ROOT / 'tests/player-occlusion-checks.inc').read_text()
        fixture = replace_once(fixture, marker, checks + '\n' + marker)
        fixture = 'int animaltown_player_occlusion(int);\n' + fixture
        (work / 'recording.c').write_text(fixture)
        flags = ['-pthread', '-O1', '-g', '-std=c11', '-Wall', '-Wextra', '-Werror',
                 '-DDND_MODEL_GX_TEST', '-I' + str(work), '-I' + str(SDK / 'include'),
                 '-ffunction-sections', '-fdata-sections']
        cc = os.environ.get('CC', 'cc')
        def run(*args):
            subprocess.run([str(x) for x in args], check=True, timeout=120)
        objects = [work / 'recording.o']
        run(cc, *flags, '-DDND_MODEL_ALLOC_COMPILE_TEST', '-c', work / 'recording.c', '-o', objects[0])
        for name in ['managed_runtime', 'async_runtime', 'model', 'model_animation',
                     'model_graphics', 'graphics_device', 'draw_resources', 'performance']:
            obj = work / (name + '.o')
            path = work / 'model_graphics.c' if name == 'model_graphics' else SDK / ('source/' + name + '.c')
            # GCC 14 flags the SDK's scoped GC-root registration when inlined.
            # Keep that unchanged support runtime uninlined, retaining -Werror;
            # the model renderer and every other unit remain optimized.
            support_flags = ['-O0'] if name == 'managed_runtime' else []
            run(cc, *flags, *support_flags, '-Dmalloc=__wrap_malloc', '-Dcalloc=__wrap_calloc',
                '-Drealloc=__wrap_realloc', '-Daligned_alloc=__wrap_aligned_alloc', '-c', path, '-o', obj)
            objects.append(obj)
        binary = work / 'player-occlusion'
        run(cc, *objects, '-pthread', '-Wl,--gc-sections', '-lm', '-o', binary)
        run(binary)


if __name__ == '__main__':
    main()
