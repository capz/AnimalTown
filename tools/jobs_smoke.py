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
        clock.frames();target=len(clock.presentations)+count;deadline=time.monotonic()+(1800 if mode.startswith('maximum-') else 90)
        while True:
            clock.frames()
            if len(clock.presentations)>=target:return
            alive()
            if time.monotonic()>deadline:raise AssertionError('Guest presentations stopped')
            time.sleep(.01)
    def press(button):
        os.write(c.controller,f'PRESS {button}\n'.encode());presentations(6)
        os.write(c.controller,f'RELEASE {button}\n'.encode());presentations(4)
    def move(x,z,seconds,run=False):
        os.write(c.controller,f'SET MAIN {.5+x*.5} {.5-z*.5}\n'.encode())
        if run:os.write(c.controller,b'PRESS B\n')
        try:guest(seconds)
        finally:os.write(c.controller,b'SET MAIN .5 .5\nRELEASE B\n')
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
            move(dx/d,dz/d,min(.8,(d-tolerance*.5)/5.04),True)
        raise AssertionError('Could not walk to '+str((x,z)))
    def approach(npc):
        for attempt in range(80):
            v=view();n=v['npcs'][npc]
            if n[0]!=v['room']:
                if v['room']!=0:
                    size=8 if v['room']==24 else 4
                    go(size*.5,size-.65,.08);move(0,1,.04);press('A');guest(1)
                else:
                    space=n[0]
                    if space==24:x,z=32.5,28.5
                    elif space==25:x,z=50.5,28.5
                    elif 16<=space<=21:
                        owner=v['npcs'][space-16];x,z=owner[4]+1.5,owner[5]+3.5
                    else:raise AssertionError('Unexpected NPC visit space '+str(space))
                    go(x,z+1,.12);move(0,-1,.04);press('A');guest(1)
                continue
            dx=n[1]/1024-v['x'];dz=n[2]/1024-v['z'];d=math.hypot(dx,dz)
            if d<1.35 and not n[3]:return
            if d<.1:guest(.2);continue
            move(dx/d,dz/d,min(.8,max(.035,(d-1.1)/5.04)),True)
        raise AssertionError('Could not reach moving NPC '+str(npc))
    def dialogue():
        for _ in range(12):
            v=view()
            if v['screen']!='dialogue' or v['typed']>=v['length']:return v
            guest(min(2,(v['length']-v['typed'])/30+.1))
        raise AssertionError('Dialogue stopped revealing text')
    def talk(npc=0):
        for _ in range(8):
            approach(npc);v=view();n=v['npcs'][npc];dx=v['x']-n[1]/1024;dz=v['z']-n[2]/1024;d=math.hypot(dx,dz)
            # Look away from a shared doorway so A addresses the villager
            # instead of taking the higher-priority door interaction.
            if d>.05:move(dx/d,dz/d,.04)
            press('A');v=dialogue()
            if v['screen']=='dialogue' and v['npc']==npc:return
            if v['screen']=='dialogue':close();continue
            if v['screen']!='world':break
        raise AssertionError('Conversation did not reach the intended NPC: '+str(v))
    def close():press('B');guest(.25)
    def accept():
        talk();shot('npc-choices');press('A');v=dialogue()
        if v['mode']!=2:raise AssertionError('Stored offer did not open its acceptance choices')
        shot('offer');press('A');dialogue();shot('accepted');close()
    def handoff(selector=False,npc=0):
        talk(npc);press('A');presentations(4);shot('handoff-list');press('A');dialogue()
        if selector:
            shot('contribution');press('A');v=dialogue()
            if v['screen']=='job-select':press('A');v=dialogue()
            if v['screen']=='job-select':raise AssertionError('Contribution input did not leave the selector')
        shot('result');close()
    def records():
        folder=c.root/'records';folder.mkdir(exist_ok=True)
        for name,data in c.options_card_files().items():(folder/name).write_bytes(data)
    def save(label='saved'):
        previous=c.committed_card_sequence(c.root/'MemoryCardA.USA.raw',details=True)
        if mode!='baseline':
            for _ in range(6):
                if view()['screen']=='world':break
                press('B')
            else:raise AssertionError('Could not close the interaction before saving')
        offset=len(c.runtime_log_text());press('START');os.write(c.controller,b'PRESS A\n')
        try:c.wait_saved_after(previous,timeout_seconds=900 if mode.startswith('maximum-') else 240)
        finally:os.write(c.controller,b'RELEASE A\n')
        wait_log('ANIMALTOWN SAVE TOTAL:',offset,900);presentations(4);shot(label);press('B');records()
    def travel():
        go(36.5,11.5,.1);press('A');presentations(4);shot('travel');press('A');press('D_DOWN');shot('travel-confirm')
        offset=len(c.runtime_log_text());press('A');wait_log('ANIMALTOWN READY ',offset,1800 if mode.startswith('maximum-') else 360);presentations(4);shot('arrived')
    def type_text(text):
        from collections import deque
        keys="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 .,!?'\n-:;()";cursor=0
        for char in text:
            target=keys.index(char);queue=deque([(cursor,[])]);seen={cursor}
            while queue:
                at,route=queue.popleft()
                if at==target:break
                for label,step in (('D_LEFT',-1),('D_RIGHT',1),('D_UP',-10),('D_DOWN',10)):
                    next_at=(at+len(keys)+step)%len(keys)
                    if next_at not in seen:seen.add(next_at);queue.append((next_at,route+[label]))
            for button in route:press(button)
            press('A');cursor=target
    def profile():
        if '--jobs-runtime' in sys.argv:
            target=int(sys.argv[sys.argv.index('--jobs-runtime')+1])
            if not 0<=target<=7:raise ValueError('Job runtime profile must be 0..7')
            for _ in range(8):
                if int(re.findall(r'ANIMALTOWN RUNTIME (\d+)',c.runtime_log_text())[-1])==target:break
                chord('B')
            else:raise AssertionError('Could not select the requested runtime profile')
        # The study mode survives Save & Quit. A second D-right would select
        # AA, allocate replacement display buffers and measure another mode.
        # Only the first capture activates it, and its marker must be fresh.
        modes=re.findall(r'ANIMALTOWN AASTUDY MODE (\d+) (\S+)',c.runtime_log_text())
        if not modes:
            offset=len(c.runtime_log_text());chord('D_RIGHT')
            wait_log(r'ANIMALTOWN AASTUDY MODE 0 480-rim100',offset)
        elif modes[-1]!=('0','480-rim100'):
            raise AssertionError('The job capture is no longer in the requested AA-off mode')
        guest(2)
        offset=len(c.runtime_log_text());started=time.monotonic();chord('D_LEFT')
        begin=wait_log(r'ANIMALTOWN AASTUDY BEGIN (\d+) 0',offset);sequence=int(begin[1]);wait_log(r'ANIMALTOWN AASTUDY END '+str(sequence)+r' 0 240',offset,300);elapsed=time.monotonic()-started;wait_log('PATH PROFILE DONE',offset)
        text=c.runtime_log_text()[offset:]
        rows=[[int(v) for v in r] for r in re.findall(r'ANIMALTOWN AASTUDY ROW '+str(sequence)+r' 0 (\d+) (\d+) (\d+) (\d+) (\d+) (\d+) (\d+)',text)]
        cpu=[[int(v) for v in r] for r in re.findall(r'PATH PROFILE ROW (\d+) (\d+) (\d+) (\d+) (\d+) (\d+)',text)]
        if len(rows)!=240 or len(cpu)!=240:raise AssertionError('Incomplete frame/CPU measurement')
        memory=re.search(r'PATH PROFILE MEMORY native (\d+) texture (\d+) path (\d+) aram (\d+) cache_peak (\d+) cache_reserved (\d+) dma_reads (\d+) bindings (\d+)',text)
        managed=re.findall(r'ANIMALTOWN MEMORY managed_used (\d+) managed_peak (\d+) managed_capacity (\d+) collections (\d+)',c.runtime_log_text())
        system=re.findall(r'ANIMALTOWN SYSTEM MEMORY mem1_used (\d+) mem1_total (\d+) arena_free (\d+) malloc_free (\d+) aram_used (\d+) aram_total (\d+)',c.runtime_log_text())
        pct=lambda values,q:sorted(values)[min(len(values)-1,math.ceil(len(values)*q)-1)]
        times=[r[1] for r in rows[5:]];cpus=[r[1] for r in cpu[5:]]
        return {'hardware':False,'renderer':'Dolphin OpenGL / llvmpipe','frames':240,'host_fps_including_input':240/elapsed,'guest_fps':1e6*len(times)/sum(times),'frame_ms_p50':pct(times,.5)/1000,'frame_ms_p95':pct(times,.95)/1000,'cpu_ms_mean':sum(cpus)/len(cpus)/1000,'cpu_ms_p95':pct(cpus,.95)/1000,'allocations':sum(r[4] for r in cpu),'collections':sum(r[5] for r in cpu),'memory':dict(zip(('native_bytes','texture_bytes','path_bytes','aram_bytes','cache_peak_bytes','cache_reserved_bytes','dma_reads','bindings'),map(int,memory.groups()))) if memory else None,'managed':dict(zip(('used_extent_bytes','peak_extent_bytes','capacity_bytes','collections'),map(int,managed[-1]))) if managed else None,'system_memory':dict(zip(('mem1_used_bytes','mem1_total_bytes','arena_free_bytes','malloc_free_bytes','aram_used_bytes','aram_total_bytes'),map(int,system[-1]))) if system else None,'raw_frames':rows,'raw_cpu':cpu}
    if mode=='template':
        c.wait_title()
        # A read-only title boot can leave Dolphin's new formatted card only
        # in memory. Persist a real console preference before using its raw
        # header as a fixture template; profile-card-fixture rebuilds the files.
        c.set_debug_visibility(True)
        if not (c.root/'MemoryCardA.USA.raw').is_file():raise AssertionError('Dolphin did not format its disposable template')
        return
    if mode in ('recovery','maximum-recovery'):
        c.wait_title();press('A');c.wait_profiles('jobs-recovery-picker.png');shot('locked');press('X');presentations(8);shot('recheck');offset=len(c.runtime_log_text());press('A');wait_log('ANIMALTOWN READY ',offset,1800 if mode.startswith('maximum-') else 600)
    else:c.start_game(False,'jobs-'+mode)
    wait_log('ANIMALTOWN READY ');presentations(2);shot('world')
    report={'scenario':'offline-jobs','mode':mode,'screenshots':shots,'passed':False}
    if mode=='maximum-economy':
        report['profile']=profile();shot('profiled');travel();shot('maximum-guest')
        folder=c.root/'records-guest';folder.mkdir(exist_ok=True)
        for name,data in c.options_card_files(c.root/'MemoryCardB.USA.raw').items():(folder/name).write_bytes(data)
        travel();shot('maximum-returned');save()
    elif mode in ('baseline','outfits-off','outfits-on','maximum-mail'):
        report['profile']=profile();shot('profiled')
        if mode=='maximum-mail':
            save('first-maximum-checkpoint');press('START');press('D_DOWN');press('A');press('D_DOWN');press('A');c.wait_title();shot('maximum-title')
            # The title restores saved profiling settings. Persistence needs
            # a real reopened checkpoint; the first capture measured full mail.
            c.start_game(False,'jobs-maximum-reload');wait_log('ANIMALTOWN READY ');shot('maximum-reloaded');save('reopened-checkpoint')
        if '--jobs-checkpoints' in sys.argv:
            save('first-checkpoint');move(1,0,.25);save('next-checkpoint')
    elif mode=='checkpoint':
        move(1,0,.25);save()
    elif mode=='retrieval-multi':
        import shutil
        accept();reloaded=False
        for stage in range(8):
            before=next(j for j in view()['jobs'] if j[1]==4)
            if before[2]==3:break
            target=before[4];handoff(npc=target)
            after=next(j for j in view()['jobs'] if j[1]==4)
            if after[2]==3:break
            if after[4]==target:
                shot('full-pockets');press('X');press('A');press('A');dialogue();close();close();handoff(npc=target)
            elif not reloaded:
                save('midstage-saved');shutil.copytree(c.root/'records',c.root/'records-mid',dirs_exist_ok=True)
                press('START');press('D_DOWN');press('A');press('D_DOWN');press('A');c.wait_title();shot('midstage-title')
                c.start_game(False,'jobs-multi-reload');wait_log('ANIMALTOWN READY ');shot('midstage-reloaded');reloaded=True
        else:raise AssertionError('Retrieval did not reach its return stage')
        if not reloaded:raise AssertionError('Multi-borrower fixture did not take a referral')
        handoff();save()
    elif mode=='destroy-snowman':
        body=next(o for o in view()['objects'].values() if o[0]==3);go(body[1]/1024,body[2]/1024+1,.1);press('Z');press('A');dialogue();shot('destroyed');close()
        if any(o[0]==3 for o in view()['objects'].values()):raise AssertionError('The actual shovel action did not destroy the snowman')
        save()
    elif mode=='owed':
        save('owed-saved');press('START');press('D_DOWN');press('A');press('D_DOWN');press('A');c.wait_title();shot('owed-title')
        c.start_game(False,'jobs-owed-reload');wait_log('ANIMALTOWN READY ');shot('owed-reloaded')
        if view()['room']!=0:raise AssertionError('Owed fixture must begin outdoors')
        go(50.5,29.5,.1);move(0,-1,.04);press('A');guest(1);go(.5,3.25,.1);press('A');shot('bank-deposit');press('A');dialogue();close()
        go(2,3.35,.08);move(0,1,.04);press('A');guest(1);handoff(npc=1);shot('owed-collected');save()
    elif mode=='letter-full':
        accept();go(.5,3.25,.1);press('A')
        for _ in range(5):press('D_DOWN')
        press('A');shot('recipient');press('A');type_text('Hello!');shot('composed');press('START');press('D_DOWN');press('A');dialogue();close()
        guest(130);handoff();shot('mailbox-full-retry');go(.5,3.25,.1);press('A')
        for _ in range(4):press('D_DOWN')
        press('A');press('A');shot('discard-old-letter');press('Y');press('D_DOWN');press('A');close();close()
        handoff();shot('reply-retried');go(.5,3.25,.1);press('A')
        for _ in range(4):press('D_DOWN')
        press('A');press('D_UP');shot('reply-mailbox');press('A');shot('reply');press('X');close();close();save()
    elif mode=='mail':
        # Fixture starts beside the front door of the resident's own house.
        press('A');guest(1)
        for _ in range(4):press('D_DOWN')
        press('A');shot('mailbox');press('A');guest(1);shot('letter');press('X');guest(1);shot('attachment');close();close();save()
    elif mode=='room':
        shot('before');press('X');press('A');press('A');dialogue();close();shot('floor')
        press('X');press('D_RIGHT');press('A');press('A');dialogue();close();shot('walls');save()
    elif mode=='moving':
        go(36.5,11.5,.1);press('A');presentations(4);shot('travel');press('Y');press('D_DOWN');shot('moving');press('A');press('D_DOWN');shot('confirmation')
        offset=len(c.runtime_log_text());press('A');wait_log('ANIMALTOWN READY ',offset,600);presentations(4);shot('moved');save()
    elif mode in ('recovery','maximum-recovery'):
        shot('recovered')
        if mode=='maximum-recovery':
            folder=c.root/'records-guest';folder.mkdir(exist_ok=True)
            for name,data in c.options_card_files(c.root/'MemoryCardB.USA.raw').items():(folder/name).write_bytes(data)
        travel();shot('returned');save()
    else:
        accept()
        if mode in ('delivery','property','retrieval'):
            press('X');shot('parcel');press('R');press('A');shot('jobs-log');close();close()
            handoff(npc=1)
            if mode=='retrieval':handoff()
        elif mode in ('foreign','moved'):
            travel();handoff();travel()
        elif mode in ('fruit','fish','insect'):handoff(True)
        elif mode=='ball':
            for _ in range(60):
                v=view()
                if any(j[1]==6 and j[2]==4 for j in v['jobs']):break
                ball=next(o for o in v['objects'].values() if o[0]==1);n=v['npcs'][0];dx=n[1]/1024-ball[1]/1024;dz=n[2]/1024-ball[2]/1024;d=math.hypot(dx,dz)
                bx,bz=ball[1]/1024,ball[2]/1024;ux,uz=dx/d,dz/d
                if (v['x']-bx)*ux+(v['z']-bz)*uz>0:go(bx-uz*1.5,bz+ux*1.5,.15)
                go(bx-ux*.75,bz-uz*.75,.1);move(ux,uz,.6,True)
            shot('ball');handoff()
        elif mode=='snowman':go(30,41.5);move(0,1,1);shot('snowman');handoff()
        elif mode=='flowers':
            go(25,42.5,.15)
            for pocket in range(1,5):
                move(0,-1,.025);press('X')
                for _ in range(pocket):press('D_RIGHT')
                press('A');press('D_DOWN');press('A');guest(2);close();close();move(1,0,.38)
            shot('planted');handoff()
        elif mode=='letter':
            go(.5,3.25,.1);press('A')
            for _ in range(5):press('D_DOWN')
            press('A');shot('recipient');press('A');type_text('Hello!');shot('composed');press('START');press('D_DOWN');shot('send-confirm');press('A');dialogue();close()
            guest(130);handoff();go(.5,3.25,.1);press('A')
            for _ in range(4):press('D_DOWN')
            press('A');shot('reply-mailbox');press('A');shot('reply');press('X');close();close()
        else:raise ValueError('Unsupported job smoke mode: '+mode)
        save()
    if mode in ('foreign','moved','moving','recovery','maximum-economy','maximum-recovery'):
        folder=c.root/'records-b';folder.mkdir(exist_ok=True)
        for name,data in c.options_card_files(c.root/'MemoryCardB.USA.raw').items():(folder/name).write_bytes(data)
    alive();report['controls_completed']=True
    (c.root/'jobs-report.json').write_text(json.dumps(report,indent=2)+'\n')
