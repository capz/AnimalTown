"""Controller qualification on disposable --job-fixture cards.

The companion --job-verify command checks the saved domain state; images alone
are not proof of a completed errand. Guest movement uses NTSC VI, not wall time.
"""
from pathlib import Path
from types import SimpleNamespace
import json
import math
import os
import re
import sys
import time

def run(context):
    c=SimpleNamespace(**context)
    mode=sys.argv[sys.argv.index('--jobs')+1]
    sys.path.insert(0,str(Path(c.__file__).resolve().parents[1]/'docs/performance'))
    from avi_guest_clock import AviGuestClock
    clock=AviGuestClock(c.user/'Dump/Frames',track_presentations=True)
    shots=[]
    def alive():
        c.check_runtime_failure()
        if c.p.poll() is not None:raise AssertionError('Dolphin stopped during offline job qualification')
    def wait_log(pattern,offset=0,seconds=180):
        deadline=time.monotonic()+seconds
        while time.monotonic()<deadline:
            alive();match=re.search(pattern,c.runtime_log_text()[offset:])
            if match:return match
            time.sleep(.1)
        raise AssertionError('Missing job runtime marker: '+pattern)
    def guest(seconds):
        target=clock.frames()+math.ceil(seconds*60000/1001);deadline=time.monotonic()+max(60,seconds*20)
        while clock.frames()<target:
            alive()
            if time.monotonic()>deadline:raise AssertionError('Guest VI clock stopped during '+mode)
            time.sleep(.025)
    def shot(name):
        name='jobs-'+mode+'-'+name+'.png';c.screenshot(name);shots.append(name)
    def press(button):c.press_presented(button,clock)
    def move(x,z,seconds):
        os.write(c.controller,f'SET MAIN {.5+x*.5} {.5-z*.5}\n'.encode())
        try:guest(seconds)
        finally:os.write(c.controller,b'SET MAIN .5 .5\n')
        guest(.2)
    def chord(button):
        os.write(c.controller,b'PRESS L\nPRESS R\n');guest(.2);press(button);os.write(c.controller,b'RELEASE L\nRELEASE R\n');guest(.3)
    def talk():press('A');guest(3)
    def close():press('B');guest(.25)
    def accept():
        talk();shot('npc-choices');press('A');guest(4);shot('offer');press('A');guest(4);shot('accepted');close()
    def handoff(selector=False):
        talk();press('A');guest(.4);shot('handoff-list');press('A');guest(3)
        if selector:shot('contribution');press('A');guest(3)
        shot('result');close()
    def records():
        folder=c.root/'records';folder.mkdir(exist_ok=True)
        for name,data in c.options_card_files().items():(folder/name).write_bytes(data)
    def save():
        previous=c.committed_card_sequence(c.root/'MemoryCardA.USA.raw',details=True)
        c.save_menu();c.wait_saved_after(previous);shot('saved');press('B');records()
    def profile():
        chord('D_RIGHT');wait_log(r'ANIMALTOWN AASTUDY MODE 0 480-rim100');guest(2)
        offset=len(c.runtime_log_text());started=time.monotonic();chord('D_LEFT')
        begin=wait_log(r'ANIMALTOWN AASTUDY BEGIN (\d+) 0',offset);sequence=int(begin[1]);wait_log(r'ANIMALTOWN AASTUDY END '+str(sequence)+r' 0 240',offset,300);elapsed=time.monotonic()-started;wait_log('PATH PROFILE DONE',offset)
        text=c.runtime_log_text()[offset:]
        rows=[[int(v) for v in r] for r in re.findall(r'ANIMALTOWN AASTUDY ROW '+str(sequence)+r' 0 (\d+) (\d+) (\d+) (\d+) (\d+) (\d+) (\d+)',text)]
        cpu=[[int(v) for v in r] for r in re.findall(r'PATH PROFILE ROW (\d+) (\d+) (\d+) (\d+) (\d+) (\d+)',text)]
        if len(rows)!=240 or len(cpu)!=240:raise AssertionError('Incomplete frame/CPU measurement')
        memory=re.search(r'PATH PROFILE MEMORY native (\d+) texture (\d+) path (\d+) aram (\d+) cache_peak (\d+) cache_reserved (\d+) dma_reads (\d+) bindings (\d+)',text)
        managed=re.findall(r'ANIMALTOWN MEMORY managed_used (\d+) managed_peak (\d+) managed_capacity (\d+) collections (\d+)',c.runtime_log_text())
        pct=lambda values,q:sorted(values)[min(len(values)-1,math.ceil(len(values)*q)-1)]
        times=[r[1] for r in rows[5:]];cpus=[r[1] for r in cpu[5:]]
        return {'hardware':False,'renderer':'Dolphin OpenGL / llvmpipe','frames':240,'host_fps_including_input':240/elapsed,'guest_fps':1e6*len(times)/sum(times),'frame_ms_p50':pct(times,.5)/1000,'frame_ms_p95':pct(times,.95)/1000,'cpu_ms_mean':sum(cpus)/len(cpus)/1000,'cpu_ms_p95':pct(cpus,.95)/1000,'allocations':sum(r[4] for r in cpu),'collections':sum(r[5] for r in cpu),'memory':dict(zip(('native_bytes','texture_bytes','path_bytes','aram_bytes','cache_peak_bytes','cache_reserved_bytes','dma_reads','bindings'),map(int,memory.groups()))) if memory else None,'managed':dict(zip(('used_extent_bytes','peak_extent_bytes','capacity_bytes','collections'),map(int,managed[-1]))) if managed else None,'raw_frames':rows,'raw_cpu':cpu}
    c.start_game(False,'jobs-'+mode);wait_log('ANIMALTOWN READY ');guest(2);shot('world')
    report={'scenario':'offline-jobs','mode':mode,'screenshots':shots,'passed':False}
    if mode in ('baseline','outfits-off','outfits-on','maximum-mail'):
        report['profile']=profile();shot('profiled')
    elif mode=='mail':
        # Fixture starts beside the front door of the resident's own house.
        press('A');guest(1)
        for _ in range(4):press('D_DOWN')
        press('A');shot('mailbox');press('A');guest(1);shot('letter');press('X');guest(1);shot('attachment');close();close();save()
    else:
        accept()
        if mode in ('delivery','property','retrieval'):
            press('X');shot('parcel');press('R');press('A');shot('jobs-log');close();close()
            move(1,0,1.5);handoff()
            if mode=='retrieval':move(-1,0,1.5);move(0,-1,.05);handoff()
        elif mode in ('fruit','fish','insect'):handoff(True)
        elif mode=='ball':move(0,-1,.5);handoff()
        elif mode=='snowman':move(0,1,.6);move(0,-1,1.2);shot('snowman');handoff()
        elif mode=='flowers':
            move(-1,0,1.12)
            for pocket in range(1,5):
                move(0,-1,.025);press('X')
                for _ in range(pocket):press('D_RIGHT')
                press('A');press('D_DOWN');press('A');guest(2);close();close();move(1,0,.38)
            shot('planted');move(1,0,.1);move(0,-1,.025);handoff()
        else:raise ValueError('Unsupported job smoke mode: '+mode)
        save()
    alive();report['controls_completed']=True
    (c.root/'jobs-report.json').write_text(json.dumps(report,indent=2)+'\n')
