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
    def presentations(count):
        clock.frames();target=len(clock.presentations)+count;deadline=time.monotonic()+90
        while True:
            clock.frames()
            if len(clock.presentations)>=target:return
            alive()
            if time.monotonic()>deadline:raise AssertionError('Guest presentations stopped')
            time.sleep(.01)
    def press(button):
        os.write(c.controller,f'PRESS {button}\n'.encode());presentations(2)
        os.write(c.controller,f'RELEASE {button}\n'.encode());presentations(2)
    def move(x,z,seconds):
        os.write(c.controller,f'SET MAIN {.5+x*.5} {.5-z*.5}\n'.encode())
        try:guest(seconds)
        finally:os.write(c.controller,b'SET MAIN .5 .5\n')
        presentations(2)
    def chord(button):
        os.write(c.controller,b'PRESS L\nPRESS R\n');presentations(2);press(button);os.write(c.controller,b'RELEASE L\nRELEASE R\n');presentations(2)
    def view():
        offset=len(c.runtime_log_text());chord('Y');wait_log('ANIMALTOWN JOB VIEW END',offset)
        text=c.runtime_log_text()[offset:]
        match=re.search(r'ANIMALTOWN JOB VIEW (\S+) ((?:-?\d+ ?){10})',text)
        if not match:raise AssertionError('Missing read-only job view')
        a=list(map(int,match[2].split()))
        npcs={int(r[0]):list(map(int,r[1:])) for r in re.findall(r'ANIMALTOWN JOB NPC (\d+) (-?\d+) (-?\d+) (-?\d+) (\d+) (-?\d+) (-?\d+)',text)}
        objects={int(r[0]):list(map(int,r[1:])) for r in re.findall(r'ANIMALTOWN JOB OBJECT (\d+) (\d+) (-?\d+) (-?\d+) (\d+)',text)}
        jobs=[list(map(int,r)) for r in re.findall(r'ANIMALTOWN JOB STATE (\d+) (\d+) (\d+) (-?\d+) (-?\d+)',text)]
        return dict(screen=match[1],room=a[0],x=a[1]/1024,z=a[2]/1024,npc=a[3],mode=a[4],choice=a[5],typed=a[6],length=a[7],selected=a[8],tab=a[9],npcs=npcs,objects=objects,jobs=jobs)
    def go(x,z,tolerance=.6):
        for _ in range(100):
            v=view();dx=x-v['x'];dz=z-v['z'];d=math.hypot(dx,dz)
            if d<tolerance:return v
            move(dx/d,dz/d,min(.8,(d-tolerance*.5)/2.6775))
        raise AssertionError('Could not walk to '+str((x,z)))
    def approach(npc):
        for attempt in range(80):
            v=view();n=v['npcs'][npc]
            if n[0]!=v['room']:raise AssertionError('NPC changed rooms during capture: '+str(n))
            dx=n[1]/1024-v['x'];dz=n[2]/1024-v['z'];d=math.hypot(dx,dz)
            if d<1.35 and not n[3]:return
            if d<.1:guest(.2);continue
            move(dx/d,dz/d,min(.8,max(.06,(d-1.1)/2.6775)))
        raise AssertionError('Could not reach moving NPC '+str(npc))
    def dialogue():
        for _ in range(12):
            v=view()
            if v['screen']!='dialogue' or v['typed']>=v['length']:return v
            guest(min(2,(v['length']-v['typed'])/30+.1))
        raise AssertionError('Dialogue stopped revealing text')
    def talk(npc=0):
        approach(npc);press('A');v=dialogue()
        if v['screen']!='dialogue' or v['npc']!=npc:raise AssertionError('Conversation did not reach the intended NPC: '+str(v))
    def close():press('B');guest(.25)
    def accept():
        talk();shot('npc-choices');press('A');v=dialogue()
        if v['mode']!=2:raise AssertionError('Stored offer did not open its acceptance choices')
        shot('offer');press('A');dialogue();shot('accepted');close()
    def handoff(selector=False,npc=0):
        talk(npc);press('A');presentations(4);shot('handoff-list');press('A');dialogue()
        if selector:shot('contribution');press('A');dialogue()
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
    if mode=='template':
        c.wait_title()
        if not (c.root/'MemoryCardA.USA.raw').is_file():raise AssertionError('Dolphin did not format its disposable template')
        return
    c.start_game(False,'jobs-'+mode);wait_log('ANIMALTOWN READY ');presentations(2);shot('world')
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
            handoff(npc=1)
            if mode=='retrieval':handoff()
        elif mode in ('fruit','fish','insect'):handoff(True)
        elif mode=='ball':
            for _ in range(60):
                v=view()
                if any(j[1]==6 and j[2]==4 for j in v['jobs']):break
                ball=next(o for o in v['objects'].values() if o[0]==1);n=v['npcs'][0];dx=n[1]/1024-ball[1]/1024;dz=n[2]/1024-ball[2]/1024;d=math.hypot(dx,dz)
                go(ball[1]/1024-dx/d*.57,ball[2]/1024-dz/d*.57,.15);move(dx/d,dz/d,.6)
            shot('ball');handoff()
        elif mode=='snowman':go(30,41.5);move(0,1,1);shot('snowman');handoff()
        elif mode=='flowers':
            go(27,42.5,.15)
            for pocket in range(1,5):
                move(0,-1,.025);press('X')
                for _ in range(pocket):press('D_RIGHT')
                press('A');press('D_DOWN');press('A');guest(2);close();close();move(1,0,.38)
            shot('planted');handoff()
        else:raise ValueError('Unsupported job smoke mode: '+mode)
        save()
    alive();report['controls_completed']=True
    (c.root/'jobs-report.json').write_text(json.dumps(report,indent=2)+'\n')
