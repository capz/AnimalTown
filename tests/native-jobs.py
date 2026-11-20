#!/usr/bin/env python3
"""Compile and run the actual job service through the pinned SDK AOT runtime."""
from pathlib import Path
import os
import subprocess
import tempfile

ROOT=Path(__file__).resolve().parents[1]
SDK=ROOT/'out/sdk-native'

def main():
    sources=('managed_runtime.c','async_runtime.c','stw.c','gc_stw.c','text.c','numeric.c','bcl.c')
    with tempfile.TemporaryDirectory(prefix='animaltown-jobs-aot-') as temporary:
        binary=Path(temporary)/'jobs'
        subprocess.run(['cc','-std=c11','-D_DEFAULT_SOURCE','-O1','-ffunction-sections','-fdata-sections',
                        '-pthread','-I'+str(SDK/'include'),str(ROOT/'out/job-smoke.c'),str(ROOT/'tests/native-jobs.c'),
                        *(str(SDK/'source'/s) for s in sources),'-Wl,--gc-sections','-lm','-o',str(binary)],check=True)
        for stress in (False,True):
            env=os.environ.copy()
            if stress:env['ANIMALTOWN_JOB_GC_STRESS']='1'
            subprocess.run([str(binary)],env=env,check=True,timeout=60)

if __name__=='__main__':main()
