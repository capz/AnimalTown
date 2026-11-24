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

def write_records(folder,files):
    """Replace a generated export, including records deleted since a checkpoint."""
    import hashlib
    import shutil
    if folder.exists():shutil.rmtree(folder)
    folder.mkdir()
    result=[]
    for name,data in sorted(files.items()):
        if not name or Path(name).name!=name or name in ('.','..'):raise AssertionError('Invalid logical record name: '+name)
        (folder/name).write_bytes(data)
        result.append({'name':name,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest()})
    return result

def finish(context,report,shutdown):
    """Export stable final cards; the companion host verifier still loads them."""
    import hashlib
    c=SimpleNamespace(**context)
    report['shutdown']=shutdown
    exported=report['final_export']={'complete':False,'cards':{}}
    try:
        if report.get('controls_completed') is not True:raise AssertionError('Offline job controls did not complete')
        if c.p.poll()!=0 or c.p.returncode!=0 or shutdown['returncode']!=0 or shutdown['forced_kill'] or not shutdown['clean_exit']:
            raise AssertionError('Offline job final export requires a clean Dolphin exit without a forced kill')
        c.check_runtime_failure()
        for slot,folder in (('A','records'),('B','records-b')):
            card=c.root/f'MemoryCard{slot}.USA.raw'
            info=exported['cards'][slot]={'card':card.name,'present':card.is_file(),'directory':folder}
            if info['present']:
                data=card.read_bytes();info.update(bytes=len(data),sha256=hashlib.sha256(data).hexdigest())
                files=c.options_card_files(card)
                info['records']=write_records(c.root/folder,files)
                info['sequence']=c.committed_card_sequence(card)
            else:
                info['records']=write_records(c.root/folder,{})
                info['sequence']=0
        expected=report.get('expected_saved_sequence')
        if expected is not None and exported['cards']['A']['sequence']<expected:
            raise AssertionError(f'Final card is older than the observed explicit save: expected at least {expected}, got '+str(exported['cards']['A']['sequence']))
        exported['complete']=True
    except BaseException as error:
        report['error']=str(error)
        raise
    finally:
        (c.root/'jobs-report.json').write_text(json.dumps(report,indent=2)+'\n')

