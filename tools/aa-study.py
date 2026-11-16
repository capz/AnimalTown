"""Run the opt-in AA research build against a disposable existing town card.

Copy a saved MemoryCardA.USA.raw into OUTPUT first. Run once with --host-msaa 1
and once with --host-msaa 4. Host 4x MSAA previews are NOT native GC 3x samples.
FPS comes from real guest frame intervals and observed host benchmark duration;
AVI dumping is disabled so video encoding cannot distort the measurements.
"""
from pathlib import Path
import sys

original=Path(__file__).with_name('iso-smoke.py')
source=original.read_text()
if len(sys.argv)<3:raise SystemExit('aa-study.py ISO OUTPUT --host-msaa 1|4 --dsp-hle --component-cable --season spring')
if not (Path(sys.argv[2])/'MemoryCardA.USA.raw').exists():raise SystemExit('Copy a disposable existing town card into OUTPUT first')
host_msaa=int(sys.argv[sys.argv.index('--host-msaa')+1]) if '--host-msaa' in sys.argv else 1
if host_msaa not in (1,4):raise ValueError('Study host MSAA must be 1 or 4')
if '--lighting' not in sys.argv:sys.argv.append('--lighting')
start=source.index('def start_game(')
end=source.index('\ndef ',start+1)
resume=r'''def start_game(new_town=False,prefix='aa'):
    wait_title();press('A');wait_profiles(prefix+'-picker.png');press('A')
    return None,wait_onboarding('world',prefix+'-world.png')
'''
source=source[:start]+resume+source[end:]
start=source.index("        elif '--lighting' in sys.argv:")
end=source.index("        elif '--terrain-scroll' in sys.argv:",start)
scene=r'''        elif '--lighting' in sys.argv:
            import re,math
            start_game(False)
            report={'host_msaa':HOST_MSAA,'renderer':'Dolphin OpenGL / llvmpipe','hardware_timing':False,'samples':[]}
            def logtext():return (root/'emulator.log').read_text(errors='replace')
            def wait_pattern(pattern,offset=0,seconds=180):
                deadline=time.monotonic()+seconds
                while time.monotonic()<deadline:
                    match=re.search(pattern,logtext()[offset:])
                    if match:return match,time.monotonic()
                    check_runtime_failure()
                    if p.poll() is not None:raise AssertionError('Dolphin exited during AA study')
                    time.sleep(.02)
                raise AssertionError('AA study marker timed out: '+pattern)
            def chord(x):
                os.write(controller,b'PRESS L\nPRESS R\n');time.sleep(.12)
                os.write(controller,('SET C '+str(x)+' .5\n').encode())
            def release():
                os.write(controller,b'SET C .5 .5\n');time.sleep(.12)
                os.write(controller,b'RELEASE L\nRELEASE R\n');time.sleep(.12)
            def mode(expected):
                offset=len(logtext());chord(1)
                match,_=wait_pattern(r'AASTUDY MODE '+str(expected)+r' ([\w-]+) render (\d+) (\d+) output (\d+) (\d+) aa (\d+) rim (\d+) xfb_bytes (\d+) native_bytes (\d+)',offset)
                release();time.sleep(4)
                keys=['name','render_width','render_height','output_width','output_height','native_aa','rim_percent','xfb_bytes','sdk_native_bytes']
                data=dict(zip(keys,[match[1]]+[int(match[i]) for i in range(2,10)]));data['profile']=expected
                return data
            def measure(view,data):
                # Every sample is produced by FrameProfile.Complete; no guessed FPS.
                offset=len(logtext());chord(0)
                begin,start_time=wait_pattern(r'AASTUDY BEGIN (\d+) '+str(data['profile']),offset)
                seq=int(begin[1]);release()
                end,end_time=wait_pattern(r'AASTUDY END '+str(seq)+r' '+str(data['profile'])+r' (\d+)',offset)
                wait_pattern(r'AASTUDY DONE '+str(seq),offset)
                pattern=r'AASTUDY ROW '+str(seq)+r' '+str(data['profile'])+r' (\d+) (\d+) (\d+) (\d+) (\d+) (\d+) (\d+)'
                rows=[[int(x) for x in row] for row in re.findall(pattern,logtext()[offset:])]
                if len(rows)!=240 or [r[0] for r in rows]!=list(range(240)):raise AssertionError('Missing or duplicated AA samples')
                times=[r[1] for r in rows[5:]]
                if not times or min(times)<=0:raise AssertionError('Invalid native frame interval')
                percentile=lambda values,q:sorted(values)[min(len(values)-1,math.ceil(len(values)*q)-1)]
                sample={**data,'view':view,'sequence':seq,'samples':len(rows),'guest_fps':len(times)*1e6/sum(times),'frame_ms_p50':percentile(times,.5)/1000,'frame_ms_p95':percentile(times,.95)/1000,'host_presented_fps':len(rows)/(end_time-start_time),'guest_speed_percent':sum(r[1] for r in rows)/1e4/(end_time-start_time),'gpu_wait_ms_mean':sum(r[2] for r in rows)/len(rows)/1000,'animation_ms_mean':sum(r[4] for r in rows)/len(rows)/1000,'draws_min_max':[min(r[5] for r in rows),max(r[5] for r in rows)],'triangles_min_max':[min(r[6] for r in rows),max(r[6] for r in rows)],'raw_rows':rows}
                report['samples'].append(sample)
                (root/'measurements.json').write_text(json.dumps(report,indent=2)+'\n')
            wanted={0,1,2,4,5} if HOST_MSAA==1 else {0,3,4,6}
            for view in ['exterior','interior']:
                if view=='interior':
                    # Return to full resolution before entering the real house.
                    mode(0);press('A');time.sleep(5);building_view('interior-entered.png',True)
                    os.write(controller,b'SET MAIN .5 .2\n');time.sleep(.25);os.write(controller,b'SET MAIN .5 .5\n');time.sleep(2)
                    # mode(0) already selected it; cycle through the rest, then 0.
                    order=[1,2,3,4,5,6,0]
                else:order=list(range(7))
                for index in order:
                    data=mode(index)
                    if index not in wanted:continue
                    prefix=view+'-'+data['name'];screenshot(prefix+'.png')
                    measure(view,data)
                    if view=='exterior':
                        press('START');screenshot(prefix+'-menu.png');press('B');time.sleep(1)
            check_runtime_failure()
'''
source=source[:start]+scene+source[end:]
source=source.replace('MSAA = 1','MSAA = '+str(host_msaa))
source=source.replace('DumpFrames = True','DumpFrames = False')
source=source.replace('else:time.sleep(45)','else:time.sleep(5)')
sys.path.insert(0,str(original.parent))
exec(compile(source,str(original),'exec'),{'__name__':'__main__','__file__':str(original),'HOST_MSAA':host_msaa})
