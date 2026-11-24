#!/usr/bin/env python3
"""Run both production bootstrap profiles through the pinned SDK's AOT runtime.

The host backend supplies mounted directories (including a current FST root),
not resolver results. Real registry/path/sync/async code runs with and without
GC stress. Physical drive, adapter, launcher and throughput tests are separate.
"""
from pathlib import Path
import os
import subprocess
import sys
import struct
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SDK = ROOT / 'out/sdk-native'
IDENTITY = 'ANIMALTOWN-CONTENT-1\n0123456789abcdef0123456789abcdef01234567\n1.2.3\n' + 'A' * 64 + '\n0\n'


def app(device, directory='apps/animaltown', **options):
    return dict(device=device, directory=directory, **options)


def scenario(profile, name, files=(), *, hint=None, root=None, result=0, device=None,
             attempts=(), borrow=None, disc_identity=IDENTITY):
    return dict(profile=profile, name=name, files=files, hint=hint, root=root,
                result=result, device=device, attempts=attempts, borrow=borrow,
                disc_identity=disc_identity)


def install(volume, device, directory='apps/animaltown', identity=IDENTITY,
            missing=None, config=b'\0', dol=True):
    target = volume / (device + ':') / directory
    target.mkdir(parents=True, exist_ok=True)
    names = ['wordmark.dndtex', 'buildings.bin', 'savebanner.rgb5a3', 'saveicon.rgb5a3']
    if dol:
        names.append('default.dol')
    for name in names:
        if name != missing:
            (target / name).write_bytes(b'fixture')
    (target / 'content.id').write_text(identity, encoding='ascii')
    (target / 'runtime.cfg').write_bytes(config)
    (target / 'fox-tex128.dndmodel').write_bytes(bytes((41, 42)))
    atlas = bytearray(struct.pack('<8I', 0x58544e44, 1, 3, 512, 512, 524288, 0, 0)) + bytearray([37]) * 524288
    for row in range(64, 96):
        atlas[32 + (row * 128 + 96) * 32:32 + (row * 128 + 128) * 32] = bytes([173]) * 1024
    (target / 'atlas.dndtex').write_bytes(atlas)
    return target