def run(context):
    c=SimpleNamespace(**context)
    mode=sys.argv[sys.argv.index('--jobs')+1]
    sys.path.insert(0,str(Path(c.__file__).resolve().parents[1]/'docs/performance'))
    from avi_guest_clock import AviGuestClock
    clock=AviGuestClock(c.user/'Dump/Frames',track_presentations=True)
    shots=[]
    report={'scenario':'offline-jobs','mode':mode,'screenshots':shots,'passed':False,
            'controls_completed':False,'expected_saved_sequence':None}
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
    def save(label='saved'):
        previous=c.committed_card_sequence(c.root/'MemoryCardA.USA.raw',details=True)
        if mode!='baseline':
            for _ in range(6):
                if view()['screen']=='world':break
                press('B')
            else:raise AssertionError('Could not close the interaction before saving')
        offset=len(c.runtime_log_text());press('START');os.write(c.controller,b'PRESS A\n')
        try:report['expected_saved_sequence']=c.wait_saved_after(previous,timeout_seconds=900 if mode.startswith('maximum-') else 240)['sequence']
        finally:os.write(c.controller,b'RELEASE A\n')
        wait_log('ANIMALTOWN SAVE TOTAL:',offset,900);presentations(4);shot(label);press('B')
    def travel(capture=None):
        go(36.5,11.5,.1);press('A');presentations(4);shot('travel');press('A');press('D_DOWN');shot('travel-confirm')
        offset=len(c.runtime_log_text());press('A')
        timeout=1800 if mode.startswith('maximum-') else 360
        source=wait_log(r'ANIMALTOWN FLIGHT BOARD (-?\d+)',offset,timeout)
        if capture:guest(.4);shot(capture+'-boarding')
        direction=wait_log(r'ANIMALTOWN FLIGHT CRUISE (-?\d+)',offset,timeout)
        if capture:presentations(3);shot(capture+'-cruise')
        destination=wait_log(r'ANIMALTOWN FLIGHT DESTINATION (-?\d+)',offset,timeout)
        wait_log('ANIMALTOWN READY ',offset,timeout)
        wait_log('ANIMALTOWN FLIGHT LAND '+destination[1],offset,timeout)
        if capture:guest(.8);shot(capture+'-landing')
        wait_log('ANIMALTOWN FLIGHT ARRIVED '+destination[1],offset,timeout)
        if capture:
            present=view()
            if present['screen']!='world' or present['room']!=0 or math.hypot(present['x']-36,present['z']-11.3)>.12:
                raise AssertionError('Player did not regain control on the grass at the destination pad')
            shot(capture+'-offloaded')
        return dict(source=int(source[1]),destination=int(destination[1]),direction=int(direction[1]))
    def type_text(text):
        # Each caller has just opened a fresh, lowercase composer. Its shared
        # chat grid wraps within rows/columns; multiline mode replaces SP with
        # NL at index 40, while R always inserts a space without moving the key.
        keys="qwertyuiopasdfghjkl'zxcvbnm,./0123456789\n!?-:;()@+"
        cursor=0;uppercase=False
        for char in text:
            if char==' ':press('R');continue
            capital='A'<=char<='Z';letter=capital or 'a'<=char<='z'
            target=keys.index(char.lower() if capital else char)
            if letter and uppercase!=capital:press('L');uppercase=capital
            row,col=divmod(cursor,10);target_row,target_col=divmod(target,10)
            down=(target_row-row)%5;right=(target_col-col)%10
            for _ in range(min(down,5-down)):press('D_DOWN' if down<=2 else 'D_UP')
            for _ in range(min(right,10-right)):press('D_RIGHT' if right<=5 else 'D_LEFT')
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
        alive();report['controls_completed']=True
        return report
    if mode in ('recovery','maximum-recovery'):
        c.wait_title();press('A');c.wait_profiles('jobs-recovery-picker.png');shot('locked');press('X');presentations(8);shot('recheck');offset=len(c.runtime_log_text());press('A');wait_log('ANIMALTOWN READY ',offset,1800 if mode.startswith('maximum-') else 600)
    else:c.start_game(False,'jobs-'+mode)
    wait_log('ANIMALTOWN READY ');presentations(2);shot('world')
    if mode=='helipad':
        outbound=travel('outbound');inbound=travel('inbound')
        if outbound!={'source':7319,'destination':7320,'direction':1} or inbound!={'source':7320,'destination':7319,'direction':-1}:
            raise AssertionError('The helipad did not complete a two-town round trip with opposite flight directions')
        report['flights']=[outbound,inbound];save()
    elif mode=='maximum-economy':
        report['profile']=profile();shot('profiled');travel();shot('maximum-guest')
        write_records(c.root/'records-guest',c.options_card_files(c.root/'MemoryCardB.USA.raw'))
        travel();shot('maximum-returned');save()
    elif mode in ('baseline','outfits-off','outfits-on','maximum-mail','maximum-post-travel'):
        report['profile']=profile();shot('profiled')
        if mode in ('maximum-mail','maximum-post-travel'):
            save('first-maximum-checkpoint');press('START');press('D_DOWN');press('A');press('D_DOWN');press('A');c.wait_title();shot('maximum-title')
            # The title restores saved profiling settings. Persistence needs
            # a real reopened checkpoint; the first capture measured full mail.
            c.start_game(False,'jobs-maximum-reload');wait_log('ANIMALTOWN READY ');shot('maximum-reloaded');save('reopened-checkpoint')
        if '--jobs-checkpoints' in sys.argv:
            save('first-checkpoint');move(1,0,.25);save('next-checkpoint')
    elif mode=='checkpoint':
        move(1,0,.25);save()
    elif mode=='retrieval-multi':
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
                save('midstage-saved');write_records(c.root/'records-mid',c.options_card_files())
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
        # Exercise the actual backpack entry points before reading incoming mail.
        # The final saved-record verifier checks exact paper/gift consumption.
        press('X');press('A');shot('stationery-actions');press('A')
        if view()['screen']!='letter-recipient':raise AssertionError('Stationery lacks its working Write a letter action')
        shot('backpack-recipient');press('B')
        if view()['screen']!='menu' or view()['selected']!=0:raise AssertionError('Recipient back lost the originating backpack slot')
        press('A');press('A');press('A');press('Y');shot('eligible-presents')
        press('B');press('B');press('D_DOWN');press('A')
        if view()['screen']!='menu' or view()['selected']!=0:raise AssertionError('Discarding the draft did not return to the backpack')
        press('B');press('X');press('D_DOWN');press('A');shot('gift-actions')
        press('D_DOWN');press('A')
        if view()['screen']!='letter-recipient':raise AssertionError('Gift lacks its working Attach to letter action')
        press('A');type_text('Hello!');shot('backpack-present-draft');press('START');press('D_DOWN');press('A');dialogue();close()
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
            write_records(c.root/'records-guest',c.options_card_files(c.root/'MemoryCardB.USA.raw'))
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
    alive();report['controls_completed']=True
    return report
