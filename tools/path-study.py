"""Matched baseline/roads capture; disposable fixture card only. AVI disabled."""
from pathlib import Path
import sys
original=Path('/work/tools/iso-smoke.py')
source=original.read_text()
if not (Path(sys.argv[2])/'MemoryCardA.USA.raw').exists():raise SystemExit('Install a disposable path fixture first')
if '--lighting' not in sys.argv:sys.argv.append('--lighting')
start=source.index('def start_game(');end=source.index('\ndef ',start+1)
source=source[:start]+'''def start_game(new_town=False,prefix='paths'):
    wait_title();press('A');wait_profiles(prefix+'-picker.png');press('A')
    return None,wait_onboarding('world',prefix+'-world.png')
'''+source[end:]
start=source.index("        elif '--lighting' in sys.argv:");end=source.index("        elif '--terrain-scroll' in sys.argv:",start)
scene=r'''        elif '--lighting' in sys.argv:
            import re,math
            report={'renderer':'Dolphin OpenGL / llvmpipe','hardware_timing':False,'samples':[]}
            def logs():return (root/'emulator.log').read_text(errors='replace')
            def wait_pattern(pattern,offset=0,seconds=240):
                deadline=time.monotonic()+seconds
                while time.monotonic()<deadline:
                    match=re.search(pattern,logs()[offset:])
                    if match:return match,time.monotonic()
                    check_runtime_failure()
                    if p.poll() is not None:raise AssertionError('Dolphin exited during path capture')
                    time.sleep(.02)
                raise AssertionError('Path capture marker timed out: '+pattern)
            def chord(button):
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.2)
                os.write(controller,('PRESS '+button+'\n').encode());time.sleep(.2)
                os.write(controller,('RELEASE '+button+'\nRELEASE L\nRELEASE R\n').encode())
            start_game(False)
            wait_pattern(r'ANIMALTOWN READY ')
            time.sleep(3);screenshot('world-ready.png')
            offset=len(logs());chord('D_RIGHT')
            wait_pattern(r'ANIMALTOWN AASTUDY MODE 0 480-rim100',offset)
            time.sleep(8)
            def measure(name,moving=False):
                screenshot(name+'-before.png');offset=len(logs());chord('D_LEFT')
                begin,started=wait_pattern(r'ANIMALTOWN AASTUDY BEGIN (\d+) 0',offset);sequence=int(begin[1])
                if moving:os.write(controller,b'SET MAIN .9 .5\n')
                end,finished=wait_pattern(r'ANIMALTOWN AASTUDY END '+str(sequence)+r' 0 (\d+)',offset)
                os.write(controller,b'SET MAIN .5 .5\n');wait_pattern(r'PATH PROFILE DONE',offset)
                text=logs()[offset:]
                rows=[[int(v) for v in r] for r in re.findall(r'ANIMALTOWN AASTUDY ROW '+str(sequence)+r' 0 (\d+) (\d+) (\d+) (\d+) (\d+) (\d+) (\d+)',text)]
                cpu=[[int(v) for v in r] for r in re.findall(r'PATH PROFILE ROW (\d+) (\d+) (\d+) (\d+) (\d+) (\d+)',text)]
                if len(rows)!=240 or len(cpu)!=240:raise AssertionError('Incomplete frame or CPU rows')
                memory=re.search(r'PATH PROFILE MEMORY native (\d+) texture (\d+) path (\d+) aram (\d+) cache_peak (\d+) cache_reserved (\d+) dma_reads (\d+) bindings (\d+)',text)
                if not memory:raise AssertionError('Missing memory counters')
                percentile=lambda values,q:sorted(values)[min(len(values)-1,math.ceil(len(values)*q)-1)]
                times=[r[1] for r in rows[5:]];cpus=[r[1] for r in cpu[5:]]
                data={'name':name,'moving':moving,'frames':240,'guest_fps':len(times)*1e6/sum(times),'host_fps':240/(finished-started),'frame_ms_p50':percentile(times,.5)/1000,'frame_ms_p95':percentile(times,.95)/1000,'cpu_ms_mean':sum(cpus)/len(cpus)/1000,'cpu_ms_p95':percentile(cpus,.95)/1000,'terrain_submit_ms_mean':sum(r[3] for r in cpu[5:])/len(cpus)/1000,'gpu_wait_ms_mean':sum(r[2] for r in rows[5:])/len(cpus)/1000,'vi_wait_ms_mean':sum(r[3] for r in rows[5:])/len(cpus)/1000,'draws_mean':sum(r[5] for r in rows[5:])/len(cpus),'triangles_mean':sum(r[6] for r in rows[5:])/len(cpus),'allocations_total':sum(r[4] for r in cpu),'gc_collections':sum(r[5] for r in cpu),'memory':dict(zip(['native_bytes','texture_bytes','path_bytes','used_aram_bytes','geometry_cache_peak_bytes','geometry_cache_reserved_bytes','dma_reads','bindings'],[int(v) for v in memory.groups()])),'raw_frames':rows,'raw_cpu':cpu}
                report['samples'].append(data);(root/'measurements.json').write_text(json.dumps(report,indent=2)+'\n');screenshot(name+'-after.png')
            measure('stationary')
            if '--study-remote' in sys.argv and report['samples'][-1]['memory']['path_bytes']!=0:raise AssertionError('Path page retained without selected terrain demand')
            if '--study-travel' in sys.argv:measure('travel',True)
            if '--study-door' in sys.argv:
                press('A');time.sleep(8);building_view('indoor.png',True);measure('indoors')
                if report['samples'][-1]['memory']['path_bytes']!=0:raise AssertionError('Path texture retained indoors')
                press('A');time.sleep(8);building_view('outdoor-return.png',False);measure('returned')
                if report['samples'][-1]['memory']['path_bytes']==0:raise AssertionError('Path texture failed to reload outdoors')
            check_runtime_failure()
'''
source=source[:start]+scene+source[end:]
source=source.replace('DumpFrames = True','DumpFrames = False').replace('else:time.sleep(45)','else:time.sleep(5)')
sys.path.insert(0,str(original.parent))
exec(compile(source,str(original),'exec'),{'__name__':'__main__','__file__':str(original)})