def main():
    cases = [
        scenario('depot', 'no-argv-slot-b', [app('cardb')], device=1, attempts=(2, 1)),
        scenario('depot', 'relocated-slot-b', [app('cardb', 'relocated/My Town \u00fc')],
                 hint='sdb:/relocated/My Town \u00fc/default.dol', root='cardb:/relocated/My Town \u00fc/', device=1, attempts=(1,)),
        scenario('depot', 'unknown-hint-fixed-priority', [app('sd'), app('cardb')],
                 hint='host:/unknown/default.dol', root='sd:/apps/animaltown/', device=2, attempts=(2,)),
        scenario('depot', 'explicit-identity-mismatch', [app('cardb', identity='wrong build'), app('sd')],
                 hint='sdb:/apps/animaltown/default.dol', result=10, attempts=(1,)),
        scenario('depot', 'partial-matching-fallback-stops', [app('sd', missing='saveicon.rgb5a3'), app('cardb')],
                 result=10, attempts=(2,)),
        scenario('depot', 'absent-app-never-uses-disc', result=10, attempts=(2, 1, 6)),
        scenario('depot', 'explicit-sd-never-borrows-slot-b', [app('cardb')],
                 hint='sd:/apps/animaltown/default.dol', result=10, attempts=(2,)),
        scenario('depot', 'explicit-sd-sp2', [app('sd'), app('cardb')],
                 hint='sd:/apps/animaltown/default.dol', root='sd:/apps/animaltown/', device=2, attempts=(2,)),
        scenario('depot', 'explicit-sdc-sp2', [app('sd')],
                 hint='sdc:/apps/animaltown/default.dol', root='sd:/apps/animaltown/', device=2, attempts=(2,)),
        scenario('depot', 'relocated-gcloader', [app('gcloader', 'games/My Town'), app('sd')],
                 hint='gcldr:/games/My Town/default.dol', root='gcloader:/games/My Town/', device=6, attempts=(6,)),
        scenario('depot', 'swiss-dvd-means-raw-gcloader', [app('gcloader', 'games/My Town'), app('sd')],
                 hint='dvd:/games/My Town/default.dol', root='gcloader:/games/My Town/', device=6, attempts=(6,)),
        scenario('depot', 'explicit-slot-a', [app('carda')],
                 hint='sda:/apps/animaltown/default.dol', root='carda:/apps/animaltown/', device=0, attempts=(0,)),
        scenario('depot', 'no-argv-gcloader', [app('gcloader')], device=6, attempts=(2, 1, 6)),
        scenario('depot', 'stale-fallback-released', [app('sd', identity='another build'), app('cardb')], device=1, attempts=(2, 1)),
        scenario('depot', 'malformed-hint-does-not-probe', [app('cardb'), app('sd')],
                 hint='sdb:/../default.dol', result=10),
        scenario('depot', 'missing-hinted-dol', [app('cardb', missing='default.dol'), app('sd')],
                 hint='sdb:/apps/animaltown/default.dol', result=10, attempts=(1,)),
        scenario('depot', 'non-dol-hint-is-rejected', [app('cardb'), app('sd')],
                 hint='sdb:/apps/animaltown/wordmark.dndtex', result=10, attempts=(1,)),
        scenario('depot', 'oversized-identity-is-rejected', [app('cardb', identity='X' * 513), app('sd')],
                 hint='sdb:/apps/animaltown/default.dol', result=10, attempts=(1,)),
        scenario('depot', 'borrowed-source-survives', [app('cardb')],
                 hint='sdb:/apps/animaltown/default.dol', root='cardb:/apps/animaltown/', device=1, borrow=1),
        scenario('depot', 'borrowed-rejected-source-survives', [app('cardb', identity='wrong build'), app('sd')],
                 hint='sdb:/apps/animaltown/default.dol', result=10, device=1, borrow=1),
        scenario('depot', 'invalid-runtime-profile', [app('cardb', config=b'\x08')],
                 hint='sdb:/apps/animaltown/default.dol', root='cardb:/apps/animaltown/', result=20, device=1, attempts=(1,)),
        scenario('depot', 'invalid-runtime-length', [app('cardb', config=b'\0\0')],
                 hint='sdb:/apps/animaltown/default.dol', root='cardb:/apps/animaltown/', result=20, device=1, attempts=(1,)),
        scenario('disc', 'no-argv-current-fst', [app('sd')], device=3, attempts=(3,)),
        scenario('disc', 'ignores-app-launcher-hint', [app('cardb')],
                 hint='sdb:/apps/animaltown/default.dol', root='ignored', device=3, attempts=(3,)),
        scenario('disc', 'wrong-disc-never-borrows-app', [app('sd'), app('cardb')],
                 hint='sd:/apps/animaltown/default.dol', result=10, attempts=(3,), disc_identity='another disc build'),
        scenario('disc', 'absent-disc-never-borrows-app', [app('sd')], result=10, attempts=(3,), disc_identity=None),
    ]
    sources = ('contiguous_write.c', 'file_optimize.c', 'file_extents.c', 'verified_copy.c', 'timing.c',
               'managed_runtime.c', 'async_runtime.c', 'stw.c', 'gc_stw.c', 'text.c', 'numeric.c', 'bcl.c', 'launch.c')
    dead_strip = '-Wl,-dead_strip' if sys.platform == 'darwin' else '-Wl,--gc-sections'
    flags = ['cc', '-std=c11', '-D_DEFAULT_SOURCE', '-D_DARWIN_C_SOURCE', '-DDND_CAPTURE_LAUNCH_ARGUMENTS',
             '-O1', '-Wall', '-Wextra', '-ffunction-sections', '-fdata-sections', '-pthread', '-I' + str(SDK / 'include')]
    with tempfile.TemporaryDirectory(prefix='animaltown-content-aot-') as temporary:
        work = Path(temporary)
        # Only the small declarations/time hooks required to compile the real
        # GameCube storage branch. No SDK implementation is replaced here.
        headers = work / 'backend-headers'
        (headers / 'ogc').mkdir(parents=True)
        (headers / 'fat.h').write_text('int FAT_getAttr(const char *path);\nint FAT_setAttr(const char *path,unsigned attributes);\n')
        (headers / 'ogc/lwp_watchdog.h').write_text('#include <stdint.h>\nstatic inline uint64_t gettime(void){return 0;}\nstatic inline uint64_t ticks_to_microsecs(uint64_t value){return value;}\n')
        objects = []
        for source in sources:
            obj = work / (source + '.o')
            subprocess.run([*flags, '-c', str(SDK / 'source' / source), '-o', str(obj)], check=True)
            objects.append(str(obj))
        binaries = {}
        for profile in ('depot', 'disc'):
            binary = work / ('content-startup-' + profile)
            subprocess.run([*flags, '-DDND_GAMECUBE_STORAGE', '-I' + str(headers),
                            str(ROOT / ('out/content-startup-' + profile + '.c')),
                            str(ROOT / 'tests/native-content-startup.c'), str(SDK / 'source/storage.c'),
                            *objects, dead_strip, '-lm', '-o', str(binary)], check=True)
            binaries[profile] = binary
        count = 0
        for stress in (False, True):
            for case in cases:
                label = case['profile'] + '/' + case['name']
                print(label + (' with GC stress' if stress else ''), flush=True)
                volume = work / ('volumes-' + str(int(stress)) + '-' + case['name'])
                volume.mkdir()
                # Depot cases deliberately include an exact matching disc.
                # Disc payloads intentionally omit an app's default.dol.
                if case['disc_identity'] is not None:
                    install(volume, 'dvd', '', identity=case['disc_identity'], dol=False)
                for package in case['files']:
                    install(volume, **package)
                env = os.environ.copy()
                env['ANIMALTOWN_CONTENT_RESULT'] = str(case['result'])
                env['ANIMALTOWN_CONTENT_MOUNTS'] = str(0 if case['device'] is None else 1 << case['device'])
                env['ANIMALTOWN_CONTENT_ATTEMPTS'] = ','.join(map(str, case['attempts']))
                env.pop('ANIMALTOWN_CONTENT_GC_STRESS', None)
                env.pop('ANIMALTOWN_CONTENT_BORROW', None)
                if stress:
                    env['ANIMALTOWN_CONTENT_GC_STRESS'] = '1'
                if case['borrow'] is not None:
                    env['ANIMALTOWN_CONTENT_BORROW'] = str(case['borrow'])
                args = [] if case['hint'] is None else [case['hint'], case['root'] or '']
                subprocess.run([str(binaries[case['profile']]), *args], cwd=volume, env=env, check=True, timeout=30)
                count += 1
    print(f'{count} production bootstrap AOT cases passed across disc/Depot profiles, launcher aliases, '
          'identity/fallback policy, runtime config, mount lifetime and GC-stressed streaming; '
          'physical adapter/launcher qualification remains separate')


if __name__ == '__main__':
    main()
