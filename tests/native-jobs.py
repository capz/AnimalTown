#!/usr/bin/env python3
"""Compile and run the actual job service through the pinned SDK AOT runtime."""
from pathlib import Path
import os
import re
import shutil
import subprocess
import tempfile

ROOT=Path(__file__).resolve().parents[1]
SDK=ROOT/'out/sdk-native'

def main():
    sources=('managed_runtime.c','async_runtime.c','stw.c','gc_stw.c','text.c','numeric.c','bcl.c')
    generated=(ROOT/'out/job-smoke.c').read_text()
    pattern=r'intptr_t (dnd_value_AnimalTown_JobSmoke_NativeJobBlocks_Io_[a-z0-9]+)\(intptr_t (\w+), intptr_t (\w+), intptr_t (\w+), intptr_t (\w+)\) \{.*?\n\}'
    def bridge(match):
        fn,card,op,block,data=match.groups()
        return f'intptr_t {fn}(intptr_t {card},intptr_t {op},intptr_t {block},intptr_t {data}) {{return job_blocks_io((int){card},(int){op},(int){block},(DndArray*){data});}}'
    generated,count=re.subn(pattern,bridge,generated,flags=re.S)
    if count!=1:raise AssertionError('Native test card bridge signature changed')
    collect=r'intptr_t (dnd_value_AnimalTown_JobSmoke_NativeJobBlocks_Collect_[a-z0-9]+)\(void\) \{.*?\n\}'
    generated,count=re.subn(collect,r'intptr_t \1(void) {dnd_gc_collect(dnd_value_heap,NULL);return 0;}',generated,flags=re.S)
    if count!=1:raise AssertionError('Native test collection bridge signature changed')
    report=r'intptr_t (dnd_value_AnimalTown_JobSmoke_NativeJobBlocks_Report_[a-z0-9]+)\(intptr_t (\w+)\) \{.*?\n\}'
    generated,count=re.subn(report,lambda m:f'intptr_t {m[1]}(intptr_t {m[2]}) {{return job_report((int){m[2]},dnd_value_heap);}}',generated,flags=re.S)
    if count!=1:raise AssertionError('Native test measurement bridge signature changed')
    generated='#include "dnd_managed.h"\nextern int job_blocks_io(int,int,int,DndArray*);\nextern int job_report(int,DndManagedHeap*);\n'+generated
    with tempfile.TemporaryDirectory(prefix='animaltown-jobs-aot-') as temporary:
        source=Path(temporary)/'jobs.c';source.write_text(generated)
        include=Path(temporary)/'include';include.mkdir()
        header=(SDK/'include/dnd_managed.h').read_text()
        if header.count('    void *free_list;')!=1:raise AssertionError('Pinned heap layout changed')
        (include/'dnd_managed.h').write_text(header.replace('    void *free_list;','    void *free_list;\n    void *allocation_tail;'))
        shutil.copyfile(ROOT/'tools/native-frame-counters.h',include/'native-frame-counters.h')
        runtime=Path(temporary)/'managed_runtime.c'
        code=(SDK/'source/managed_runtime.c').read_text()
        start=code.index('static void rebuild_free_list(DndManagedHeap *heap) {')
        end=code.index('DndObject *dnd_object_new(',start)
        allocator=(ROOT/'tools/native-managed-allocator.inc').read_text()
        allocator=allocator.replace('TOWN_COUNT(TOWN_ALLOCATIONS);return object;','if(heap->used>job_heap_peak)job_heap_peak=heap->used;TOWN_COUNT(TOWN_ALLOCATIONS);return object;')
        diagnostic=r'fprintf(stderr,"OOM request=%zu live=",bytes); size_t live=0,available=0,largest=0; for(DndHeapBlock *b=first_block(heap);b;b=b->next){if(b->free){available+=b->size;if(b->size>largest)largest=b->size;}else{live+=b->size;if(b->size>65536)fprintf(stderr," [%s:%u]",block_object(b)->type->name,b->size);}}fprintf(stderr," %zu free=%zu largest=%zu\n",live,available,largest);'
        allocator=allocator.replace('dnd_exception_throw(DND_EXCEPTION_OUT_OF_MEMORY,"Managed heap exhausted.");',diagnostic+'dnd_exception_throw(DND_EXCEPTION_OUT_OF_MEMORY,"Managed heap exhausted.");')
        code=code[:start]+allocator+'\n'+code[end:]
        runtime.write_text('#include <stdio.h>\nextern size_t job_heap_peak;\n'+code)
        binary=Path(temporary)/'jobs'
        subprocess.run(['cc','-std=c11','-D_DEFAULT_SOURCE','-O1','-ffunction-sections','-fdata-sections',
                        '-pthread','-I'+str(include),'-I'+str(SDK/'include'),str(source),str(ROOT/'tests/native-jobs.c'),
                        *(str(runtime) if s=='managed_runtime.c' else str(SDK/'source'/s) for s in sources),'-Wl,--gc-sections','-lm','-o',str(binary)],check=True)
        for stress in (False,True):
            env=os.environ.copy();env.setdefault('ANIMALTOWN_JOB_HEAP_MIB','8')
            if stress:env['ANIMALTOWN_JOB_GC_STRESS']='1'
            subprocess.run([str(binary)],env=env,check=True,timeout=60)

if __name__=='__main__':main()
