#!/usr/bin/env python3
"""Run the production app bootstrap through the pinned SDK's host AOT runtime.

This exercises real SDK filesystem calls, not physical SD hardware or GX.
"""
from pathlib import Path
import os
import subprocess
import tempfile

ROOT=Path(__file__).resolve().parents[1]
SDK=ROOT/'out/sdk-native'
IDENTITY='ANIMALTOWN-CONTENT-1\n0123456789abcdef0123456789abcdef01234567\n1.2.3\n'+'A'*64+'\n0\n'

def main():
    sources=('storage.c','contiguous_write.c','file_optimize.c','file_extents.c','verified_copy.c','timing.c',
             'managed_runtime.c','async_runtime.c','stw.c','gc_stw.c','text.c','numeric.c','bcl.c','launch.c')
    with tempfile.TemporaryDirectory(prefix='animaltown-content-aot-') as temporary:
        work=Path(temporary);binary=work/'content-startup'
        subprocess.run(['cc','-std=c11','-D_DEFAULT_SOURCE','-DDND_CAPTURE_LAUNCH_ARGUMENTS','-O1',
                        '-ffunction-sections','-fdata-sections','-pthread','-I'+str(SDK/'include'),
                        str(ROOT/'out/content-startup.c'),str(ROOT/'tests/native-content-startup.c'),
                        *(str(SDK/'source'/s) for s in sources),'-Wl,--gc-sections','-lm','-o',str(binary)],check=True)
        def install(volume,device,directory='apps/animaltown',identity=IDENTITY,complete=True):
            app=volume/device/directory;app.mkdir(parents=True)
            for name in ('default.dol','wordmark.dndtex','buildings.bin','savebanner.rgb5a3','saveicon.rgb5a3'):
                if complete or name!='saveicon.rgb5a3':(app/name).write_bytes(b'fixture')
            (app/'content.id').write_text(identity,encoding='ascii');(app/'runtime.cfg').write_bytes(b'\0')
            (app/'fox-tex128.dndmodel').write_bytes(bytes((41,42)))
        for stress in (False,True):
            for case in range(6):
                volume=work/f'volumes-{int(stress)}-{case}';volume.mkdir()
                # Even a matching disc must never be mounted/borrowed by an app.
                install(volume,'dvd','')
                args=[]
                if case==0:install(volume,'cardb')
                elif case==1:
                    install(volume,'cardb','relocated/My Town');args=['sdb:/relocated/My Town/default.dol','cardb:/relocated/My Town/']
                elif case==2:
                    install(volume,'sd');install(volume,'cardb');args=['host:/unknown/default.dol','sd:/apps/animaltown/']
                elif case==3:
                    install(volume,'cardb',identity='wrong build');install(volume,'sd');args=['sdb:/apps/animaltown/default.dol','','reject']
                elif case==4:
                    install(volume,'sd',complete=False);install(volume,'cardb');args=['host:/unknown/default.dol','','reject']
                else:args=['host:/unknown/default.dol','','reject']
                env=os.environ.copy();env['DND_STORAGE_ROOT']=str(volume)
                if stress:env['ANIMALTOWN_CONTENT_GC_STRESS']='1'
                subprocess.run([str(binary),*args],env=env,check=True,timeout=30)
    print('12 production bootstrap AOT cases passed, including GC stress; physical SD/launcher qualification remains pending')

if __name__=='__main__':main()
